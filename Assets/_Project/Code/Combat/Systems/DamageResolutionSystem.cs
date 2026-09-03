using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// The only system that changes anyone's health.
    ///
    /// Everything upstream writes intents into a buffer on the target; this
    /// turns the pile into a result. The indirection is what makes the rest of
    /// the combat code composable: a projectile, an explosion and a chain jump
    /// all say the same thing in the same place, and none of them has to know
    /// whether the target has already been killed by something else this frame.
    ///
    /// Parallel over entities, safely, because every entity only ever touches
    /// its own buffer and its own health. That is the payoff for putting the
    /// events on the target rather than in one global stream.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Skills.Systems.SkillHitSystem))]
    public partial struct DamageResolutionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ResolveDamageJob().ScheduleParallel();
        }

        /// <summary>
        /// WithPresent covers both flags, and both need it: Dead is disabled on
        /// everything still alive, and DamageFeedback is disabled on everything
        /// that was not hurt last frame. Those are precisely the two sets this
        /// job exists to raise.
        /// </summary>
        [BurstCompile]
        [WithPresent(typeof(Dead), typeof(DamageFeedback))]
        private partial struct ResolveDamageJob : IJobEntity
        {
            private void Execute(
                ref Health health,
                ref Dead dead,
                EnabledRefRW<Dead> isDead,
                ref DamageFeedback feedback,
                EnabledRefRW<DamageFeedback> hasFeedback,
                DynamicBuffer<DamageEvent> events)
            {
                if (events.Length == 0)
                    return;

                // Cleared whatever happens. An event that stayed in the buffer
                // would be applied again next frame, and a corpse would keep
                // taking the same hit forever.
                bool alreadyDead = isDead.ValueRO;
                float remaining = health.Current;

                bool killed = false;
                DamageEvent killingBlow = default;

                float total = 0f;
                DamageType lastType = DamageType.Physical;

                for (int i = 0; i < events.Length; i++)
                {
                    total += events[i].Amount;
                    remaining -= events[i].Amount;

                    // Captured in the loop, because the buffer is cleared before
                    // the feedback is written and there would be nothing left to
                    // read it from.
                    lastType = events[i].Type;

                    // The blow that takes it below zero is the one that decides
                    // what happens to the corpse, so it is recorded rather than
                    // simply the last one in the buffer.
                    if (!killed && remaining <= 0f)
                    {
                        killed = true;
                        killingBlow = events[i];
                    }
                }

                events.Clear();

                if (alreadyDead)
                    return;

                // The whole frame as one figure. The element comes from the last
                // blow rather than being mixed: a number has one colour, and the
                // last thing to land is the one the player just watched arrive.
                feedback.Amount = total;
                feedback.Type = lastType;
                feedback.Killing = killed;
                hasFeedback.ValueRW = true;

                health.Current = remaining;

                if (!killed)
                    return;

                dead = new Dead
                {
                    KilledByPlayerId = killingBlow.SourcePlayerId,
                    Type = killingBlow.Type,
                    ExplosionRadius = killingBlow.ExplosionRadius,
                    ExplosionDamage = killingBlow.ExplosionDamage
                };

                isDead.ValueRW = true;
            }
        }
    }
}
