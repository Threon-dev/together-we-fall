using Unity.Burst;
using Unity.Collections;
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
        private ComponentLookup<StatusGate> _gates;

        public void OnCreate(ref SystemState state)
        {
            _gates = state.GetComponentLookup<StatusGate>(isReadOnly: true);

            state.RequireForUpdate<Health>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _gates.Update(ref state);

            new ResolveDamageJob
            {
                // A lookup rather than a component on the query, deliberately.
                // Requiring StatusGate here would silently exclude anything with
                // health that has not been given one — and "this entity takes no
                // damage at all" is the worst way for a missing component to
                // announce itself.
                Gates = _gates
            }.ScheduleParallel();
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
            [ReadOnly] public ComponentLookup<StatusGate> Gates;

            private void Execute(
                Entity entity,
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

                // Vulnerability and fortification, applied here and nowhere
                // else. This is the one place health changes, so it is the only
                // place "takes more damage" can mean one thing — a multiplier
                // applied where the blow is produced would be applied once per
                // projectile, once per blast and not at all by a burn.
                float taken = Gates.HasComponent(entity)
                    ? Gates[entity].DamageTakenMultiplier
                    : 1f;

                bool killed = false;
                DamageEvent killingBlow = default;

                float total = 0f;
                DamageType lastType = DamageType.Physical;

                for (int i = 0; i < events.Length; i++)
                {
                    float amount = events[i].Amount * taken;

                    total += amount;
                    remaining -= amount;

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
