using Unity.Collections;
using Unity.Entities;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Creates every projectile the game will ever have, once.
    ///
    /// After this runs there are no more structural changes on the projectile
    /// path at all: firing raises a flag, retiring lowers it. Instantiate and
    /// Destroy were already batched — one call for a whole volley — but each
    /// batch is still a sync point, and in a fight nearly every frame has one.
    ///
    /// The pool size is therefore also the ceiling on projectiles in the air, in
    /// exactly the way MaxAlive caps enemies. Past it a shot is not fired, which
    /// under the kind of barrage that reaches the ceiling is invisible.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct ProjectilePoolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SkillPrefabs>();
        }

        public void OnUpdate(ref SystemState state)
        {
            SkillPrefabs prefabs = SystemAPI.GetSingleton<SkillPrefabs>();

            int size = prefabs.ProjectilePoolSize;
            if (size <= 0 || prefabs.Projectile == Entity.Null)
            {
                state.Enabled = false;
                return;
            }

            using NativeArray<Entity> pooled = state.EntityManager.Instantiate(
                prefabs.Projectile, size, Allocator.Temp);

            // Parked under the floor rather than hidden. Toggling a rendering
            // tag is a structural change, which is the one thing this system
            // exists to stop happening; a projectile a kilometre down is thrown
            // out by the frustum and costs nothing.
            ProjectileSpawn.Release(state.EntityManager, pooled);

            UnityEngine.Debug.Log(
                $"[ProjectilePoolSystem] {size} projectiles pooled. Nothing on this path " +
                "allocates or destroys again.");

            // The pool exists. There is nothing left for this system to do, ever.
            state.Enabled = false;
        }
    }
}
