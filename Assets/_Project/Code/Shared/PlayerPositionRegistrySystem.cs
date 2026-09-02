using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Shared
{
    /// <summary>
    /// Creates the player position registry entity once, at world startup.
    ///
    /// Done by a system rather than from a MonoBehaviour so the registry exists
    /// whether or not a player is present in the scene: enemy systems may run
    /// earlier and need a valid (if empty) buffer, not a "has it been created
    /// yet" check in every frame.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct PlayerPositionRegistrySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Entity registry = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(registry, "PlayerPositionsRegistry");
            state.EntityManager.AddComponent<PlayerPositionsSingleton>(registry);
            state.EntityManager.AddBuffer<PlayerPositionElement>(registry);

            // Registry is in place — this system has nothing left to do.
            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
}
