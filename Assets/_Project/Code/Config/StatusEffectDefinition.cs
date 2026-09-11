using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// One status: how long it lasts, how hard it is, and how many times it
    /// piles up.
    ///
    /// What it DOES is not here. That is read off the type — see StatusEffects,
    /// which is the only place in the project that branches on one. The split is
    /// the same one the support gems already make between a kind and its
    /// numbers: a Stun authored with no movement block, or an asset claiming to
    /// be Haste while slowing its target, are states that should not exist, and
    /// the cheapest way to make them impossible is to not offer the fields.
    ///
    /// An asset rather than a row in the reaction table, because the same status
    /// is named from several places: an element leaves it on its own, a reaction
    /// hands it out as a result, and a skill applies it directly. Two authored
    /// copies of "Ignite" would be two things to keep in balance and one to
    /// forget.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StatusEffectDefinition",
        menuName = "Together We Fall/Status Effect")]
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        [Tooltip("Shown to the player. Falls back to the asset name when empty.")]
        [SerializeField] private string _displayName;

        [Tooltip("What this status is. Everything it does follows from this — " +
                 "which number it scales, whether it stops movement or casting, " +
                 "and whether it is hard control and so subject to diminishing " +
                 "returns. Two assets claiming the same type is a bake error.")]
        [SerializeField] private StatusEffectType _type = StatusEffectType.Ignite;

        [Tooltip("The element this marks its target with, for the reaction " +
                 "table. Ignored by control and stat statuses, which carry no " +
                 "element at all.")]
        [SerializeField] private DamageType _element = DamageType.Fire;

        [SerializeField, Min(0.1f)] private float _duration = 4f;

        [Tooltip("How many times it can pile up. One for control, more for a " +
                 "burn or a debuff meant to be built up.")]
        [SerializeField, Range(1, 20)] private int _maxStacks = 3;

        [Tooltip("Applying it again adds a full duration instead of refreshing " +
                 "to one. Off for control: being stunned during a stun must not " +
                 "lengthen it, or a crowd becomes a stun-lock.")]
        [SerializeField] private bool _stacksDuration;

        [Header("Damage over time")]
        [Tooltip("Zero for a status that only marks its target, which is most " +
                 "of them. Shock and Chill mark; Ignite burns.")]
        [SerializeField, Min(0f)] private float _damagePerSecond;

        [Tooltip("Seconds between ticks. Each tick is worth this many seconds " +
                 "of damage, so changing it changes the rhythm and not the total.")]
        [SerializeField, Range(0.1f, 2f)] private float _tickInterval = 0.5f;

        [Header("Magnitude")]
        [Tooltip("What one stack is worth to the number this status scales: the " +
                 "fraction a Slow takes off movement, the fraction Vulnerable " +
                 "adds to damage taken. Always positive — which way it goes is " +
                 "decided by the type. Ignored by control and damage statuses.")]
        [SerializeField, Range(0f, 1f)] private float _magnitudePerStack = 0.3f;

        [Header("Diminishing returns")]
        [Tooltip("Multiple of this status's duration that the target is immune " +
                 "for once it ends. Read only for hard control — Stun, Root, " +
                 "Silence, Fear. Zero disables it, which with trigger gems in " +
                 "the game means a permanent lock is authorable.")]
        [SerializeField, Range(0f, 5f)] private float _immunityMultiplier = 1.5f;

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

        public StatusEffectType Type => _type;

        /// <summary>
        /// Derived, never authored. A second field saying "this is crowd
        /// control" would be a second answer to a question the type already
        /// answers, and the two would disagree the day somebody duplicates an
        /// asset to make a variant.
        /// </summary>
        public StatusCategory Category => StatusEffects.CategoryOf(_type);

        /// <summary>
        /// The element, or Physical for a status that carries none.
        ///
        /// Zeroed in the property rather than checked by every consumer, the
        /// same way ItemDefinition zeroes two-handedness outside the main hand:
        /// the blob then only ever sees the corrected value.
        /// </summary>
        public DamageType Element
            => StatusEffects.CarriesElement(_type) ? _element : DamageType.Physical;

        public bool CarriesElement => StatusEffects.CarriesElement(_type);

        public float Duration => _duration;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
        public bool StacksDuration => _stacksDuration;
        public float DamagePerSecond => Mathf.Max(0f, _damagePerSecond);
        public float TickInterval => Mathf.Max(0.1f, _tickInterval);
        public float MagnitudePerStack => Mathf.Max(0f, _magnitudePerStack);
        public float ImmunityMultiplier => Mathf.Max(0f, _immunityMultiplier);
    }
}
