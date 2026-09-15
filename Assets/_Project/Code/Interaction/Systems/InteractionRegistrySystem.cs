using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Interaction.Systems
{
    /// <summary>
    /// Creates the interaction request queue once, at world startup.
    ///
    /// By a system rather than from a MonoBehaviour, for the same reason as the
    /// player position registry: the queue has to exist whether or not a player
    /// is in the scene, so nothing downstream needs a "has it been created yet"
    /// branch in every frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct InteractionRegistrySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Entity registry = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(registry, "InteractionRequests");
            state.EntityManager.AddComponent<InteractionRequestsSingleton>(registry);
            state.EntityManager.AddBuffer<InteractionRequest>(registry);

            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
}
