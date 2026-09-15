using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using TogetherWeFall.Combat;
using TogetherWeFall.Combat.Systems;

namespace TogetherWeFall.DebugTools.Systems
{
    /// <summary>
    /// Keeps training dummies standing, and flashes them when something lands.
    ///
    /// It runs between damage resolution and the death reaction, which is the
    /// whole trick: the damage pipeline is left completely untouched — it hurts
    /// a dummy exactly as it hurts anything else and marks it dead when the
    /// health runs out — and this puts the health back and lowers the flag
    /// before anything acts on it. No "if this is a dummy" ever reaches the one
    /// system that resolves damage for everything in the game.
    ///
    /// The damage is not thrown away, either: SkillHitSystem has already counted
    /// it into the run tally, so the debug overlay measures real output against
    /// a target that never falls over.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageResolutionSystem))]
    [UpdateBefore(typeof(DeathReactionSystem))]
    public partial struct TrainingDummySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TrainingDummy>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new RestoreDummiesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel();
        }

        /// <summary>
        /// WithPresent is required, not decoration: Dead is disabled on a dummy
        /// that is merely being shot at, and that is most of them most of the
        /// time. Without it this job would only ever see a dummy on the single
        /// frame it was killed — and would never run the flash at all.
        /// </summary>
        [BurstCompile]
        [WithPresent(typeof(Dead))]
        private partial struct RestoreDummiesJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref Health health,
                ref TrainingDummy dummy,
                ref URPMaterialPropertyBaseColor color,
                EnabledRefRW<Dead> isDead)
            {
                // Health below maximum is how a dummy knows it was hit: the
                // damage buffer has already been drained by the system upstream,
                // so the dent it left is the only evidence remaining.
                if (health.Current < health.Max)
                {
                    dummy.FlashRemaining = dummy.FlashDuration;
                    health.Current = health.Max;
                }

                // Whatever the last blow was worth, it does not get to finish
                // the job.
                if (isDead.ValueRO)
                    isDead.ValueRW = false;

                if (dummy.FlashRemaining <= 0f)
                    return;

                dummy.FlashRemaining = math.max(0f, dummy.FlashRemaining - DeltaTime);

                float flash = dummy.FlashRemaining / math.max(0.01f, dummy.FlashDuration);
                color.Value = math.lerp(dummy.RestColor, new float4(1f, 1f, 1f, 1f), flash);
            }
        }
    }
}
