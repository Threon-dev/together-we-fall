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

        public float MoveSpeed => _moveSpeed;
        public float RotationSpeed => _rotationSpeed;
        public float StoppingDistance => _stoppingDistance;
    }
}
