using Unity.Collections;
using Unity.Entities;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Creates every zone the game will ever have, once.
    ///
    /// The same shape as the projectile pool and for the same reason, at a much
    /// smaller scale: zones are rare, but a fight is exactly when one is lit, and
    /// a structural change in that frame costs the same whether it happens once
    /// or fifty times.
    ///
    /// A scene whose skill database has no zone prefab assigned simply has no
    /// zones. That is the state of every scene built before this feature existed,
    /// and it plays as it always did.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ZonePoolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SkillPrefabs>();
        }

        public void OnUpdate(ref SystemState state)
        {
            SkillPrefabs prefabs = SystemAPI.GetSingleton<SkillPrefabs>();

            int size = prefabs.ZonePoolSize;
            if (size <= 0 || prefabs.Zone == Entity.Null)
            {
                state.Enabled = false;
                return;
            }

            using NativeArray<Entity> pooled = state.EntityManager.Instantiate(
                prefabs.Zone, size, Allocator.Temp);

            ZoneSpawn.Release(state.EntityManager, pooled);

            UnityEngine.Debug.Log(
                $"[ZonePoolSystem] {size} zones pooled. Lighting one is a flag from here on.");

            state.Enabled = false;
        }
    }
}
