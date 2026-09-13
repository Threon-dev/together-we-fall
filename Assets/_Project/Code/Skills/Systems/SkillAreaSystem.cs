using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Audio;
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
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
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
                //
                // Unless it asked not to be. A zone pulsing on a timer produces
                // an area like any other, and announcing each pulse would shake
                // the camera and stop time twice a second for as long as it
                // burns — for an effect that is already visibly on the screen.
                if (!area.Silent)
                {
                    vfx.Add(new VfxEvent
                    {
                        Kind = VfxEventKind.Explosion,
                        Position = area.Position,
                        Color = DamageTypePalette.For(area.Type),
                        Magnitude = area.Radius
                    });

                    SystemAPI.GetSingletonBuffer<AudioEvent>().Add(new AudioEvent
                    {
                        Cue = AudioCue.Explosion,
                        Position = area.Position,

                        // Scaled by the blast, so a big one is heard over a wave
                        // of small ones instead of being gated behind them.
                        Volume = math.clamp(area.Radius * 0.25f, 0.6f, 2f)
                    });
                }

                // A meteor, a step of a fissure, the landing of a leap: the
                // skill's own cast effect, here and now rather than at the press.
                if (area.CastVfx && area.VfxId != 0)
                {
                    vfx.Add(new VfxEvent
                    {
                        Kind = VfxEventKind.SkillCast,
                        Position = area.Position,
                        EndPosition = area.Position + area.Direction,
                        Color = DamageTypePalette.For(area.Type),
                        Magnitude = area.Radius,
                        VfxId = area.VfxId
                    });
                }

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

            // Who carries the chain onward, decided before any hit is written.
            //
            // The body nearest the centre rather than the first one the array
            // happens to hold, so the same blast on the same crowd always
            // chains from the same place — an order-of-iteration answer would
            // make a build that looks identical behave differently depending on
            // which enemy spawned first.
            int chainFrom = area.ChainsRemaining > 0
                ? NearestInside(area, targets, radiusSq)
                : -1;

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

                var hit = new PendingHit
                {
                    Target = targets.Entities[i],
                    Origin = targets.PositionOf(i),
                    Damage = area.Damage,
                    Type = area.Type,
                    SourcePlayerId = area.SourcePlayerId,

                    // So a nova leaves an impact on every body it caught, not
                    // just a ring on the floor. Bounded by the particle pool's
                    // per-prefab ceiling rather than by a rule here: what a
                    // crowd costs is a presentation question.
                    VfxId = area.VfxId,

                    // An area effect chains from exactly one of the bodies it
                    // caught, and only when something asked it to. It used to
                    // chain from none of them, which made every chain gem in a
                    // group with a swing, a burst or a projectile that bursts
                    // on impact into a hole that did nothing and said nothing.
                    ChainsRemaining = i == chainFrom ? area.ChainsRemaining : 0,
                    ChainRange = area.ChainRange,
                    ChainDelay = area.ChainDelay,
                    Delay = 0f,
                    ExplosionRadius = area.ExplosionRadius,
                    ExplosionDamage = area.ExplosionDamage,
                    CullThreshold = area.CullThreshold,
                    ManaOnKill = area.ManaOnKill,

                    // Handed to every body, and each rolls its own in the hit
                    // stage. One roll for the whole blast would be one number
                    // deciding a crowd.
                    CritChance = area.CritChance,
                    CritMultiplier = area.CritMultiplier,

                    // Passed straight through: what the blast was carrying, every
                    // body it caught is struck by, and whether the blast was
                    // itself a reaction decides whether these hits may start one.
                    CarriedElements = area.CarriedElements,
                    AppliedStatus = area.AppliedStatus,
                    FromReaction = area.FromReaction,

                    // Same derivation as a projectile: depth is what a cast was
                    // when it happened, so nothing new had to be carried.
                    FromTrigger = area.TriggerDepth > 0
                };

                // The one body that chains starts its visited list with itself,
                // exactly as a bolt does — without it the first jump lands back
                // on the body the blast is standing on.
                if (i == chainFrom)
                    hit.Visited.Add(hit.Target);

                hits.Add(hit);
            }
        }

        /// <summary>
        /// The body nearest the centre of the blast, or -1 when it catches
        /// nobody.
        ///
        /// Walks the same targets the loop below walks and applies the same two
        /// tests, which is the point: a chain must start from a body that was
        /// actually hit, not from one standing just outside the arc.
        /// </summary>
        private static int NearestInside(
            in PendingArea area, in EnemyTargets targets, float radiusSq)
        {
            int nearest = -1;
            float nearestSq = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                float3 offset = targets.PositionOf(i) - area.Position;
                offset.y = 0f;

                float distanceSq = math.lengthsq(offset);

                if (distanceSq > radiusSq || distanceSq >= nearestSq)
                    continue;

                if (!EnemyTargets.IsInsideArc(offset, distanceSq, area.Direction, area.ArcCosine))
                    continue;

                nearest = i;
                nearestSq = distanceSq;
            }

            return nearest;
        }
    }
}
