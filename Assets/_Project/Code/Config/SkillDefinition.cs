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

        [Header("Chaining")]
        [Tooltip("Jumps the skill makes on its own, before any support.")]
        [SerializeField, Range(0, 20)] private int _baseChains;

        [SerializeField, Min(1f)] private float _chainRange = 8f;

        [Tooltip("Pause between jumps. Without it every jump lands on the same " +
                 "frame and the chain reads as one flash.")]
        [SerializeField, Range(0f, 0.5f)] private float _chainDelay = 0.07f;

        [Header("Supports")]
        [SerializeField] private SkillModifier[] _modifiers = Array.Empty<SkillModifier>();

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

        public SkillEffectKind Effect => _effect;
        public DamageType DamageType => _damageType;

        public float BaseDamage => _baseDamage;
        public float Cooldown => _cooldown;
        public float Range => _range;
        public float Radius => _radius;
        public float ArcDegrees => _arcDegrees;
        public float ProjectileSpeed => _projectileSpeed;

        public int BaseChains => _baseChains;
        public float ChainRange => _chainRange;
        public float ChainDelay => _chainDelay;

        public SkillModifier[] Modifiers => _modifiers;
    }
}
