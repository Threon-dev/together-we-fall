using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TogetherWeFall.DebugTools.Systems
{
    /// <summary>
    /// Sweeps patrolling dummies back and forth.
    ///
    /// Position from a sine of elapsed time rather than a step per frame: a
    /// stepped walker accumulates its own rounding and ends up somewhere it was
    /// not sent, and reversing at the ends needs a direction to remember. From
    /// the clock there is nothing to remember and nothing to drift.
    ///
    /// Scaled time on purpose, unlike everything to do with feel. A hit-stop is
    /// supposed to freeze the fight, and a target that kept gliding through one
    /// would be the one thing on screen that ignored it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DummyPatrolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DummyPatrol>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new PatrolJob
            {
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct PatrolJob : IJobEntity
        {
            public float ElapsedTime;

            private void Execute(ref LocalTransform transform, in DummyPatrol patrol)
            {
                float offset = math.sin(ElapsedTime * patrol.Frequency * 2f * math.PI) *
                               patrol.Distance;

                transform.Position = patrol.Origin + patrol.Axis * offset;
            }
        }
    }
}
