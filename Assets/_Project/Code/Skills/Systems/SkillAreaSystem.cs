using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Resolves everything that damages a shape rather than a target: area
    /// bursts, melee swings, projectile impacts and exploding corpses.
    ///
    /// All four are one queue and one system because they are one idea. Giving
    /// each its own path would mean four places to fix the day the arc test is
    /// wrong, and four places for the answer to drift apart.
    ///
    /// The output is ordinary pending hits. An area effect has no privileged
    /// route into damage — it produces the same events a single projectile does,
    /// which is why an explosion can be caused by anything and hurt anything.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SkillProjectileSystem))]
    public partial struct SkillAreaSystem : ISystem
    {
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate<SkillBudgetSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<PendingArea> areas = SystemAPI.GetSingletonBuffer<PendingArea>();
            if (areas.Length == 0)
                return;

            // Taken and cleared, then the ones that are not ready yet go back.
            // Editing a buffer while walking it is the kind of thing that works
            // until an effect adds another one mid-loop.
            using NativeArray<PendingArea> queued = areas.ToNativeArray(Allocator.Temp);
            areas.Clear();

            using NativeArray<Entity> enemies = _enemyQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> enemyTransforms =
                _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var targets = new EnemyTargets { Entities = enemies, Transforms = enemyTransforms };
            DynamicBuffer<PendingHit> hits = SystemAPI.GetSingletonBuffer<PendingHit>();
            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            DynamicBuffer<PendingCast> casts = SystemAPI.GetSingletonBuffer<PendingCast>();

            float deltaTime = SystemAPI.Time.DeltaTime;

            // The same lever as MaxRequestsPerFrame in pathfinding. Four players
            // emptying area skills into one crowd should cost several ordinary
            // frames rather than one frame nobody can ignore; an effect held
            // over goes off next frame, a sixtieth of a second late.
            int budget = SystemAPI.GetSingleton<SkillBudgetSettings>().MaxAreasPerFrame;

            for (int i = 0; i < queued.Length; i++)
            {
                PendingArea area = queued[i];
                area.Delay -= deltaTime;

                // Held over either because it is not due yet or because this
                // frame has done enough. Its delay has already been counted
                // down, so it is first in line next time.
                if (area.Delay > 0f || budget <= 0)
                {
                    areas.Add(area);
                    continue;
                }

                budget--;

                // Announced once per blast, not once per body caught in it.
                // Whether anything draws it is not this system's concern.
                vfx.Add(new VfxEvent
                {
                    Kind = VfxEventKind.Explosion,
                    Position = area.Position,
                    Color = DamageTypePalette.For(area.Type),
                    Magnitude = area.Radius
                });

                // Once per blast, not once per body caught in it — same rule as
                // the effect above, and for the same reason.
                if (area.TriggerSkillIndex >= 0)
                {
                    casts.Add(new PendingCast
                    {
                        SkillIndex = area.TriggerSkillIndex,
                        PlayerId = area.SourcePlayerId,
                        Origin = area.Position,
                        Direction = area.Direction,
                        DamageScale = area.TriggerDamageScale,
                        Depth = area.TriggerDepth + 1
                    });
                }

                Resolve(area, targets, hits);
            }
        }

        private static void Resolve(
            in PendingArea area, in EnemyTargets targets, DynamicBuffer<PendingHit> hits)
        {
            float radiusSq = area.Radius * area.Radius;

            for (int i = 0; i < targets.Length; i++)
            {
                float3 offset = targets.PositionOf(i) - area.Position;

                // Flat distance: enemies stand on the ground and a burst goes off
                // at chest height, so counting the vertical gap would shrink
                // every radius by a metre for no reason anyone could see.
                offset.y = 0f;

                float distanceSq = math.lengthsq(offset);
                if (distanceSq > radiusSq)
                    continue;

                if (!EnemyTargets.IsInsideArc(offset, distanceSq, area.Direction, area.ArcCosine))
                    continue;

                hits.Add(new PendingHit
                {
                    Target = targets.Entities[i],
                    Origin = targets.PositionOf(i),
                    Damage = area.Damage,
                    Type = area.Type,
                    SourcePlayerId = area.SourcePlayerId,

                    // An area effect does not chain. Chaining belongs to the
                    // single-target effects that have somewhere to jump from.
                    ChainsRemaining = 0,
                    Delay = 0f,
                    ExplosionRadius = area.ExplosionRadius,
                    ExplosionDamage = area.ExplosionDamage
                });
            }
        }
    }
}
