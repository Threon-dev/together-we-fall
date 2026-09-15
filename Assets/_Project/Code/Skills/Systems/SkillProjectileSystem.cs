using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Moves projectiles, finds what they touch, and retires them.
    ///
    /// The flight and the impact test are one Burst job; forking and destruction
    /// happen afterwards on the main thread, because both are structural. That
    /// split is the same one the wave spawner makes and for the same reason —
    /// the per-frame work is data, the rare work is structure.
    ///
    /// A projectile carries everything it needs to resolve its own impact, so
    /// this system never looks up the skill that fired it. It also means a fork
    /// is nothing special: two more projectiles with one fewer fork left.
    ///
    /// Walls stop them. Each frame's step is ray-cast against the level's
    /// colliders on the main thread first (WallQuery), and the job is handed
    /// where each one would meet a wall. A projectile that does stops there and
    /// lands: a burst goes off against the wall, an on-impact trigger fires —
    /// but it does not fork, because forking is for bodies.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SkillCastSystem))]
    public partial struct SkillProjectileSystem : ISystem
    {
        /// <summary>How far to either side a fork leaves the line of flight.</summary>
        private const float ForkAngleDegrees = 22f;

        /// <summary>
        /// No skill to trigger. Not zero, because zero is the first skill in the
        /// database and a default-constructed field would silently cast it.
        /// </summary>
        private const int NoTrigger = -1;

        private EntityQuery _projectileQuery;
        private EntityQuery _spentQuery;
        private EntityQuery _freeProjectileQuery;
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            // Both flags are enableable, and between them they say everything:
            // Active means in the air, Spent means finished this frame. A pooled
            // projectile waiting to be fired matches neither query.
            _projectileQuery = SystemAPI.QueryBuilder()
                .WithAll<SkillProjectile, ProjectileActive, LocalTransform>()
                .Build();

            _spentQuery = SystemAPI.QueryBuilder()
                .WithAll<SkillProjectile, ProjectileSpent, ProjectileActive, LocalTransform>()
                .Build();

            _freeProjectileQuery = SystemAPI.QueryBuilder()
                .WithAll<SkillProjectile>()
                .WithDisabled<ProjectileActive>()
                .Build();

            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate<SkillPrefabs>();
            state.RequireForUpdate(_projectileQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // TempJob, not Temp, for everything the job below touches. Run still
            // goes through the job scheduler, and the scheduler rejects Temp
            // containers in job fields outright — Temp memory is not guaranteed
            // to outlive the call that made it. They are disposed at the end of
            // this update either way, because Run finishes before it returns.
            using NativeArray<Entity> enemies = _enemyQuery.ToEntityArray(Allocator.TempJob);
            using NativeArray<LocalTransform> enemyTransforms =
                _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            using var hits = new NativeList<PendingHit>(8, Allocator.TempJob);
            using var areas = new NativeList<PendingArea>(4, Allocator.TempJob);
            using var casts = new NativeList<PendingCast>(4, Allocator.TempJob);
            using var effects = new NativeList<VfxEvent>(4, Allocator.TempJob);

            // Run rather than Schedule: the results are needed in this same
            // update, before the structural changes below. Burst still compiles
            // it, it simply runs on this thread.
            //
            // But Run schedules with NO dependency at all — it passes a default
            // handle — so anything already reading what this job writes has to
            // be finished first. LocalTransform is exactly that: the transform
            // systems read it, and this job moves projectiles through it. The
            // same call PathfindingSystem makes before touching entity data
            // from the main thread.
            state.CompleteDependency();

            float deltaTime = SystemAPI.Time.DeltaTime;

            // PhysX answers on this thread only, so the walls are found before
            // the job and handed to it rather than asked from inside it.
            using var walls = new NativeHashMap<Entity, float3>(0, Allocator.TempJob);
            FindWalls(walls, deltaTime);

            new MoveProjectilesJob
            {
                DeltaTime = deltaTime,
                Enemies = enemies,
                EnemyTransforms = enemyTransforms,
                Walls = walls,
                Hits = hits,
                Areas = areas,
                Casts = casts,
                Effects = effects
            }.Run();

            AppendEvents(ref state, hits, areas, casts, effects);
            RetireSpentProjectiles(ref state);
        }

        /// <summary>
        /// Where each projectile in the air would meet a wall during this
        /// frame's step, for the ones that would.
        /// </summary>
        private void FindWalls(NativeHashMap<Entity, float3> walls, float deltaTime)
        {
            using NativeArray<Entity> flying = _projectileQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _projectileQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using NativeArray<SkillProjectile> projectiles =
                _projectileQuery.ToComponentDataArray<SkillProjectile>(Allocator.Temp);

            int mask = WallQuery.Mask();

            for (int i = 0; i < flying.Length; i++)
            {
                float3 from = transforms[i].Position;
                float3 to = from + projectiles[i].Velocity * deltaTime;

                if (WallQuery.Cast(from, to, mask, out float3 stop))
                    walls.Add(flying[i], stop);
            }
        }

        private void AppendEvents(
            ref SystemState state,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<PendingCast> casts,
            NativeList<VfxEvent> effects)
        {
            // Optional, like every presentation queue: a build with no
            // presenter stops projectiles on walls all the same.
            if (effects.Length > 0 &&
                SystemAPI.TryGetSingletonBuffer<VfxEvent>(out DynamicBuffer<VfxEvent> vfx))
            {
                for (int i = 0; i < effects.Length; i++)
                    vfx.Add(effects[i]);
            }

            if (hits.Length > 0)
            {
                DynamicBuffer<PendingHit> buffer = SystemAPI.GetSingletonBuffer<PendingHit>();
                for (int i = 0; i < hits.Length; i++)
                    buffer.Add(hits[i]);
            }

            if (areas.Length > 0)
            {
                DynamicBuffer<PendingArea> areaBuffer = SystemAPI.GetSingletonBuffer<PendingArea>();
                for (int i = 0; i < areas.Length; i++)
                    areaBuffer.Add(areas[i]);
            }

            if (casts.Length == 0)
                return;

            // Drained by SkillCastSystem, which runs before this one — so a
            // triggered skill goes off on the next frame. Sixteen milliseconds
            // after the impact that caused it, which nobody can see, and the
            // alternative is a cast system that runs twice.
            DynamicBuffer<PendingCast> castBuffer = SystemAPI.GetSingletonBuffer<PendingCast>();
            for (int i = 0; i < casts.Length; i++)
                castBuffer.Add(casts[i]);
        }

        /// <summary>
        /// Puts whatever the finished projectiles split into back in the air,
        /// and returns the finished ones to the pool.
        ///
        /// Neither half is a structural change any more, so the arrays gathered
        /// at the top stay valid throughout — which is what lets the release
        /// happen before the forks are handed out.
        /// </summary>
        private void RetireSpentProjectiles(ref SystemState state)
        {
            using NativeArray<Entity> finished = _spentQuery.ToEntityArray(Allocator.Temp);
            if (finished.Length == 0)
                return;

            using NativeArray<SkillProjectile> projectiles =
                _spentQuery.ToComponentDataArray<SkillProjectile>(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _spentQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            using var forks = new NativeList<ProjectileSpawn>(4, Allocator.Temp);

            for (int i = 0; i < finished.Length; i++)
            {
                // A projectile that timed out splits into nothing: forking is
                // what happens when it lands, not when it gives up.
                if (!projectiles[i].HitSomething || projectiles[i].ForksRemaining <= 0)
                    continue;

                AddForks(projectiles[i], transforms[i].Position, forks);
            }

            // Released first, so a fork can be given the very projectile that
            // just landed. Nothing here is destroyed and nothing is created.
            ProjectileSpawn.Release(state.EntityManager, finished);

            if (forks.Length == 0)
                return;

            using NativeArray<Entity> free = _freeProjectileQuery.ToEntityArray(Allocator.Temp);
            ProjectileSpawn.ActivateAll(state.EntityManager, free, forks);
        }

        private static void AddForks(
            SkillProjectile projectile, float3 position, NativeList<ProjectileSpawn> forks)
        {
            float speed = math.length(projectile.Velocity);
            float3 direction = speed > 1e-4f
                ? projectile.Velocity / speed
                : new float3(0f, 0f, 1f);

            for (int side = -1; side <= 1; side += 2)
            {
                SkillProjectile fork = projectile;
                fork.ForksRemaining = projectile.ForksRemaining - 1;
                fork.Velocity = Rotate(direction, side * math.radians(ForkAngleDegrees)) * speed;

                // Cleared, because the copy inherited it from a projectile that
                // just landed. A fork that is born having already hit something
                // would fork again the moment it timed out.
                fork.HitSomething = false;

                forks.Add(new ProjectileSpawn
                {
                    Position = position,
                    Projectile = fork
                });
            }
        }

        private static float3 Rotate(float3 direction, float angle)
        {
            math.sincos(angle, out float sin, out float cos);

            return new float3(
                direction.x * cos - direction.z * sin,
                0f,
                direction.x * sin + direction.z * cos);
        }

        /// <summary>
        /// WithPresent is required, not decoration: ProjectileSpent is disabled
        /// on every projectile still in flight, which is precisely the set this
        /// job exists to move.
        /// </summary>
        [BurstCompile]
        [WithAll(typeof(ProjectileActive))]
        [WithPresent(typeof(ProjectileSpent))]
        private partial struct MoveProjectilesJob : IJobEntity
        {
            public float DeltaTime;

            [ReadOnly] public NativeArray<Entity> Enemies;
            [ReadOnly] public NativeArray<LocalTransform> EnemyTransforms;

            /// <summary>Where a projectile meets a wall this frame, keyed by projectile.</summary>
            [ReadOnly] public NativeHashMap<Entity, float3> Walls;

            public NativeList<PendingHit> Hits;
            public NativeList<PendingArea> Areas;
            public NativeList<PendingCast> Casts;
            public NativeList<VfxEvent> Effects;

            private void Execute(
                Entity entity,
                ref LocalTransform transform,
                ref SkillProjectile projectile,
                EnabledRefRW<ProjectileSpent> isSpent)
            {
                // Already finished earlier this frame and waiting to be cleaned
                // up. Moving it again would drag the impact point along with it.
                if (isSpent.ValueRO)
                    return;

                // A wall in this step ends the flight at the wall. Moved there
                // before bodies are looked for, so one standing on this side of
                // it is still hit, and one on the far side is out of reach of a
                // projectile that never got there.
                bool walled = Walls.TryGetValue(entity, out float3 wall);

                transform.Position = walled
                    ? wall
                    : transform.Position + projectile.Velocity * DeltaTime;

                projectile.Lifetime -= DeltaTime;

                var targets = new EnemyTargets
                {
                    Entities = Enemies,
                    Transforms = EnemyTransforms
                };

                int index = FindTarget(targets, transform.Position, projectile);

                if (index >= 0)
                {
                    Impact(Enemies[index], transform.Position, projectile);

                    // Recorded before retiring, because the forks made from this
                    // projectile inherit it and it is the only thing standing
                    // between them and the body they were born inside. A
                    // piercing projectile needs it for the same reason: it is
                    // still standing inside what it just hit.
                    projectile.LastHitTarget = Enemies[index];

                    if (projectile.PiercesRemaining > 0)
                    {
                        projectile.PiercesRemaining--;

                        // The trigger has already fired, for this impact. A
                        // trigger goes off once per effect instance, and a
                        // projectile that passes through a line of bodies is
                        // one effect — leaving this set would make the number
                        // of triggered casts depend on how crowded the room is,
                        // which is the property that rule exists to avoid.
                        projectile.TriggerSkillIndex = NoTrigger;
                        return;
                    }

                    Retire(ref projectile, isSpent, hitSomething: true);
                    return;
                }

                // Nothing to hit this side of the wall: it lands on the wall.
                // Not as having hit something, so it does not fork off the
                // stone — a split is what happens in a body.
                if (walled)
                {
                    Impact(Entity.Null, transform.Position, projectile);
                    Retire(ref projectile, isSpent, hitSomething: false);
                    return;
                }

                if (projectile.Lifetime > 0f)
                    return;

                Retire(ref projectile, isSpent, hitSomething: false);
            }

            /// <summary>
            /// The nearest enemy in range, skipping the one this projectile came
            /// out of.
            ///
            /// Only forks carry that exclusion; a freshly cast projectile has no
            /// history and takes the plain path, which is the one almost every
            /// projectile in the air is on.
            /// </summary>
            private int FindTarget(
                in EnemyTargets targets, float3 position, in SkillProjectile projectile)
            {
                if (projectile.LastHitTarget == Entity.Null)
                    return targets.FindNearest(position, projectile.HitRadius);

                var born = new FixedList64Bytes<Entity>();
                born.Add(projectile.LastHitTarget);

                return targets.FindNearestUnvisited(position, projectile.HitRadius, born);
            }

            private static void Retire(
                ref SkillProjectile projectile,
                EnabledRefRW<ProjectileSpent> isSpent,
                bool hitSomething)
            {
                projectile.HitSomething = hitSomething;
                isSpent.ValueRW = true;
            }

            /// <summary>
            /// A projectile with an impact radius bursts; one without hits the
            /// single target it touched. Both go through the same queues as
            /// everything else, so neither needs its own path into damage.
            ///
            /// A null target is a wall. The trigger and the burst go off as they
            /// would anywhere; a single-target projectile has nobody to hurt, so
            /// all that is left of it is the impact effect — which SkillHitSystem
            /// draws only for blows on bodies, and so is announced here.
            /// </summary>
            private void Impact(Entity target, float3 position, in SkillProjectile projectile)
            {
                if (target == Entity.Null && projectile.VfxId != 0)
                {
                    Effects.Add(new VfxEvent
                    {
                        Kind = VfxEventKind.SkillHit,
                        Position = position,
                        VfxId = projectile.VfxId
                    });
                }

                // Once per impact, whatever else this projectile does. A trigger
                // fires per effect instance, never per body — the same rule that
                // keeps a burst catching forty enemies from triggering forty
                // times.
                if (projectile.TriggerSkillIndex >= 0)
                {
                    Casts.Add(new PendingCast
                    {
                        SkillIndex = projectile.TriggerSkillIndex,
                        PlayerId = projectile.SourcePlayerId,
                        Origin = position,
                        Direction = math.normalizesafe(projectile.Velocity, new float3(0f, 0f, 1f)),
                        DamageScale = projectile.TriggerDamageScale,
                        Depth = projectile.TriggerDepth + 1,

                        // The body this projectile actually touched, not
                        // whatever happens to be nearest to where it stopped.
                        PreferredTarget = target
                    });
                }

                if (projectile.ImpactRadius > 0f)
                {
                    Areas.Add(new PendingArea
                    {
                        Position = position,
                        Direction = math.normalizesafe(projectile.Velocity, new float3(0f, 0f, 1f)),
                        Radius = projectile.ImpactRadius,
                        ArcCosine = -1f,
                        Damage = projectile.Damage,
                        Type = projectile.Type,
                        SourcePlayerId = projectile.SourcePlayerId,
                        Delay = 0f,

                        // The look, handed on the same way the single-target
                        // branch below hands it: a projectile that bursts still
                        // produces blows, and they are still this skill's.
                        VfxId = projectile.VfxId,

                        // The chain survives the blast now, handed to one body
                        // inside it. A projectile with an impact radius used to
                        // swallow every jump it was carrying — which made the
                        // three chain gems dead weight on the one skill in the
                        // library that bursts on impact, with nothing saying so.
                        ChainsRemaining = projectile.ChainsRemaining,
                        ChainRange = projectile.ChainRange,
                        ChainDelay = projectile.ChainDelay,
                        ExplosionRadius = projectile.ExplosionRadius,
                        ExplosionDamage = projectile.ExplosionDamage,
                        CullThreshold = projectile.CullThreshold,
                        ManaOnKill = projectile.ManaOnKill,
                        CritChance = projectile.CritChance,
                        CritMultiplier = projectile.CritMultiplier,

                        // Whatever it gathered on the way is still on it when it
                        // bursts. A blast that arrived through a wall of fire is
                        // carrying fire into everything it catches.
                        CarriedElements = projectile.CarriedElements,

                        // As is the status it was told to apply, which every
                        // body the burst catches is then marked with — checked
                        // against each of their own immunities, one at a time.
                        AppliedStatus = projectile.AppliedStatus,

                        // Explicitly nothing. The trigger already fired above,
                        // for this same impact, and zero is a real skill index —
                        // leaving the field at its default would make every
                        // impact burst cast the first skill in the database.
                        TriggerSkillIndex = NoTrigger
                    });

                    return;
                }

                if (target == Entity.Null)
                    return;

                var hit = new PendingHit
                {
                    Target = target,

                    // The impact point, and therefore where a chain jump looks
                    // from. Left at the default it would be the world origin.
                    Origin = position,

                    Damage = projectile.Damage,
                    Type = projectile.Type,
                    SourcePlayerId = projectile.SourcePlayerId,
                    ChainsRemaining = projectile.ChainsRemaining,
                    ChainRange = projectile.ChainRange,
                    ChainDelay = projectile.ChainDelay,
                    Delay = 0f,
                    ExplosionRadius = projectile.ExplosionRadius,
                    ExplosionDamage = projectile.ExplosionDamage,
                    CullThreshold = projectile.CullThreshold,
                    ManaOnKill = projectile.ManaOnKill,
                    CritChance = projectile.CritChance,
                    CritMultiplier = projectile.CritMultiplier,

                    // The look, handed from the thing that flew to the blow it
                    // became: the impact effect belongs to the skill, and this
                    // is the last stage that still knows which skill that was.
                    VfxId = projectile.VfxId,

                    // What it picked up on the way. This is the point of the
                    // whole overlap stage: the element gathered in flight arrives
                    // at the target beside the projectile's own.
                    CarriedElements = projectile.CarriedElements,
                    AppliedStatus = projectile.AppliedStatus,

                    // A projectile from a triggered cast carries the depth it
                    // was cast at, so what fired it is already written down.
                    FromTrigger = projectile.TriggerDepth > 0
                };

                hit.Visited.Add(target);
                Hits.Add(hit);
            }
        }
    }
}
