using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Vfx.Systems
{
    /// <summary>
    /// Creates the presentation queue once, at world startup.
    ///
    /// Its own entity rather than a fifth buffer on the skill events one,
    /// because the two are on opposite sides of a line that will matter: the
    /// skill queues are host state, and this is something a client derives and
    /// draws. Keeping them apart means that when the network arrives, the answer
    /// to "does this replicate" is a property of which entity it lives on.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct VfxEventRegistrySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Entity registry = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(registry, "VfxEvents");

            state.EntityManager.AddComponent<VfxEventsSingleton>(registry);
            state.EntityManager.AddBuffer<VfxEvent>(registry);

            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
}
