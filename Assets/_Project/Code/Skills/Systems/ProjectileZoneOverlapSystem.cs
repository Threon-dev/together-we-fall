using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Hands a zone's element to the projectiles flying through it.
    ///
    /// This is the half of the feature the player can actually see themselves
    /// building: a bolt steered through a wall of fire arrives carrying fire, and
    /// what that is worth is decided at the far end by the same table that
    /// decides what a status is worth. Nothing here knows what fire and lightning
    /// do together — it only knows how to write down that both are present.
    ///
    /// A distance test, not a trigger collider: there is no Unity Physics in this
    /// project, and a zone is a circle on the ground, which is two subtractions
    /// and a compare. The spatial hash SeparationSystem builds is deliberately
    /// not reused here, and would not pay: it is built per frame for hundreds of
    /// enemies, while this compares a couple of hundred projectiles against a
    /// handful of zones. Hashing ten things costs more than looking at them. If
    /// zones ever number in the hundreds, that hash is the upgrade — the same
    /// note EnemyTargets already carries about its own linear searches.
    ///
    /// It runs before the projectiles move, so a projectile is tagged from where
    /// it was rather than where it is going. A zone is metres across and a
    /// projectile crosses a fraction of a metre per frame, so the only case that
    /// misses is one that enters the far edge of a zone and lands in the same
    /// frame — sixteen milliseconds of a fifty-frame journey.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SkillCastSystem))]
    [UpdateBefore(typeof(SkillProjectileSystem))]
    public partial struct ProjectileZoneOverlapSystem : ISystem
    {
        private EntityQuery _zoneQuery;
        private EntityQuery _projectileQuery;

        public void OnCreate(ref SystemState state)
        {
            _zoneQuery = SystemAPI.QueryBuilder()
                .WithAll<ElementZone, ZoneActive, LocalTransform>()
                .Build();

            _projectileQuery = SystemAPI.QueryBuilder()
                .WithAll<SkillProjectile, ProjectileActive, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate(_zoneQuery);
            state.RequireForUpdate(_projectileQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // TempJob for everything the job touches: Run goes through the
            // scheduler too, and the scheduler rejects Temp containers in job
            // fields outright.
            using NativeArray<ElementZone> zones =
                _zoneQuery.ToComponentDataArray<ElementZone>(Allocator.TempJob);
            using NativeArray<LocalTransform> zoneTransforms =
                _zoneQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            using var effects = new NativeList<VfxEvent>(4, Allocator.TempJob);

            state.CompleteDependency();

            new TagProjectilesJob
            {
                Zones = zones,
                ZoneTransforms = zoneTransforms,
                Effects = effects
            }.Run();

            if (effects.Length == 0)
                return;

            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            for (int i = 0; i < effects.Length; i++)
                vfx.Add(effects[i]);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActive))]
        private partial struct TagProjectilesJob : IJobEntity
        {
            [ReadOnly] public NativeArray<ElementZone> Zones;
            [ReadOnly] public NativeArray<LocalTransform> ZoneTransforms;

            public NativeList<VfxEvent> Effects;

            private void Execute(
                in LocalTransform transform,
                ref SkillProjectile projectile,
                ref URPMaterialPropertyBaseColor color)
            {
                for (int i = 0; i < Zones.Length; i++)
                {
                    DamageType element = Zones[i].Element;

                    // Nothing to gain from a zone of the element it already deals,
                    // and nothing to gain from one it has already crossed. The
                    // second check is the whole of "do not stack the same
                    // element": a set has no room for a duplicate, so flying
                    // through two fires leaves one fire.
                    if (element == projectile.Type ||
                        ElementMask.Has(projectile.CarriedElements, element))
                    {
                        continue;
                    }

                    // Flat distance, like every other reach test in the fight:
                    // a zone lies on the ground and a projectile flies at chest
                    // height, so counting the vertical gap would shrink every
                    // zone by a metre for no reason anyone could see.
                    float2 offset = transform.Position.xz - ZoneTransforms[i].Position.xz;
                    if (math.lengthsq(offset) > Zones[i].Radius * Zones[i].Radius)
                        continue;

                    projectile.CarriedElements =
                        ElementMask.With(projectile.CarriedElements, element);

                    // The player has to be able to see that the shot changed
                    // BEFORE it lands, or the reaction at the far end looks like
                    // a number that came from nowhere. Blended rather than
                    // replaced, so what it is carrying reads as an addition to
                    // what it is rather than a different projectile.
                    color.Value = math.lerp(color.Value, DamageTypePalette.For(element), 0.5f);

                    Effects.Add(new VfxEvent
                    {
                        Kind = VfxEventKind.ElementBurst,
                        Position = transform.Position,
                        Color = DamageTypePalette.For(element),
                        Magnitude = 0.8f
                    });
                }
            }
        }
    }
}
