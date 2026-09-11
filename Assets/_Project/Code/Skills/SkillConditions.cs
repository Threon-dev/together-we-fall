using Unity.Mathematics;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// What a support may insist on before it does anything.
    ///
    /// The second axis of a build. A support gem answers "what does this skill
    /// do differently"; a condition on that gem answers "and when" — which is
    /// what turns a flat number into a reason to arrange a fight. "Chain, but
    /// only into burning bodies" is a different gem from "Chain" without a
    /// single new mechanism behind it.
    /// </summary>
    public enum ModifierConditionType : byte
    {
        /// <summary>No condition. What every support authored before this existed is.</summary>
        None = 0,

        /// <summary>The target is carrying the required element.</summary>
        TargetHasStatus = 1,

        /// <summary>The target is at or below the threshold fraction of its health.</summary>
        TargetLowHealth = 2,

        /// <summary>
        /// The caster was hurt recently.
        ///
        /// The one condition with no source: players cannot be damaged yet. It
        /// is here for the same reason the unwritten trigger conditions are —
        /// the seam is one line beside the code that will know, and an enum
        /// everybody has to agree to extend later is worse than a value that is
        /// honestly documented as early.
        /// </summary>
        CasterRecentlyHit = 3,

        /// <summary>
        /// The supported skill's own element is the required one. "Only helps
        /// fire skills."
        ///
        /// Read from what the skill was AUTHORED as, not from what it ends up
        /// dealing. A conversion support is itself part of the same fold, so
        /// asking about the converted element would make the answer depend on
        /// the order two gems happen to sit in — and a build whose numbers move
        /// when gems are swapped between two holes that link identically is a
        /// build nobody can reason about.
        /// </summary>
        TargetElementType = 4,

        /// <summary>
        /// The target is under the required status.
        ///
        /// The wider sibling of TargetHasStatus, which asks about elements. Two
        /// values rather than one because the sets genuinely differ: an element
        /// is also what a projectile can pick up in flight and what a blow
        /// arrives carrying, while a status is only ever something on a body.
        /// Collapsing them would mean asking "is this target stunned" of a byte
        /// that has no room for the answer.
        ///
        /// It is what makes control worth applying to something other than the
        /// enemy in front of you: "chain, but only into rooted bodies" turns a
        /// root from a defensive move into the setup for the next one.
        /// </summary>
        TargetHasStatusEffect = 5
    }

    /// <summary>
    /// Everything a condition may ask about, gathered once per cast.
    ///
    /// Gathered rather than looked up per modifier because the expensive half is
    /// finding the target at all, and five supports asking about the same body
    /// should cost one search. Gathered ONLY when at least one support has a
    /// condition, so a build with none pays nothing — the same shape as
    /// StatsDirty: the work is unreachable unless something says it is needed.
    ///
    /// The target here is the one the cast is AIMED at, not each body the skill
    /// eventually touches. That is a real limitation and a deliberate one: a
    /// cast-phase support decides how many projectiles exist and how big the
    /// blast is, and by the time a projectile lands that decision is a frame
    /// old. Asking per victim would mean carrying the whole conditional set
    /// through the projectile, the hit and the blast — the same three structs
    /// the gem link group was deferred over, for the same cost.
    ///
    /// In practice the two coincide: a bolt strikes what it acquired, a swing
    /// hits what is in front, a burst goes off where you pointed, and a
    /// projectile flies in a straight line at the thing you aimed at.
    /// </summary>
    public struct CastConditions
    {
        /// <summary>Whether there was anything to aim at. False makes every target condition false.</summary>
        public bool HasTarget;

        /// <summary>Which elements the aimed target is carrying, as an ElementMask.</summary>
        public byte TargetElements;

        /// <summary>Everything on the aimed target, as a StatusMask.</summary>
        public ushort TargetStatuses;

        /// <summary>The aimed target's health, from one down to zero.</summary>
        public float TargetHealthFraction;

        /// <summary>Whether the caster was hurt recently. Always false today.</summary>
        public bool CasterRecentlyHit;

        /// <summary>The supported skill as authored, before any conversion.</summary>
        public DamageType SkillElement;

        /// <summary>
        /// Nothing known, which answers "no" to everything except a support that
        /// asked for nothing. The state a triggered cast starts from, and the
        /// state a cast with no conditional supports never leaves.
        /// </summary>
        public static CastConditions Unknown(DamageType skillElement) => new CastConditions
        {
            HasTarget = false,
            TargetElements = 0,
            TargetStatuses = 0,
            TargetHealthFraction = 1f,
            CasterRecentlyHit = false,
            SkillElement = skillElement
        };
    }

    /// <summary>
    /// The one place a condition is turned into yes or no.
    ///
    /// A struct of static methods, like GridFit and EquipmentSlots: the fold
    /// asks it to decide whether a support counts, and the panel asks it to
    /// describe the gem in a tooltip. Two callers, one rule.
    /// </summary>
    public struct SkillConditions
    {
        /// <summary>
        /// Whether this support applies at all right now.
        ///
        /// An unconditional support — which is every support authored before
        /// this feature — answers yes without touching anything, so the check
        /// costs one comparison on the path that every existing build takes.
        /// </summary>
        public static bool IsMet(in SkillModifierBlob modifier, in CastConditions conditions)
        {
            switch (modifier.Condition)
            {
                case ModifierConditionType.None:
                    return true;

                case ModifierConditionType.TargetHasStatus:
                    return conditions.HasTarget &&
                           ElementMask.Has(conditions.TargetElements, modifier.RequiredElement);

                case ModifierConditionType.TargetLowHealth:
                    return conditions.HasTarget &&
                           conditions.TargetHealthFraction <=
                           math.clamp(modifier.Threshold, 0f, 1f);

                case ModifierConditionType.CasterRecentlyHit:
                    return conditions.CasterRecentlyHit;

                case ModifierConditionType.TargetElementType:
                    return conditions.SkillElement == modifier.RequiredElement;

                case ModifierConditionType.TargetHasStatusEffect:
                    return conditions.HasTarget &&
                           StatusMask.Has(conditions.TargetStatuses, modifier.RequiredStatus);

                default:
                    return true;
            }
        }

        /// <summary>Whether any support in this list wants the context gathered.</summary>
        public static bool AnyConditional(
            in Unity.Collections.FixedList512Bytes<SkillModifierBlob> supports)
        {
            for (int i = 0; i < supports.Length; i++)
            {
                if (supports[i].Condition != ModifierConditionType.None)
                    return true;
            }

            return false;
        }

        /// <summary>Short words for a tooltip. Empty for an unconditional support.</summary>
        public static string Describe(in SkillModifierBlob modifier)
        {
            switch (modifier.Condition)
            {
                case ModifierConditionType.TargetHasStatus:
                    return $"only vs {modifier.RequiredElement}";

                case ModifierConditionType.TargetLowHealth:
                    return $"only below {modifier.Threshold * 100f:0}% life";

                case ModifierConditionType.CasterRecentlyHit:
                    return "only when recently hit";

                case ModifierConditionType.TargetElementType:
                    return $"only on {modifier.RequiredElement} skills";

                case ModifierConditionType.TargetHasStatusEffect:
                    return $"only vs {modifier.RequiredStatus}";

                default:
                    return string.Empty;
            }
        }
    }
}
