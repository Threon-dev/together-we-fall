using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace TogetherWeFall.DebugTools.Systems
{
    /// <summary>
    /// Sweeps patrolling dummies back and forth — in both worlds.
    ///
    /// Position from a sine of time rather than a step per frame: a stepped
    /// walker accumulates its own rounding and ends up somewhere it was not
    /// sent, and reversing at the ends needs a direction to remember. From the
    /// clock there is nothing to remember and nothing to drift.
    ///
    /// Both worlds, because a dummy is a static SubScene entity rather than a
    /// ghost: each world bakes its own copy, and nothing carries the host's
    /// position to the copy a screen draws. Moving only the server's left the
    /// host hitting a dummy that every screen showed standing still. The path is
    /// a pure function of the network tick, so a client derives the position
    /// the host had — the same move as the dungeon seed — and derives it at the
    /// interpolation tick, which is when every ghost on that screen is drawn, so
    /// a shot seen landing on the dummy did land.
    ///
    /// Network time rather than world time: each world's clock starts when that
    /// world was made, and a friend's client is made minutes after the host.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DummyPatrolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DummyPatrol>();
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            NetworkTime time = SystemAPI.GetSingleton<NetworkTime>();
            bool server = state.WorldUnmanaged.IsServer();

            NetworkTick tick = server ? time.ServerTick : time.InterpolationTick;
            float fraction = server ? time.ServerTickFraction : time.InterpolationTickFraction;

            if (!tick.IsValid)
                return;

            // A client that has not heard the host's rate yet reads the default,
            // which is also what the host runs unless somebody configured it.
            SystemAPI.TryGetSingleton(out ClientServerTickRate rate);
            rate.ResolveDefaults();

            // A partial tick is the one in progress: tick N at fraction f is
            // N - 1 + f, the same reading NetCode makes when it interpolates.
            double seconds = ((double)tick.TickIndexForValidTick - 1.0 + fraction) / rate.SimulationTickRate;

            new PatrolJob
            {
                Seconds = (float)seconds
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct PatrolJob : IJobEntity
        {
            public float Seconds;

            private void Execute(ref LocalTransform transform, in DummyPatrol patrol)
            {
                float offset = math.sin(Seconds * patrol.Frequency * 2f * math.PI) *
                               patrol.Distance;

                transform.Position = patrol.Origin + patrol.Axis * offset;
            }
        }
    }
}
