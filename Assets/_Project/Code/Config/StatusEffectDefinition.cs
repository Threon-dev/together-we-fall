using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// One status an element can leave on a target — Ignite, Shock, Chill.
    ///
    /// An asset rather than a row in the reaction table, because the same status
    /// is named from several places: fire leaves it on its own, and a reaction
    /// may hand it out as its result. Two authored copies of "Ignite" would be
    /// two things to keep in balance and one to forget.
    ///
    /// What a status DOES, beyond marking its target with an element and
    /// optionally burning it, is nothing yet. That is honest rather than
    /// unfinished: enemies do not attack, so the classic "shocked targets are
    /// slower to swing" has nothing to slow down. The interesting half —
    /// "shocked targets take more from fire" — is not a property of the status
    /// at all; it is a reaction rule, and lives next door.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StatusEffectDefinition",
        menuName = "Together We Fall/Status Effect")]
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        [Tooltip("Shown to the player. Falls back to the asset name when empty.")]
        [SerializeField] private string _displayName;

        [Tooltip("The element this marks its target with. A target carries at " +
                 "most one status per element, so two statuses sharing an " +
                 "element replace each other.")]
        [SerializeField] private DamageType _element = DamageType.Fire;

        [SerializeField, Min(0.1f)] private float _duration = 4f;

        [Tooltip("How many times it can pile up. Each stack adds a tick's worth " +
                 "of damage; nothing else scales with it yet.")]
        [SerializeField, Range(1, 20)] private int _maxStacks = 3;

        [Header("Damage over time")]
        [Tooltip("Zero for a status that only marks its target, which is most " +
                 "of them. Shock and Chill mark; Ignite burns.")]
        [SerializeField, Min(0f)] private float _damagePerSecond;

        [Tooltip("Seconds between ticks. Each tick is worth this many seconds " +
                 "of damage, so changing it changes the rhythm and not the total.")]
        [SerializeField, Range(0.1f, 2f)] private float _tickInterval = 0.5f;

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

        public DamageType Element => _element;
        public float Duration => _duration;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
        public float DamagePerSecond => Mathf.Max(0f, _damagePerSecond);
        public float TickInterval => Mathf.Max(0.1f, _tickInterval);
    }
}
