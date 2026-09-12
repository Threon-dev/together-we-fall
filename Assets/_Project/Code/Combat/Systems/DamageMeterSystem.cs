using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// Writes down what landed on a metered target, and forgets anything older
    /// than the window.
    ///
    /// It runs between the reaction stage and the resolver, and that position is
    /// the whole design. After the reactions, so the numbers it records are the
    /// ones that will actually be dealt — a reaction bonus is added to the event
    /// there, and a meter reading it any earlier would under-report exactly the
    /// thing a player is trying to see. Before the resolver, because the resolver
    /// drains the buffer: one stage later there is nothing left to read.
    ///
    /// Nothing writes to it and nothing waits on it. A build with no meter
    /// anywhere behaves identically, which is the same bargain the VFX queue
    /// makes.
    ///
    /// Game time rather than wall clock, deliberately: it is the clock cooldowns
    /// and durations run on, so damage per second here is damage per second of
    /// the fight rather than of the hit-stop.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ElementReactionSystem))]
    [UpdateBefore(typeof(DamageResolutionSystem))]
    public partial struct DamageMeterSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DamageMeter>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float now = (float)SystemAPI.Time.ElapsedTime;

            // WithPresent is load-bearing, not decoration: the reset flag is
            // down on every meter almost all of the time, and without it this
            // query would only ever see a meter on the single frame somebody
            // pressed the button — which is to say, it would never record
            // anything at all.
            foreach ((DynamicBuffer<DamageMeterEntry> log,
                      DynamicBuffer<DamageEvent> damage,
                      RefRO<DamageMeter> meter,
                      EnabledRefRW<DamageMeterReset> reset) in
                     SystemAPI.Query<DynamicBuffer<DamageMeterEntry>,
                         DynamicBuffer<DamageEvent>,
                         RefRO<DamageMeter>,
                         EnabledRefRW<DamageMeterReset>>()
                         .WithPresent<DamageMeterReset>())
            {
                if (reset.ValueRO)
                {
                    log.Clear();
                    reset.ValueRW = false;
                }

                for (int i = 0; i < damage.Length; i++)
                {
                    DamageEvent blow = damage[i];

                    log.Add(new DamageMeterEntry
                    {
                        Timestamp = now,
                        Damage = blow.Amount,
                        Element = blow.Type,
                        SourcePlayerId = blow.SourcePlayerId,
                        FromReaction = blow.FromReaction,
                        FromTrigger = blow.FromTrigger
                    });
                }

                Prune(log, now - math.max(0.1f, meter.ValueRO.Window));
            }
        }

        /// <summary>
        /// Drops everything older than the window.
        ///
        /// Entries are appended in time order, so the expired ones are a prefix
        /// and one RemoveRange handles them. That is what keeps the buffer
        /// bounded by the rate of fire rather than by a capacity somebody has to
        /// guess at.
        /// </summary>
        private static void Prune(DynamicBuffer<DamageMeterEntry> log, float cutoff)
        {
            int expired = 0;

            while (expired < log.Length && log[expired].Timestamp < cutoff)
                expired++;

            if (expired > 0)
                log.RemoveRange(0, expired);
        }
    }
}
