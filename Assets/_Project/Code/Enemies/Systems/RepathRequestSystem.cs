using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Decides which enemies need a fresh path, without computing any.
    ///
    /// Split from PathfindingSystem on purpose. Deciding is cheap, parallel and
    /// Burst-friendly; computing is a managed main-thread call. Keeping them
    /// apart means the expensive half only ever sees a pre-filtered list, and
    /// the cheap half scales freely with enemy count.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChaseTargetSystem))]
    public partial struct RepathRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PathfindingSettings>();
            state.RequireForUpdate<EnemyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            PathfindingSettings settings = SystemAPI.GetSingleton<PathfindingSettings>();

            new RequestRepathJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                RepathInterval = settings.RepathInterval,
                MinRepathInterval = settings.MinRepathInterval,
                DistanceThresholdSq = settings.RepathDistanceThreshold * settings.RepathDistanceThreshold
            }.ScheduleParallel();
        }

        /// <summary>
        /// WithPresent is load-bearing, not decoration. EnabledRefRW alone puts
        /// NeedsRepath into the query with its enabled-state filter applied, so
        /// the job would only ever see enemies whose flag is ALREADY raised —
        /// and this job is the only thing that raises it. The result is a system
        /// that works exactly once per enemy: the first path is computed, the
        /// flag is cleared by PathfindingSystem, and nothing can ever request
        /// another one. Enemies then run out of corners and fall back to
        /// straight-line steering, walking through walls.
        /// </summary>
        [BurstCompile]
        [WithPresent(typeof(NeedsRepath))]
        private partial struct RequestRepathJob : IJobEntity
        {
            public float DeltaTime;
            public float RepathInterval;
            public float MinRepathInterval;
            public float DistanceThresholdSq;

            private void Execute(
                in ChaseTarget target,
                in DynamicBuffer<PathPoint> path,
                ref PathProgress progress,
                EnabledRefRW<NeedsRepath> needsRepath)
            {
                progress.TimeSinceRepath += DeltaTime;

                // Already queued, or nothing to chase — nothing to decide.
                if (needsRepath.ValueRO || !target.HasTarget)
                    return;

                // The floor applies to every reason below, including "no path at
                // all". An enemy stranded off the navmesh fails its query every
                // time; without this it would re-enter the queue every frame and
                // starve everyone else out of the budget.
                if (progress.TimeSinceRepath < MinRepathInterval)
                    return;

                bool stale = progress.TimeSinceRepath >= RepathInterval;
                bool targetMoved =
                    math.distancesq(target.Position, progress.LastTargetPosition) >= DistanceThresholdSq;
                bool hasNoPath = path.Length == 0;

                if (stale || targetMoved || hasNoPath)
                    needsRepath.ValueRW = true;
            }
        }
    }
}
