using Unity.Collections;
using Unity.Entities;

namespace TogetherWeFall.Combat
{
    /// <summary>
    /// What happens when two elements meet.
    ///
    /// Deliberately three outcomes and not four. "Consume and spread" from the
    /// original sketch is not a fourth mechanism: now that any elemental blow
    /// leaves its element behind, a blast IS how something spreads — everything
    /// it catches takes elemental damage and is marked by it. Two names for one
    /// mechanism is the thing that later drifts into two behaviours.
    /// </summary>
    public enum ElementReactionKind : byte
    {
        /// <summary>No rule authored for this pair. The default, and the common case.</summary>
        None = 0,

        /// <summary>This blow hits harder.</summary>
        BonusDamage = 1,

        /// <summary>The pair leaves something new behind — steam, brittleness, whatever is authored.</summary>
        ApplyStatus = 2,

        /// <summary>The pair goes off, hurting everything around the target.</summary>
        Explosion = 3
    }

    /// <summary>
    /// One status effect, as data.
    ///
    /// Safe to copy, unlike ItemBlob and SkillBlob: there is no BlobArray inside
    /// it, so it has no self-relative offsets to invalidate. That is why the
    /// lookups below hand these back by value.
    /// </summary>
    public struct StatusBlob
    {
        public FixedString64Bytes Name;

        /// <summary>
        /// What this status is. The key it is stored under on a target, and the
        /// name a skill refers to it by across two separately baked databases.
        /// </summary>
        public StatusEffectType Type;

        /// <summary>
        /// The element it marks with. Meaningless for a status that carries
        /// none — see StatusEffects.CarriesElement.
        /// </summary>
        public DamageType Element;

        public float Duration;
        public int MaxStacks;

        /// <summary>Damage per second while it lasts. Zero for a status that only marks.</summary>
        public float DamagePerSecond;

        /// <summary>Seconds between ticks. A tick is worth an interval's worth of damage.</summary>
        public float TickInterval;

        /// <summary>
        /// What one stack is worth to whichever number this status scales: the
        /// fraction a slow takes away, the fraction vulnerability adds.
        ///
        /// Unsigned. Which way it goes is a fact about the type, not about the
        /// asset, so "Haste, minus thirty percent" cannot be authored.
        /// </summary>
        public float MagnitudePerStack;

        /// <summary>
        /// Whether applying it again adds time instead of refreshing it.
        ///
        /// Refreshing is what control wants — being stunned during a stun
        /// should not lengthen it — and adding is what a poison wants. One flag
        /// rather than two application paths, and the default is the safe one.
        /// </summary>
        public bool StacksDuration;

        /// <summary>
        /// Multiple of this status's duration that its target is immune for
        /// afterwards. Read only for hard control; ignored by everything else.
        /// </summary>
        public float ImmunityMultiplier;
    }

    /// <summary>One rule: what an incoming element does to a target already carrying another.</summary>
    public struct ElementReactionRuleBlob
    {
        public ElementReactionKind Kind;

        /// <summary>Multiplies the blow for BonusDamage, and the blast for Explosion.</summary>
        public float DamageMultiplier;

        /// <summary>Status left behind by ApplyStatus, as an index, or -1.</summary>
        public int ResultStatus;

        /// <summary>Whether the element that was already there is spent doing this.</summary>
        public bool ConsumesExisting;

        /// <summary>Blast radius for Explosion. Ignored otherwise.</summary>
        public float Radius;
    }

    /// <summary>
    /// Every reaction in the game, and every status that can be applied.
    ///
    /// The rules are a square table indexed by the pair rather than a list to
    /// search: with a handful of elements the whole thing is twenty-five entries,
    /// a lookup is arithmetic, and two rules claiming the same pair become a
    /// collision the baker can complain about instead of a race between whichever
    /// one the search reaches first.
    /// </summary>
    public struct ElementReactionBlob
    {
        public BlobArray<StatusBlob> Statuses;

        /// <summary>
        /// What each element leaves behind on its own, as an index into Statuses
        /// or -1. This is the whole of "status application": a blow marks its
        /// target with its own element, and the table says what that is called.
        /// </summary>
        public BlobArray<int> DefaultStatus;

        /// <summary>
        /// Where each status type lives in Statuses, or -1.
        ///
        /// StatusMask.Count long, indexed by the type itself. It exists because
        /// a skill names the status it applies, and a skill is baked by a
        /// different authoring object than this table — an index would mean
        /// whichever order that object happened to produce. The type enum is the
        /// vocabulary the two share, exactly as the FNV id is for items and
        /// skills, and this array is how a name becomes an index once.
        /// </summary>
        public BlobArray<int> StatusByType;

        /// <summary>ElementMask.Count squared, indexed existing * Count + incoming.</summary>
        public BlobArray<ElementReactionRuleBlob> Rules;
    }

    /// <summary>
    /// Handle to the reaction table, and the only place a pair of elements is
    /// turned into an outcome.
    ///
    /// The lookups live on the component rather than on the blob struct for the
    /// reason that runs through this project: a method on the blob would take
    /// `this` by value, and copying a struct with a BlobArray in it quietly
    /// breaks every offset inside.
    ///
    /// Adding a combination is a new asset and a re-bake. No system knows that
    /// fire and lightning are interesting together — they only know how to ask.
    /// </summary>
    public struct ElementReactionDatabase : IComponentData
    {
        public BlobAssetReference<ElementReactionBlob> Value;

        /// <summary>
        /// The rule for one ordered pair. Ordered on purpose: fire onto a shocked
        /// target and lightning onto a burning one are different sentences, and
        /// authors should be able to say only one of them.
        /// </summary>
        public bool TryGetRule(
            DamageType existing, DamageType incoming, out ElementReactionRuleBlob rule)
        {
            rule = default;

            if (!Value.IsCreated || existing == incoming)
                return false;

            ref ElementReactionBlob blob = ref Value.Value;

            int index = (int)existing * ElementMask.Count + (int)incoming;
            if (index < 0 || index >= blob.Rules.Length)
                return false;

            rule = blob.Rules[index];
            return rule.Kind != ElementReactionKind.None;
        }

        /// <summary>The status an element leaves on its own, or -1 if it leaves none.</summary>
        public int DefaultStatusOf(DamageType element)
        {
            if (!Value.IsCreated)
                return -1;

            ref ElementReactionBlob blob = ref Value.Value;

            int index = (int)element;
            return index >= 0 && index < blob.DefaultStatus.Length ? blob.DefaultStatus[index] : -1;
        }

        public bool TryGetStatus(int index, out StatusBlob status)
        {
            status = default;

            if (!Value.IsCreated || index < 0 || index >= Value.Value.Statuses.Length)
                return false;

            status = Value.Value.Statuses[index];
            return true;
        }

        /// <summary>
        /// One status by name, for whoever holds a type rather than an index —
        /// a skill that applies a stun, a keystone that adds vulnerability.
        ///
        /// A status type that nobody authored an asset for answers no, and the
        /// caller does nothing. That is the right failure: a skill referring to
        /// a status the reaction table has never heard of should be inert rather
        /// than inventing default numbers for it.
        /// </summary>
        public bool TryGetStatusOfType(StatusEffectType type, out int index, out StatusBlob status)
        {
            index = -1;
            status = default;

            if (!Value.IsCreated || type == StatusEffectType.None)
                return false;

            ref ElementReactionBlob blob = ref Value.Value;

            int slot = (int)type;
            if (slot < 0 || slot >= blob.StatusByType.Length)
                return false;

            index = blob.StatusByType[slot];
            return TryGetStatus(index, out status);
        }
    }
}
