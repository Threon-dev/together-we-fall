using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// One support socketed into a skill.
    ///
    /// The fields are grouped by size rather than by meaning, on purpose: five
    /// bytes and six four-byte values pack into thirty-two, and this struct is
    /// gathered by value into a fixed list once per cast. Interleaving them
    /// would pad it to forty-four and cost a fifth of the list capacity for
    /// nothing.
    /// </summary>
    public struct SkillModifierBlob
    {
        public SkillModifierKind Kind;

        public DamageType ConvertTo;

        /// <summary>What makes a TriggerOnCondition support fire. Ignored by the rest.</summary>
        public TriggerConditionType TriggerCondition;

        /// <summary>
        /// What must be true for this support to count at all, or None.
        ///
        /// On every kind rather than on a separate sort of gem, which is the
        /// whole economy of the idea: "chain" and "chain into burning bodies"
        /// are one mechanism and one authored field apart, and every support
        /// that already exists gets the option for free.
        /// </summary>
        public ModifierConditionType Condition;

        /// <summary>Which element the condition is about. See ModifierConditionType.</summary>
        public DamageType RequiredElement;

        public float Value;

        /// <summary>Second number, where one is not enough. See SkillModifier.</summary>
        public float SecondaryValue;

        /// <summary>
        /// Skill this support triggers, by stable id, or zero for none.
        ///
        /// An id rather than an index because a support can now arrive from a
        /// gem, and the item baker has no idea what order the skill database
        /// ended up in. Resolve turns it into an index once, at cast time.
        ///
        /// Only TriggerOnHit uses it. A condition trigger names no skill: it
        /// fires the actives already linked to it.
        /// </summary>
        public int TriggeredSkillId;

        /// <summary>
        /// How often a condition trigger actually goes off, from zero to one.
        ///
        /// Mandatory rather than a nicety. A trigger that is certain turns every
        /// kill in a wave into a cast, and the interesting version of "cast on
        /// kill" is the one you cannot count on.
        /// </summary>
        public float ProcChance;

        /// <summary>Seconds a condition trigger must wait between firings.</summary>
        public float TriggerCooldown;

        /// <summary>
        /// The number the condition compares against: a fraction of health for
        /// TargetLowHealth and for OnLowHealth. Ignored by conditions that
        /// compare nothing.
        /// </summary>
        public float Threshold;
    }

    /// <summary>
    /// One skill and everything socketed into it.
    ///
    /// NEVER copy by value: the modifier list is a BlobArray, whose offset is
    /// relative to its own address. Always take it as `ref SkillBlob`.
    /// </summary>
    public struct SkillBlob
    {
        public FixedString64Bytes Name;

        /// <summary>What an active gem names this skill by.</summary>
        public int SkillId;
        public SkillEffectKind Effect;
        public DamageType Type;

        public float BaseDamage;
        public float Cooldown;

        /// <summary>How far the skill reaches, and how far a projectile travels.</summary>
        public float Range;

        /// <summary>Blast radius, arc radius, or impact radius. Zero means single target.</summary>
        public float Radius;

        /// <summary>Full width of a melee arc. Three hundred and sixty is a circle.</summary>
        public float ArcDegrees;

        public float ProjectileSpeed;

        /// <summary>How long a persistent zone lasts. Ignored by every other effect.</summary>
        public float ZoneDuration;

        /// <summary>Seconds between a zone's damage pulses.</summary>
        public float ZoneTickInterval;

        public int BaseChains;
        public float ChainRange;
        public float ChainDelay;

        /// <summary>
        /// Supports authored onto the skill itself.
        ///
        /// These are now INNATE behaviour rather than the build: a skill that is
        /// meant to chain by its nature says so here. Everything a player
        /// chooses arrives from gems in the same link group and is folded on top
        /// of these. Emptying this array on an asset is all it takes to make a
        /// skill purely what its gems say it is.
        /// </summary>
        public BlobArray<SkillModifierBlob> Modifiers;
    }

    /// <summary>Every skill in the game, addressed by index.</summary>
    public struct SkillDatabaseBlob
    {
        public BlobArray<SkillBlob> Skills;
    }

    /// <summary>
    /// A skill after its supports and the caster have been folded in. What the
    /// cast actually uses.
    /// </summary>
    public struct ResolvedSkill
    {
        public SkillEffectKind Effect;
        public DamageType Type;

        public float Damage;
        public float Cooldown;
        public float Range;
        public float Radius;
        public float ArcCosine;
        public float ProjectileSpeed;

        public float ZoneDuration;
        public float ZoneTickInterval;

        /// <summary>How many times the whole skill goes off. Multicast raises it.</summary>
        public int Casts;

        public int Forks;
        public int Chains;
        public float ChainRange;
        public float ChainDelay;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>Skill to cast where this one lands, or -1.</summary>
        public int TriggerSkillIndex;

        /// <summary>Fraction of the triggered skill's own damage. One is full.</summary>
        public float TriggerDamageScale;
    }

    /// <summary>
    /// Handle to the skill database, and the place supports are applied.
    ///
    /// The fold happens here, once per cast, rather than in a system per
    /// modifier. That is a deliberate departure from "each modifier is its own
    /// system": most supports change how many things exist or how big they are,
    /// which is a decision to make BEFORE anything exists — a system reading a
    /// damage event has already missed the moment. The result would be a dozen
    /// systems that iterate nothing on almost every frame, and the one place to
    /// look to answer "why is my damage this number" would be spread across all
    /// of them.
    ///
    /// What genuinely does belong downstream is here too, just not as a fold:
    /// chains happen at hit time, forks at impact, explosions at death. Those
    /// are behaviours, and they live in the systems that own those moments.
    ///
    /// Resolve lives on this component rather than on the blob struct because a
    /// method on the blob would take `this` by value, and copying a struct that
    /// contains a BlobArray quietly breaks it.
    /// </summary>
    public struct SkillDatabase : IComponentData
    {
        public BlobAssetReference<SkillDatabaseBlob> Value;

        public bool IsValidIndex(int index)
            => Value.IsCreated && index >= 0 && index < Value.Value.Skills.Length;

        /// <summary>
        /// Index of a skill by its stable id, or -1.
        ///
        /// A scan rather than a binary search: there are four skills, and
        /// sorting the database by id would scramble the order the loadout and
        /// the content factory both address it by.
        /// </summary>
        public int IndexOf(int skillId)
        {
            if (!Value.IsCreated || skillId == 0)
                return -1;

            ref SkillDatabaseBlob blob = ref Value.Value;

            for (int i = 0; i < blob.Skills.Length; i++)
            {
                if (blob.Skills[i].SkillId == skillId)
                    return i;
            }

            return -1;
        }

        public FixedString64Bytes NameOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Name : default;

        /// <summary>
        /// How far a skill reaches, before the fold.
        ///
        /// Needed by the one caller that has to look at the world BEFORE it can
        /// fold: a conditional support asks about the target, and finding the
        /// target needs a range. Range is not scaled by any support, so asking
        /// early and asking late give the same answer — which is what makes this
        /// safe rather than a second source of truth.
        /// </summary>
        public float RangeOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Range : 0f;

        /// <summary>
        /// Everything the supports of one cast add up to, before the maths.
        ///
        /// Its own struct so that innate supports and gem supports run through
        /// exactly the same code. Two loops with the same body is how the two
        /// sources start disagreeing about what Multicast means.
        /// </summary>
        private struct Fold
        {
            public float IncreasedDamage;
            public float IncreasedArea;
            public float IncreasedSpeed;

            public int Chains;
            public int Forks;
            public int Casts;

            public DamageType Type;
            public float ExplosionRadius;
            public float ExplosionShare;

            public int TriggerId;
            public float TriggerShare;
        }

        /// <summary>
        /// Folds one support in, unless its condition says otherwise.
        ///
        /// The condition check sits here rather than in the two loops below so
        /// that innate supports and gem supports go through it identically —
        /// the same reason this method exists at all. It is one comparison for
        /// every support authored before conditions existed, because None is
        /// the first case.
        /// </summary>
        private static void Accumulate(
            in SkillModifierBlob modifier, ref Fold fold, in CastConditions conditions)
        {
            if (!SkillConditions.IsMet(modifier, conditions))
                return;

            switch (modifier.Kind)
            {
                case SkillModifierKind.IncreasedDamage:
                    fold.IncreasedDamage += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedArea:
                    fold.IncreasedArea += modifier.Value;
                    break;

                case SkillModifierKind.IncreasedProjectileSpeed:
                    fold.IncreasedSpeed += modifier.Value;
                    break;

                case SkillModifierKind.AddedChains:
                    fold.Chains += (int)modifier.Value;
                    break;

                case SkillModifierKind.Fork:
                    fold.Forks += (int)math.max(1f, modifier.Value);
                    break;

                case SkillModifierKind.Multicast:
                    fold.Casts += (int)math.max(1f, modifier.Value);
                    break;

                case SkillModifierKind.ElementalConversion:
                    fold.Type = modifier.ConvertTo;
                    break;

                case SkillModifierKind.ExplodeOnKill:
                    fold.ExplosionRadius = math.max(fold.ExplosionRadius, modifier.Value);
                    fold.ExplosionShare = math.max(fold.ExplosionShare, modifier.SecondaryValue);
                    break;

                case SkillModifierKind.TriggerOnHit:
                    // Two trigger supports in one group: the last one folded
                    // wins. Firing both would double every effect downstream of
                    // it, which is not what anyone means by socketing two.
                    fold.TriggerId = modifier.TriggeredSkillId;
                    fold.TriggerShare = modifier.Value;
                    break;

                case SkillModifierKind.TriggerOnCondition:
                    // Nothing to fold. It changes who casts, not what the cast
                    // is — and by the time anything gets here, that decision has
                    // already been made by TriggerEvaluationSystem. Listed
                    // explicitly rather than left to the default so that the one
                    // support with no numbers is visibly deliberate.
                    break;
            }
        }

        /// <summary>
        /// Folds supports and the caster's stats into the numbers one cast uses.

        /// <para>
        /// The supports arrive as an argument rather than being read off the
        /// skill, and that is the whole gem model: the same active gem folds a
        /// different list depending on what is socketed beside it. Nothing below
        /// this line knows the numbers came from sockets.
        /// </para>
        ///
        /// Increases stack additively before being applied once, the same rule
        /// the equipment stats follow — two supports each adding fifty percent
        /// give double, not two and a quarter times.
        ///
        /// <para>
        /// The conditions arrive as an argument for the same reason the supports
        /// do: whether a support counts is a fact about this cast, not about the
        /// gem. A caller with nothing to say passes CastConditions.Unknown and
        /// every conditional support simply sits out.
        /// </para>
        /// </summary>
        public ResolvedSkill Resolve(
            int index,
            in StatBlock stats,
            in FixedList512Bytes<SkillModifierBlob> gemSupports,
            in CastConditions conditions)
        {
            ref SkillBlob skill = ref Value.Value.Skills[index];

            // The element a condition asks about is the authored one, so that
            // the answer cannot depend on where in this same fold a conversion
            // gem happens to sit.
            CastConditions asked = conditions;
            asked.SkillElement = skill.Type;

            var fold = new Fold
            {
                Chains = skill.BaseChains,
                Casts = 1,
                Type = skill.Type,
                TriggerShare = 100f
            };

            // Innate first, then whatever the player linked. The order only
            // matters for the two modifiers that overwrite rather than add — a
            // gem conversion beats an authored one, which is the way round
            // anybody would expect.
            for (int m = 0; m < skill.Modifiers.Length; m++)
            {
                // A plain struct with no blob array inside, so copying is safe
                // here in a way that copying the skill never is.
                Accumulate(skill.Modifiers[m], ref fold, asked);
            }

            for (int g = 0; g < gemSupports.Length; g++)
                Accumulate(gemSupports[g], ref fold, asked);

            float increasedDamage = fold.IncreasedDamage;
            float increasedArea = fold.IncreasedArea;
            float increasedSpeed = fold.IncreasedSpeed;

            int chains = fold.Chains;
            int forks = fold.Forks;
            int casts = fold.Casts;

            DamageType type = fold.Type;
            float explosionRadius = fold.ExplosionRadius;
            float explosionShare = fold.ExplosionShare;

            // Turned into an index exactly once, here, so nothing downstream
            // ever holds an id it would have to resolve again.
            int triggerIndex = IndexOf(fold.TriggerId);
            float triggerShare = fold.TriggerShare;

            // The character's damage stat adds flat to every skill, so a weapon
            // makes every skill hit harder without any skill knowing weapons
            // exist. Supports then scale the sum.
            float damage = (skill.BaseDamage + stats.Get(StatKind.Damage))
                           * (1f + increasedDamage * 0.01f);

            // Attack speed shortens cooldowns, which is the other half of making
            // equipment felt rather than merely displayed.
            //
            // Zero means the sheet has not been computed yet — a character in
            // its first frame, or a scene with no base stats baked. Treating
            // that as "no attack speed" would make every skill ten times slower
            // and look like a bug in the cooldowns.
            float attackSpeed = stats.Get(StatKind.AttackSpeed);
            if (attackSpeed <= 0.01f)
                attackSpeed = 1f;

            return new ResolvedSkill
            {
                Effect = skill.Effect,
                Type = type,
                Damage = damage,
                Cooldown = skill.Cooldown / attackSpeed,
                Range = skill.Range,
                Radius = skill.Radius * (1f + increasedArea * 0.01f),
                ArcCosine = math.cos(math.radians(math.clamp(skill.ArcDegrees, 0f, 360f) * 0.5f)),
                ProjectileSpeed = skill.ProjectileSpeed * (1f + increasedSpeed * 0.01f),

                // Duration is not scaled by anything yet. Increased area already
                // makes a zone cover more ground through Radius above, and a
                // support for how long it lasts is a new modifier kind rather
                // than a number quietly borrowed from an existing one.
                ZoneDuration = skill.ZoneDuration,
                ZoneTickInterval = skill.ZoneTickInterval,
                Casts = math.max(1, casts),
                Forks = math.max(0, forks),
                Chains = math.max(0, chains),
                ChainRange = skill.ChainRange,
                ChainDelay = skill.ChainDelay,
                ExplosionRadius = explosionRadius,
                ExplosionDamage = damage * explosionShare * 0.01f,
                TriggerSkillIndex = triggerIndex,
                TriggerDamageScale = math.max(0f, triggerShare) * 0.01f
            };
        }
    }
}
