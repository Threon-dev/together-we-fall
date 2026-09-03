using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Skills
{
    /// <summary>One support socketed into a skill.</summary>
    public struct SkillModifierBlob
    {
        public SkillModifierKind Kind;
        public float Value;

        /// <summary>
        /// Skill this support triggers, or -1. An index rather than a reference
        /// because a blob cannot hold one, and because the index is what the
        /// cast system needs anyway.
        /// </summary>
        public int TriggeredSkillIndex;

        /// <summary>Second number, where one is not enough. See SkillModifier.</summary>
        public float SecondaryValue;

        public DamageType ConvertTo;
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

        public int BaseChains;
        public float ChainRange;
        public float ChainDelay;

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

        public FixedString64Bytes NameOf(int index)
            => IsValidIndex(index) ? Value.Value.Skills[index].Name : default;

        /// <summary>
        /// Folds supports and the caster's stats into the numbers one cast uses.
        ///
        /// Increases stack additively before being applied once, the same rule
        /// the equipment stats follow — two supports each adding fifty percent
        /// give double, not two and a quarter times.
        /// </summary>
        public ResolvedSkill Resolve(int index, in StatBlock stats)
        {
            ref SkillBlob skill = ref Value.Value.Skills[index];

            float increasedDamage = 0f;
            float increasedArea = 0f;
            float increasedSpeed = 0f;

            int chains = skill.BaseChains;
            int forks = 0;
            int casts = 1;

            DamageType type = skill.Type;
            float explosionRadius = 0f;
            float explosionShare = 0f;

            int triggerIndex = -1;
            float triggerShare = 100f;

            for (int m = 0; m < skill.Modifiers.Length; m++)
            {
                // A plain struct with no blob array inside, so copying is safe
                // here in a way that copying the skill never is.
                SkillModifierBlob modifier = skill.Modifiers[m];

                switch (modifier.Kind)
                {
                    case SkillModifierKind.IncreasedDamage:
                        increasedDamage += modifier.Value;
                        break;

                    case SkillModifierKind.IncreasedArea:
                        increasedArea += modifier.Value;
                        break;

                    case SkillModifierKind.IncreasedProjectileSpeed:
                        increasedSpeed += modifier.Value;
                        break;

                    case SkillModifierKind.AddedChains:
                        chains += (int)modifier.Value;
                        break;

                    case SkillModifierKind.Fork:
                        forks += (int)math.max(1f, modifier.Value);
                        break;

                    case SkillModifierKind.Multicast:
                        casts += (int)math.max(1f, modifier.Value);
                        break;

                    case SkillModifierKind.ElementalConversion:
                        type = modifier.ConvertTo;
                        break;

                    case SkillModifierKind.ExplodeOnKill:
                        explosionRadius = math.max(explosionRadius, modifier.Value);
                        explosionShare = math.max(explosionShare, modifier.SecondaryValue);
                        break;

                    case SkillModifierKind.TriggerOnHit:
                        // Two trigger supports in one skill: the last socketed
                        // wins. Firing both would double every effect downstream
                        // of it, which is not what anyone means by socketing two.
                        triggerIndex = modifier.TriggeredSkillIndex;
                        triggerShare = modifier.Value;
                        break;
                }
            }

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
