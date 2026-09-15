using Unity.Collections;
using Unity.Entities;

namespace TogetherWeFall.Loot.Systems
{
    /// <summary>
    /// Creates every dropped item the game will ever have, once.
    ///
    /// The smallest of the three pools by volume — a chest gives a handful, not
    /// a hundred — but the same shape of cost, and a treasure room cleared in a
    /// hurry is a burst of creates followed by a burst of destroys. Pooling it
    /// costs one system and removes the category rather than leaving an argument
    /// about where the threshold is.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct LootItemPoolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // Both live on the baked loot entity, so this waits for the SubScene
            // rather than assuming the world is ready on frame one.
            state.RequireForUpdate<LootPrefabs>();
            state.RequireForUpdate<LootSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            LootPrefabs prefabs = SystemAPI.GetSingleton<LootPrefabs>();
            LootSettings settings = SystemAPI.GetSingleton<LootSettings>();

            if (prefabs.Item == Entity.Null || settings.ItemPoolSize <= 0)
            {
                state.Enabled = false;
                return;
            }

            using NativeArray<Entity> pooled = state.EntityManager.Instantiate(
                prefabs.Item, settings.ItemPoolSize, Allocator.Temp);

            // The prefab is baked with InteractableTag down, so these are already
            // invisible to the interaction resolver. All that is left is to put
            // them somewhere the camera will not find them.
            LootItemPool.Release(state.EntityManager, pooled);

            UnityEngine.Debug.Log(
                $"[LootItemPoolSystem] {settings.ItemPoolSize} loot items pooled.");

            state.Enabled = false;
        }
    }
}
