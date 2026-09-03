using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Enemy parameters. A ScriptableObject rather than constants in code so
    /// balance and performance can be tuned with sliders, without a rebuild.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyConfig",
        menuName = "Together We Fall/Enemy Config")]
    public sealed class EnemyConfig : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 3.5f;
        [SerializeField] private float _rotationSpeed = 540f;

        [Tooltip("Distance from the player at which the enemy stops. Without it " +
                 "the crowd shoves the player around and jitters against them.")]
        [SerializeField] private float _stoppingDistance = 1.5f;

        [Header("Survivability")]
        [Tooltip("Low on purpose. A prototype wants to see a hundred enemies die " +
                 "to a chain, not to measure how long one takes.")]
        [SerializeField, Min(1f)] private float _maxHealth = 40f;

        [Tooltip("How long a body takes to shrink away after dying. Long enough " +
                 "to see the crowd come apart, short enough that corpses are not " +
                 "still on screen when the next wave arrives.")]
        [SerializeField, Range(0.05f, 2f)] private float _deathFadeSeconds = 0.35f;

        public float MoveSpeed => _moveSpeed;
        public float RotationSpeed => _rotationSpeed;
        public float StoppingDistance => _stoppingDistance;
        public float MaxHealth => _maxHealth;
        public float DeathFadeSeconds => _deathFadeSeconds;
    }
}
