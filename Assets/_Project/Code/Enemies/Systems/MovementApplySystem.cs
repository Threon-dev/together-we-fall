using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// The only system that writes to an enemy's LocalTransform.
    ///
    /// Everything upstream — path following, crowd separation, and the navmesh
    /// projection that trims the result down to what the surface actually
    /// allows — only ever writes DesiredVelocity. Because of that, adding a new
    /// influence on movement never creates two systems fighting over one
    /// transform, and any single contribution can be switched off in isolation
    /// while profiling.
    ///
    /// Presentation state is updated here too — only the fields that will one
    /// day travel to the client, and no AI decisions.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SeparationSystem))]
    public partial struct MovementApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ApplyMovementJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct ApplyMovementJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref LocalTransform transform,
                ref EnemyPresentation presentation,
                in MovementData movement)
            {
                float speed = math.length(movement.DesiredVelocity);

                if (speed < 0.0001f)
                {
                    presentation.MoveSpeedNormalized = 0f;
                    return;
                }

                float3 direction = movement.DesiredVelocity / speed;

                transform.Position += movement.DesiredVelocity * DeltaTime;

                // Turn gradually rather than instantly: a snap 180° turn reads
                // as twitching in a crowd, and it is exactly what makes enemies
                // look like they teleport even when their positions are correct.
                quaternion desiredRotation = quaternion.LookRotationSafe(direction, math.up());
                float maxRadians = math.radians(movement.RotationSpeed) * DeltaTime;
                transform.Rotation = RotateTowards(transform.Rotation, desiredRotation, maxRadians);

                presentation.FacingDirection = direction;
                presentation.MoveSpeedNormalized = math.saturate(speed / math.max(movement.Speed, 0.0001f));
            }

            /// <summary>
            /// Unity.Mathematics has no equivalent of Quaternion.RotateTowards,
            /// and a plain slerp with a fixed t would make turn rate depend on
            /// the angle — distant targets would snap around while nearby ones
            /// crawled.
            /// </summary>
            private static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
            {
                float dot = math.clamp(math.dot(from, to), -1f, 1f);
                float angle = math.acos(math.abs(dot)) * 2f;

                if (angle < 1e-5f)
                    return to;

                float t = math.min(1f, maxRadians / angle);
                return math.slerp(from, to, t);
            }
        }
    }
}
