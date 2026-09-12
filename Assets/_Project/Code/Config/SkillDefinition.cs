using System;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// One skill: what it does, how hard it hits, and what is socketed into it.
    ///
    /// The supports live on the skill rather than on the player because that is
    /// the PoE model — a skill is the gem plus its links, and the same support
    /// behaves differently depending on what it is supporting. Moving supports
    /// onto the character would make "chain" mean one thing for everything they
    /// cast, which is a different and much duller game.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SkillDefinition",
        menuName = "Together We Fall/Skill Definition")]
    public sealed class SkillDefinition : ScriptableObject
    {
        [Tooltip("Shown to the player. Falls back to the asset name when empty.")]
        [SerializeField] private string _displayName;

        [Header("Base effect")]
        [SerializeField] private SkillEffectKind _effect = SkillEffectKind.Projectile;
        [SerializeField] private DamageType _damageType = DamageType.Physical;

        [SerializeField, Min(0f)] private float _baseDamage = 25f;

        [Tooltip("Seconds between casts, before attack speed is applied.")]
        [SerializeField, Min(0.05f)] private float _cooldown = 0.5f;

        [Tooltip("Mana the cast costs. Zero is free, which is what every skill " +
                 "authored before mana existed is — a weapon's built-in attack " +
                 "should stay free, or an empty pool means no attack at all.")]
        [SerializeField, Min(0f)] private float _manaCost;

        [Tooltip("How far the skill reaches. For a projectile, how far it flies.")]
        [SerializeField, Min(1f)] private float _range = 20f;

        [Header("Area")]
        [Tooltip("Blast radius for an area burst, reach for a melee arc, impact " +
                 "radius for a projectile. Zero on a projectile means it hits " +
                 "one target and nothing else.")]
        [SerializeField, Min(0f)] private float _radius;

        [Tooltip("Full width of a melee arc in degrees. 360 is a circle and is " +
                 "what every non-melee effect uses.")]
        [SerializeField, Range(10f, 360f)] private float _arcDegrees = 360f;

        [Header("Projectile")]
        [SerializeField, Min(1f)] private float _projectileSpeed = 26f;

        [Header("Persistent zone")]
        [Tooltip("How long a Persistent Zone stays on the ground. Ignored by " +
                 "every other effect.")]
        [SerializeField, Min(0.5f)] private float _zoneDuration = 5f;

        [Tooltip("Seconds between damage pulses inside a zone. A pulse is worth " +
                 "the skill damage, so this is the rate as well as the rhythm.")]
        [SerializeField, Range(0.1f, 3f)] private float _zoneTickInterval = 0.5f;

        [Header("Chaining")]
        [Tooltip("Jumps the skill makes on its own, before any support.")]
        [SerializeField, Range(0, 20)] private int _baseChains;

        [SerializeField, Min(1f)] private float _chainRange = 8f;

        [Tooltip("Pause between jumps. Without it every jump lands on the same " +
                 "frame and the chain reads as one flash.")]
        [SerializeField, Range(0f, 0.5f)] private float _chainDelay = 0.07f;

        [Header("Status")]
        [Tooltip("A status everything this skill hits is marked with, on top of " +
                 "whatever its element leaves behind. This is the only way a " +
                 "stun, a root or a debuff ever lands — nothing applies one on " +
                 "its own. The asset must also be listed on the reaction table, " +
                 "or the runtime has never heard of it.")]
        [SerializeField] private StatusEffectDefinition _appliedStatus;

        [Header("Look")]
        [Tooltip("What this skill looks like: the flash where it is cast, the " +
                 "thing that flies, the effect where it lands. Empty draws what " +
                 "the game drew before sets existed — a coloured line and a " +
                 "ring, and nothing is broken by leaving it so.")]
        [SerializeField] private SkillVfxSet _vfx;

        [Header("Supports")]
        [SerializeField] private SkillModifier[] _modifiers = Array.Empty<SkillModifier>();

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

        /// <summary>
        /// What an active gem refers to this skill by.
        ///
        /// The same FNV hash items use, for the same reason: the item database
        /// and the skill database are baked by two authoring objects that share
        /// no ordering, so an index would mean whichever happened to be first.
        /// </summary>
        public int SkillId => ItemDefinition.ComputeId(DisplayName);

        public SkillEffectKind Effect => _effect;
        public DamageType DamageType => _damageType;

        public float BaseDamage => _baseDamage;
        public float Cooldown => _cooldown;
        public float ManaCost => _manaCost;
        public float Range => _range;
        public float Radius => _radius;
        public float ArcDegrees => _arcDegrees;
        public float ProjectileSpeed => _projectileSpeed;

        public float ZoneDuration => _zoneDuration;
        public float ZoneTickInterval => _zoneTickInterval;

        public int BaseChains => _baseChains;
        public float ChainRange => _chainRange;
        public float ChainDelay => _chainDelay;

        public SkillModifier[] Modifiers => _modifiers;

        /// <summary>The asset, so the baker can depend on it and read its type.</summary>
        public StatusEffectDefinition AppliedStatus => _appliedStatus;

        /// <summary>The visual set, for the baker to depend on.</summary>
        public SkillVfxSet Vfx => _vfx;

        /// <summary>
        /// The visual set as a stable id, or zero for a skill that has none.
        ///
        /// An id rather than a reference for the reason every cross-database
        /// reference here is one: the presenter's list of sets and the skill
        /// database are filled by two different things, and a job cannot hold a
        /// prefab anyway.
        /// </summary>
        public int VfxId => _vfx != null ? ItemDefinition.ComputeId(_vfx.name) : 0;

        /// <summary>
        /// What the skill applies, as the name the two databases share.
        ///
        /// An asset reference here and a type in the blob: authoring picks a
        /// thing that exists, and the runtime carries a value a job can compare.
        /// </summary>
        public StatusEffectType AppliedStatusType => _appliedStatus != null
            ? _appliedStatus.Type
            : StatusEffectType.None;
    }
}
