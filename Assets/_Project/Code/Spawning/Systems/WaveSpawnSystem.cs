using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Combat;
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
    ///
    /// The system takes orders and never gives them. Who decides that a wave is
    /// due — a debug key, a room the players walked into, or later a host — is
    /// none of its business; it only knows how many enemies to put down and
    /// which points it may put them on.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WaveSpawnSystem : ISystem
    {
        private EntityQuery _spawnPointQuery;
        private EntityQuery _freeEnemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _spawnPointQuery = SystemAPI.QueryBuilder()
                .WithAll<SpawnPoint, LocalToWorld>()
                .Build();

            // The pool, seen from the other side. A body is free when it is no
            // longer an enemy and no longer fading: the first excludes the
            // living, the second excludes corpses still playing out.
            _freeEnemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemySpawnDefaults>()
                .WithDisabled<EnemyTag>()
                .WithDisabled<DeathFade>()
                .Build();

            // Both parts are required: a spawner baked before the order buffer
            // existed would otherwise throw on the first frame rather than
            // simply staying idle until the scene is rebuilt.
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<WaveSpawnerConfig, WaveSpawnOrder>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity spawner = SystemAPI.GetSingletonEntity<WaveSpawnerConfig>();

            DynamicBuffer<WaveSpawnOrder> orders = SystemAPI.GetBuffer<WaveSpawnOrder>(spawner);
            if (orders.Length == 0)
                return;

            // One order per frame. Instantiating a wave is the most expensive
            // thing this system does, and draining the whole queue at once would
            // pile those spikes into a single frame to save a few milliseconds
            // of latency nobody would notice.
            WaveSpawnOrder order = orders[0];
            orders.RemoveAt(0);

            WaveSpawnerConfig config = SystemAPI.GetComponent<WaveSpawnerConfig>(spawner);

            using NativeList<float3> origins = CollectOrigins(order.RoomId);
            if (origins.Length == 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WaveSpawnSystem] No spawn points for room {order.RoomId} — wave skipped.");
                return;
            }

            int requested = order.Count > 0 ? order.Count : config.WaveSize;

            WaveSpawnerState spawnerState = SystemAPI.GetComponent<WaveSpawnerState>(spawner);
            int spawned = SpawnWave(ref state, config, ref spawnerState, origins, requested);
            state.EntityManager.SetComponentData(spawner, spawnerState);

            if (spawned < requested)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WaveSpawnSystem] Only {spawned} of {requested} enemies spawned — the " +
                    $"pool of {config.MaxAlive} is fully in use.");
            }
        }

        /// <summary>
        /// Gathers the positions an order may use. Both arrays come from the
        /// same query, so index i describes the same entity in each.
        /// </summary>
        private NativeList<float3> CollectOrigins(int roomId)
        {
            using NativeArray<LocalToWorld> transforms =
                _spawnPointQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            using NativeArray<SpawnPoint> points =
                _spawnPointQuery.ToComponentDataArray<SpawnPoint>(Allocator.Temp);

            var origins = new NativeList<float3>(math.max(1, transforms.Length), Allocator.Temp);

            for (int i = 0; i < points.Length; i++)
            {
                if (roomId != SpawnPoint.AnyRoom && points[i].RoomId != roomId)
                    continue;

                origins.Add(transforms[i].Position);
            }

            return origins;
        }

        /// <summary>
        /// Wakes as much of the wave as the pool can supply, and returns how
        /// many that was.
        ///
        /// Nothing is created here any more. The pool holds MaxAlive bodies and
        /// a wave takes from it, so the cap enforces itself: asking for more
        /// than is free simply gets less, which is what a cap always meant.
        /// </summary>
        private int SpawnWave(
            ref SystemState state,
            WaveSpawnerConfig config,
            ref WaveSpawnerState spawnerState,
            in NativeList<float3> origins,
            int count)
        {
            using NativeArray<Entity> free = _freeEnemyQuery.ToEntityArray(Allocator.Temp);

            int toSpawn = math.min(count, free.Length);
            if (toSpawn <= 0)
                return 0;

            // A list rather than an array because a using-declared array is
            // readonly, and writing through its indexer will not compile.
            using var positions = new NativeList<float3>(toSpawn, Allocator.Temp);

            for (int i = 0; i < toSpawn; i++)
            {
                float3 origin = origins[spawnerState.Random.NextInt(0, origins.Length)];

                float2 offset = spawnerState.Random.NextFloat2Direction()
                                * spawnerState.Random.NextFloat(0f, config.JitterRadius);

                positions.Add(origin + new float3(offset.x, 0f, offset.y));
            }

            int woken = EnemyPool.Activate(state.EntityManager, free, positions.AsArray());

            spawnerState.WavesSpawned++;
            spawnerState.TotalSpawned += woken;

            return woken;
        }
    }
}
