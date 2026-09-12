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
                bool anyCrit = false;
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
                    anyCrit |= events[i].Crit;

                    // The blow that takes it below zero is the one that decides
                    // what happens to the corpse, so it is recorded rather than
                    // simply the last one in the buffer.
                    //
                    // Or the one that takes it under the culling threshold it
                    // was carrying, which is the same decision: a body left
                    // with a sliver of life by a supported blow is a body that
                    // support finished. Resolved here because this is the only
                    // stage that can see the fraction — everything upstream
                    // knows how hard it hit, not how much was left.
                    if (!killed && (remaining <= 0f || Culled(events[i], remaining, health.Max)))
                    {
                        killed = true;
                        killingBlow = events[i];

                        // Taken to zero rather than left at the sliver, so the
                        // orb, the number and the corpse all agree about what
                        // happened.
                        remaining = 0f;
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
                feedback.Crit = anyCrit;
                hasFeedback.ValueRW = true;

                health.Current = remaining;

                if (!killed)
                    return;

                dead = new Dead
                {
                    KilledByPlayerId = killingBlow.SourcePlayerId,
                    Type = killingBlow.Type,
                    ExplosionRadius = killingBlow.ExplosionRadius,
                    ExplosionDamage = killingBlow.ExplosionDamage,
                    ManaOnKill = killingBlow.ManaOnKill
                };

                isDead.ValueRW = true;
            }

            /// <summary>
            /// Whether this blow's culling support finishes what is left.
            ///
            /// Guarded against a zero maximum for the same reason the cast
            /// conditions are: a target whose maximum is zero would make every
            /// culling support an instant kill rather than a meaningless
            /// question.
            /// </summary>
            private static bool Culled(in DamageEvent blow, float remaining, float max)
                => blow.CullThreshold > 0f && max > 0f &&
                   remaining / max <= blow.CullThreshold;
        }
    }
}
