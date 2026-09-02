using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;

namespace TogetherWeFall.Spawning.Systems
{
    /// <summary>
    /// Spawns a wave of enemies on demand.
    ///
    /// Deliberately without [BurstCompile] and without jobs: instantiation is a
    /// structural change that syncs the world anyway, and it happens rarely
    /// (once per wave, not every frame). Complicating it here would pay for
    /// nothing.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WaveSpawnSystem : ISystem
    {
        private EntityQuery _spawnPointQuery;
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _spawnPointQuery = SystemAPI.QueryBuilder()
                .WithAll<SpawnPoint, LocalToWorld>()
                .Build();

            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag>()
                .Build();

            state.RequireForUpdate<WaveSpawnerConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity spawner = SystemAPI.GetSingletonEntity<WaveSpawnerConfig>();
            var request = SystemAPI.GetComponentRW<WaveSpawnRequest>(spawner);

            if (request.ValueRO.PendingWaves <= 0)
                return;

            WaveSpawnerConfig config = SystemAPI.GetComponent<WaveSpawnerConfig>(spawner);
            var spawnerState = SystemAPI.GetComponentRW<WaveSpawnerState>(spawner);

            request.ValueRW.PendingWaves--;

            if (_spawnPointQuery.IsEmpty)
            {
                UnityEngine.Debug.LogWarning(
                    "[WaveSpawnSystem] No spawn points in the scene — wave skipped.");
                return;
            }

            int alive = _enemyQuery.CalculateEntityCount();
            int budget = math.max(0, config.MaxAlive - alive);
            int toSpawn = math.min(config.WaveSize, budget);

            if (toSpawn <= 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WaveSpawnSystem] Living enemy cap reached ({config.MaxAlive}) — " +
                    "wave skipped.");
                return;
            }

            SpawnWave(ref state, config, ref spawnerState.ValueRW, toSpawn);
        }

        private void SpawnWave(
            ref SystemState state,
            WaveSpawnerConfig config,
            ref WaveSpawnerState spawnerState,
            int count)
        {
            using NativeArray<LocalToWorld> spawnPoints =
                _spawnPointQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            // Instantiate the whole wave in one call: 100 separate Instantiate
            // calls would mean 100 structural changes and a visible spike in
            // exactly the place we are trying to measure that spike.
            using NativeArray<Entity> spawned =
                state.EntityManager.Instantiate(config.EnemyPrefab, count, Allocator.Temp);

            for (int i = 0; i < spawned.Length; i++)
            {
                int pointIndex = spawnerState.Random.NextInt(0, spawnPoints.Length);
                float3 origin = spawnPoints[pointIndex].Position;

                float2 offset = spawnerState.Random.NextFloat2Direction()
                                * spawnerState.Random.NextFloat(0f, config.JitterRadius);

                float3 position = origin + new float3(offset.x, 0f, offset.y);

                state.EntityManager.SetComponentData(
                    spawned[i],
                    LocalTransform.FromPosition(position));
            }

            spawnerState.WavesSpawned++;
            spawnerState.TotalSpawned += count;
        }
    }
}
