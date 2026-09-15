using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Pushes nearby enemies apart so a wave flows around the player instead of
    /// stacking into a single point.
    ///
    /// Built on a spatial hash rather than an all-pairs sweep for the obvious
    /// reason: comparing every enemy against every other is O(n squared), and at
    /// 500 enemies that is a quarter of a million distance checks per frame. The
    /// grid turns it into "check the nine cells around me", which stays roughly
    /// linear as long as cell size tracks the separation radius.
    ///
    /// Like every other steering system, this one only adds to
    /// MovementData.DesiredVelocity — the transform is still written by exactly
    /// one system downstream.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FollowPathSystem))]
    public partial struct SeparationSystem : ISystem
    {
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<SeparationSettings>();
            state.RequireForUpdate(_enemyQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SeparationSettings settings = SystemAPI.GetSingleton<SeparationSettings>();
            int enemyCount = _enemyQuery.CalculateEntityCount();

            // A ParallelWriter cannot grow its map, so capacity has to cover
            // every enemy up front. Each enemy contributes exactly one entry,
            // which makes the count an exact figure rather than a guess.
            var grid = new NativeParallelMultiHashMap<int, NeighborEntry>(
                enemyCount, Allocator.TempJob);

            state.Dependency = new BuildGridJob
            {
                CellSize = settings.CellSize,
                Writer = grid.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new SeparationJob
            {
                Grid = grid,
                CellSize = settings.CellSize,
                Radius = settings.Radius,
                RadiusSq = settings.Radius * settings.Radius,
                Strength = settings.Strength,
                MaxNeighbors = settings.MaxNeighbors
            }.ScheduleParallel(state.Dependency);

            grid.Dispose(state.Dependency);
        }

        /// <summary>One enemy's entry in the grid.</summary>
        private struct NeighborEntry
        {
            public float3 Position;
            public int EntityIndex;
        }

        /// <summary>
        /// Grid maths shared by both jobs. Kept in one place because the two
        /// must agree exactly: if the build and the lookup disagreed on how a
        /// position maps to a cell, neighbours would simply never be found and
        /// the system would fail silently rather than loudly.
        /// </summary>
        private static class SpatialHash
        {
            public static int2 ToCell(float3 position, float cellSize)
                => (int2)math.floor(position.xz / cellSize);

            public static int Key(int2 cell) => (int)math.hash(cell);
        }

        /// <summary>
        /// WithAll(EnemyTag) is required, not cosmetic: this job's only other
        /// parameter is LocalTransform, which spawn points and any other baked
        /// object also carry. Without the filter they would be hashed in as
        /// phantom neighbours and enemies would shove away from empty air.
        /// </summary>
        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct BuildGridJob : IJobEntity
        {
            public float CellSize;
            public NativeParallelMultiHashMap<int, NeighborEntry>.ParallelWriter Writer;

            private void Execute(Entity entity, in LocalTransform transform)
            {
                int2 cell = SpatialHash.ToCell(transform.Position, CellSize);

                Writer.Add(SpatialHash.Key(cell), new NeighborEntry
                {
                    Position = transform.Position,
                    EntityIndex = entity.Index
                });
            }
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct SeparationJob : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<int, NeighborEntry> Grid;

            public float CellSize;
            public float Radius;
            public float RadiusSq;
            public float Strength;
            public int MaxNeighbors;

            private void Execute(Entity entity, in LocalTransform transform, ref MovementData movement)
            {
                float3 position = transform.Position;
                int2 centerCell = SpatialHash.ToCell(position, CellSize);

                float3 push = float3.zero;
                int considered = 0;

                // Nine cells cover the whole radius because cell size is clamped
                // to at least the radius at bake time.
                for (int dz = -1; dz <= 1 && considered < MaxNeighbors; dz++)
                {
                    for (int dx = -1; dx <= 1 && considered < MaxNeighbors; dx++)
                    {
                        int key = SpatialHash.Key(centerCell + new int2(dx, dz));

                        if (!Grid.TryGetFirstValue(key, out NeighborEntry neighbor, out var iterator))
                            continue;

                        do
                        {
                            if (neighbor.EntityIndex == entity.Index)
                                continue;

                            float3 delta = position - neighbor.Position;
                            delta.y = 0f;

                            float distanceSq = math.lengthsq(delta);
                            if (distanceSq > RadiusSq)
                                continue;

                            push += ResolvePush(delta, distanceSq, entity.Index, neighbor.EntityIndex);
                            considered++;
                        }
                        while (considered < MaxNeighbors && Grid.TryGetNextValue(out neighbor, ref iterator));
                    }
                }

                if (considered == 0)
                    return;

                float3 combined = movement.DesiredVelocity + push * Strength * movement.Speed;

                // Clamp to the enemy's own speed. Without it a dense pile-up
                // launches units outward faster than they could ever walk, which
                // reads as an explosion rather than a crowd.
                float speed = math.length(combined);
                if (speed > movement.Speed)
                    combined = combined / speed * movement.Speed;

                movement.DesiredVelocity = combined;
            }

            private float3 ResolvePush(float3 delta, float distanceSq, int selfIndex, int otherIndex)
            {
                if (distanceSq >= 1e-6f)
                {
                    float distance = math.sqrt(distanceSq);

                    // Linear falloff: full strength when touching, nothing at the
                    // radius. Stops distant neighbours from dominating the sum.
                    return delta / distance * (1f - distance / Radius);
                }

                // Two enemies at the exact same spot: the difference vector holds
                // no direction at all. Derive one from the index pair so the tie
                // breaks deterministically and the two are pushed opposite ways,
                // rather than staying welded together forever.
                uint hash = math.hash(new int2(
                    math.min(selfIndex, otherIndex),
                    math.max(selfIndex, otherIndex)));

                float angle = hash % 628u / 100f;
                float3 axis = new float3(math.cos(angle), 0f, math.sin(angle));

                return selfIndex < otherIndex ? axis : -axis;
            }
        }
    }
}
