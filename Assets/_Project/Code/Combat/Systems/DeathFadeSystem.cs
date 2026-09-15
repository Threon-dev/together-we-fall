using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Enemies;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// Plays a body out and then removes it.
    ///
    /// The whole point is the pause. Destroying an enemy the frame it runs out
    /// of health means a crowd does not die, it stops existing between two
    /// frames — which reads as the wave being switched off rather than killed.
    /// A third of a second of shrinking is enough for the eye to see a hundred
    /// bodies come apart.
    ///
    /// No shader involved. A dissolve would need an authored shader graph; scale
    /// and the per-instance colour override are already there, cost nothing, and
    /// carry the same read. If a dissolve is written later it replaces the two
    /// lines in the job and nothing else.
    ///
    /// The fade runs in parallel, the removal on the main thread, because only
    /// one of those is a structural change.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathReactionSystem))]
    public partial struct DeathFadeSystem : ISystem
    {
        private EntityQuery _fadingQuery;

        public void OnCreate(ref SystemState state)
        {
            // DeathFade is enableable, so this holds exactly the bodies on their
            // way out.
            _fadingQuery = SystemAPI.QueryBuilder()
                .WithAll<DeathFade>()
                .Build();

            state.RequireForUpdate(_fadingQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Removal first, and it removes the bodies that finished fading LAST
            // frame. The order is not cosmetic: touching entity data on the main
            // thread forces a sync, and syncing on a job this same update had
            // just scheduled — before OnUpdate returned it to the dependency
            // manager — is a safety error rather than a stall.
            //
            // The cost is that a finished body survives one more frame at zero
            // scale, which is invisible. The gain is that the fade stays a
            // parallel job with no sync point at all.
            RemoveFinished(ref state);

            new FadeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel();
        }

        /// <summary>
        /// Returns the bodies whose fade ran out on an earlier frame to the pool.
        ///
        /// Not destroyed: an enemy is a pooled body, and the end of the fade is
        /// simply the moment it becomes available again. Reading the components
        /// here still forces a sync, which is why this runs before the job below
        /// is scheduled rather than after it.
        /// </summary>
        private void RemoveFinished(ref SystemState state)
        {
            using NativeArray<Entity> fading = _fadingQuery.ToEntityArray(Allocator.Temp);
            if (fading.Length == 0)
                return;

            using NativeArray<DeathFade> fades =
                _fadingQuery.ToComponentDataArray<DeathFade>(Allocator.Temp);

            using var finished = new NativeList<Entity>(fading.Length, Allocator.Temp);

            for (int i = 0; i < fades.Length; i++)
            {
                if (fades[i].Remaining <= 0f)
                    finished.Add(fading[i]);
            }

            if (finished.Length > 0)
                EnemyPool.Release(state.EntityManager, finished.AsArray());
        }

        [BurstCompile]
        private partial struct FadeJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref LocalTransform transform,
                ref DeathFade fade,
                ref URPMaterialPropertyBaseColor color)
            {
                fade.Remaining -= DeltaTime;

                // One at the moment of death, zero when it is gone.
                float remaining = math.saturate(fade.Remaining / math.max(0.01f, fade.Duration));

                transform.Scale = fade.StartScale * remaining;

                // Darkened rather than made transparent: the enemy material is
                // opaque, and turning it translucent would mean a second
                // material and a second draw call per corpse.
                color.Value = new float4(fade.StartColor.xyz * remaining, fade.StartColor.w);
            }
        }
    }
}
