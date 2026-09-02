using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Wave spawn parameters. The main levers for performance measurements:
    /// how many enemies per wave and how many may be alive at once.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SpawnConfig",
        menuName = "Together We Fall/Spawn Config")]
    public sealed class SpawnConfig : ScriptableObject
    {
        [Header("Wave")]
        [SerializeField, Range(1, 1000)] private int _waveSize = 100;

        [Tooltip("Ceiling on living enemies. Guards against an accidentally " +
                 "held spawn key taking the editor down.")]
        [SerializeField, Range(1, 5000)] private int _maxAlive = 1000;

        [Header("Spawn point scatter")]
        [Tooltip("Radius the enemies are scattered over around a spawn point. " +
                 "Zero would mean the whole wave is born at one coordinate.")]
        [SerializeField] private float _spawnJitterRadius = 3f;

        public int WaveSize => _waveSize;
        public int MaxAlive => _maxAlive;
        public float SpawnJitterRadius => _spawnJitterRadius;
    }
}
