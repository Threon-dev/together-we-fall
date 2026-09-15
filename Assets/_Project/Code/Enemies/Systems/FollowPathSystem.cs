using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Steers each enemy along its computed path, filling
    /// MovementData.DesiredVelocity. Replaces the stage-2 DirectSteeringSystem
    /// and writes the same field, so nothing downstream changed.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    public partial struct FollowPathSystem : ISystem
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

            new FollowPathJob
            {
                CornerReachRadiusSq = settings.CornerReachRadius * settings.CornerReachRadius
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct FollowPathJob : IJobEntity
        {
            public float CornerReachRadiusSq;

            private void Execute(
                in LocalTransform transform,
                in ChaseTarget target,
                in DynamicBuffer<PathPoint> path,
                ref PathProgress progress,
                ref MovementData movement)
            {
                if (!target.HasTarget)
                {
                    movement.DesiredVelocity = float3.zero;
                    return;
                }

                float3 toTarget = target.Position - transform.Position;
                toTarget.y = 0f;

                if (math.length(toTarget) <= movement.StoppingDistance)
                {
                    movement.DesiredVelocity = float3.zero;
                    return;
                }

                float3 steerPoint = ResolveSteerPoint(transform.Position, target, path, ref progress);

                float3 direction = steerPoint - transform.Position;
                direction.y = 0f;

                float length = math.length(direction);
                movement.DesiredVelocity = length > 0.0001f
                    ? direction / length * movement.Speed
                    : float3.zero;
            }

            private float3 ResolveSteerPoint(
                float3 position,
                in ChaseTarget target,
                in DynamicBuffer<PathPoint> path,
                ref PathProgress progress)
            {
                // No path yet — freshly spawned, or the last query failed. Head
                // straight at the target rather than standing still: waiting in
                // the repath queue can take several frames under a tight budget,
                // and frozen enemies look far more broken than ones that clip a
                // corner for a moment.
                if (path.Length == 0)
                    return target.Position;

                // Consume every corner already reached this frame. A loop rather
                // than a single step because a fast enemy can pass more than one
                // corner between updates, and stepping one at a time would make
                // it visibly backtrack.
                while (progress.CurrentCorner < path.Length)
                {
                    float3 corner = path[progress.CurrentCorner].Position;
                    float3 toCorner = corner - position;
                    toCorner.y = 0f;

                    if (math.lengthsq(toCorner) > CornerReachRadiusSq)
                        return corner;

                    progress.CurrentCorner++;
                }

                // Path exhausted: the remaining distance is a straight run to the
                // target, and a fresh path is already on its way.
                return target.Position;
            }
        }
    }
}
