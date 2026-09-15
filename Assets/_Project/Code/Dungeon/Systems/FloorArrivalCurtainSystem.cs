using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Curtain;

namespace TogetherWeFall.Dungeon.Systems
{
    /// <summary>
    /// Lifts the curtain once the floor players arrived on is ready to stand on.
    ///
    /// The portal takes the screen down and nothing used to bring it back: the
    /// curtain only ever fades in by itself when its world is created, and the
    /// world outlives a scene load. Ready rather than "the scene loaded",
    /// because until the navmesh is baked there are no waves and nothing to
    /// look at but the floor assembling itself.
    ///
    /// A host decision like the fade out, so every screen opens at the same
    /// moment: the request goes into the host's curtain and CurtainSync carries
    /// it to the clients. Only a curtain still closed for a floor transition is
    /// opened, which makes this once per floor and leaves a scene started
    /// straight from the editor to its own startup fade.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct FloorArrivalCurtainSystem : ISystem
    {
        /// <summary>The same length as the portal's fade out, so leaving and arriving match.</summary>
        private const float FadeSeconds = 0.8f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonRunState>();
            state.RequireForUpdate<CurtainSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<DungeonRunState>().Phase != DungeonRunPhase.Ready)
                return;

            Entity curtain = SystemAPI.GetSingletonEntity<CurtainSingleton>();
            CurtainState current = SystemAPI.GetComponent<CurtainState>(curtain);

            if (current.Target < 1f || current.Reason != CurtainReason.FloorTransition)
                return;

            SystemAPI.GetBuffer<CurtainRequest>(curtain).Add(new CurtainRequest
            {
                Target = 0f,
                Duration = FadeSeconds,
                Colour = new float4(0f, 0f, 0f, 1f),
                Reason = CurtainReason.FloorTransition
            });
        }
    }
}
