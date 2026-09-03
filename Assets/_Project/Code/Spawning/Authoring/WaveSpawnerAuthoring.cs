using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Spawning.Authoring
{
    /// <summary>
    /// Wave spawner. One per scene, living in a SubScene together with the
    /// spawn points.
    /// </summary>
    public sealed class WaveSpawnerAuthoring : MonoBehaviour
    {
        [SerializeField] private GameObject _enemyPrefab;
        [SerializeField] private SpawnConfig _config;

        [Tooltip("Seed for the scatter generator. The same seed produces the " +
                 "same wave layout — handy so FPS measurements compare across " +
                 "runs instead of drifting with randomness.")]
        [SerializeField] private uint _randomSeed = 1337;

        public GameObject EnemyPrefab => _enemyPrefab;
        public SpawnConfig Config => _config;
        public uint RandomSeed => _randomSeed;

        private sealed class WaveSpawnerBaker : Baker<WaveSpawnerAuthoring>
        {
            public override void Bake(WaveSpawnerAuthoring authoring)
            {
                if (authoring.EnemyPrefab == null || authoring.Config == null)
                {
                    Debug.LogError(
                        $"[{nameof(WaveSpawnerAuthoring)}] No enemy prefab or SpawnConfig " +
                        "assigned — waves will not spawn.", authoring);
                    return;
                }

                DependsOn(authoring.Config);

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new WaveSpawnerConfig
                {
                    EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                    WaveSize = authoring.Config.WaveSize,
                    MaxAlive = authoring.Config.MaxAlive,
                    JitterRadius = authoring.Config.SpawnJitterRadius
                });

                AddBuffer<WaveSpawnOrder>(entity);

                AddComponent(entity, new WaveSpawnerState
                {
                    Random = Unity.Mathematics.Random.CreateFromIndex(authoring.RandomSeed),
                    WavesSpawned = 0,
                    TotalSpawned = 0
                });
            }
        }
    }
}
