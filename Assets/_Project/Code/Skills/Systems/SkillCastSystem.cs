using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Turns cast requests into things that exist in the world.
    ///
    /// This is where supports are applied, and the only place they are: the fold
    /// in SkillDatabase.Resolve runs once here, and everything downstream works
    /// from the numbers it produced. A projectile in flight has no idea which
    /// supports made it, which is exactly why the skill can be re-cast or
    /// changed while it is still travelling.
    ///
    /// It is also where equipment finally does something. Damage from the
    /// character sheet is added to every skill, and attack speed shortens every
    /// cooldown — so a weapon picked out of a chest is felt rather than merely
    /// listed on a panel.
    ///
    /// Not Bursted and on the main thread, like the wave spawner and for the
    /// same reason: instantiating is a structural change that syncs the world
    /// anyway, and it happens on the frames a player presses a button, not every
    /// frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SkillCastSystem : ISystem
    {
        /// <summary>How far apart multicast copies fan out, in degrees.</summary>
        private const float MulticastSpreadDegrees = 9f;

        /// <summary>How wide a chain bolt looks for its first target. Sixty degrees each way.</summary>
        private const float BoltAcquireCosine = 0.5f;

        /// <summary>How close a projectile counts as touching an enemy.</summary>
        private const float ProjectileHitRadius = 0.7f;

        /// <summary>
        /// Below this, the opening stroke of a bolt is not drawn.
        ///
        /// A bolt triggered on a body starts on that body, so the line from
        /// where it began to what it struck has no length. Drawing it puts a
        /// smear on the corpse; skipping it lets the chain read as appearing at
        /// the target and travelling outward, which is what happened.
        /// </summary>
        private const float MinimumStrikeLength = 0.6f;

        /// <summary>Distance in front of the caster a projectile appears at.</summary>
        private const float MuzzleOffset = 0.9f;

        private EntityQuery _enemyQuery;
        private EntityQuery _characterQuery;
        private EntityQuery _freeProjectileQuery;

        public void OnCreate(ref SystemState state)
        {
            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, PlayerStats, SkillSlot>()
                .Build();

            // The pool, seen from the other side: everything not currently in
            // the air. WithDisabled is what makes "idle" a query rather than a
            // list somebody has to keep.
            _freeProjectileQuery = SystemAPI.QueryBuilder()
                .WithAll<SkillProjectile>()
                .WithDisabled<ProjectileActive>()
                .Build();

            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<SkillTriggerSettings>();
            state.RequireForUpdate<SkillPrefabs>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            TickCooldowns(ref state, SystemAPI.Time.DeltaTime);

            DynamicBuffer<SkillCastRequest> requests =
                SystemAPI.GetSingletonBuffer<SkillCastRequest>();
            DynamicBuffer<PendingCast> triggered = SystemAPI.GetSingletonBuffer<PendingCast>();

            if (requests.Length == 0 && triggered.Length == 0)
                return;

            // Taken and cleared up front: a request that produced nothing must
            // not sit in the queue waiting to produce nothing again.
            using NativeArray<SkillCastRequest> pending = requests.ToNativeArray(Allocator.Temp);
            using NativeArray<PendingCast> pendingTriggers =
                triggered.ToNativeArray(Allocator.Temp);

            requests.Clear();
            triggered.Clear();

            SkillDatabase skills = SystemAPI.GetSingleton<SkillDatabase>();
            int maxTriggerDepth = SystemAPI.GetSingleton<SkillTriggerSettings>().MaxDepth;

            using NativeArray<Entity> enemies = _enemyQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> enemyTransforms =
                _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var targets = new EnemyTargets { Entities = enemies, Transforms = enemyTransforms };

            using var hits = new NativeList<PendingHit>(4, Allocator.Temp);
            using var areas = new NativeList<PendingArea>(4, Allocator.Temp);
            using var spawns = new NativeList<ProjectileSpawn>(8, Allocator.Temp);
            using var effects = new NativeList<VfxEvent>(4, Allocator.Temp);

            for (int i = 0; i < pending.Length; i++)
                Cast(ref state, pending[i], skills, targets, hits, areas, spawns, effects);

            for (int i = 0; i < pendingTriggers.Length; i++)
            {
                CastTriggered(
                    ref state, pendingTriggers[i], skills, maxTriggerDepth,
                    targets, hits, areas, spawns, effects);
            }

            // Buffer writes before structural ones: instantiating below would
            // invalidate every buffer taken above.
            AppendEvents(ref state, hits, areas, effects);
            SpawnProjectiles(ref state, spawns);
        }

        private void TickCooldowns(ref SystemState state, float deltaTime)
        {
            foreach (DynamicBuffer<SkillSlot> iterated in
                     SystemAPI.Query<DynamicBuffer<SkillSlot>>().WithAll<PlayerCharacter>())
            {
                // Copied into a local because a foreach variable is readonly, and
                // writing through its indexer counts as modifying it. A
                // DynamicBuffer is a handle, so the copy addresses the same data.
                DynamicBuffer<SkillSlot> slots = iterated;

                for (int i = 0; i < slots.Length; i++)
                {
                    SkillSlot slot = slots[i];
                    if (slot.CooldownRemaining <= 0f)
                        continue;

                    slot.CooldownRemaining = math.max(0f, slot.CooldownRemaining - deltaTime);
                    slots[i] = slot;
                }
            }
        }

        private void Cast(
            ref SystemState state,
            in SkillCastRequest request,
            SkillDatabase skills,
            in EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<ProjectileSpawn> spawns,
            NativeList<VfxEvent> effects)
        {
            if (!TryGetCharacter(ref state, request.PlayerId, out Entity character))
                return;

            DynamicBuffer<SkillSlot> slots = state.EntityManager.GetBuffer<SkillSlot>(character);
            if (request.SlotIndex < 0 || request.SlotIndex >= slots.Length)
                return;

            SkillSlot slot = slots[request.SlotIndex];

            // The cooldown is the host's answer, not the client's. A client that
            // spams the button gets exactly as many casts as it is owed.
            if (!slot.HasSkill || slot.CooldownRemaining > 0f || !skills.IsValidIndex(slot.SkillIndex))
                return;

            StatBlock stats = state.EntityManager.GetComponentData<PlayerStats>(character).Final;
            ResolvedSkill resolved = skills.Resolve(slot.SkillIndex, stats);

            var context = new CastContext
            {
                PlayerId = request.PlayerId,
                Origin = request.Origin,
                AimPoint = request.AimPoint,

                // A player press names no target. What it hits is the host's to
                // work out, exactly as with every other request.
                PreferredTarget = Entity.Null,

                // A player press is the top of the chain. Anything it triggers
                // starts counting from here.
                Depth = 0
            };

            float3 direction = Flatten(request.Direction);

            // Multicast is one cooldown and several effects, not several casts.
            bool produced = false;
            for (int c = 0; c < resolved.Casts; c++)
            {
                produced |= Emit(
                    resolved, context, Spread(direction, c, resolved.Casts),
                    targets, hits, areas, spawns, effects);
            }

            // A cast that found nothing to do costs nothing. Only a bolt can
            // fail this way — everything else goes off wherever it was pointed —
            // and charging a cooldown for a bolt that had no target reads as the
            // skill being broken rather than as having missed.
            if (!produced)
                return;

            slot.CooldownRemaining = resolved.Cooldown;
            slots[request.SlotIndex] = slot;
        }

        /// <summary>
        /// Casts a skill that something other than a player asked for.
        ///
        /// Two things it deliberately does not do. It charges no cooldown: a
        /// trigger is a consequence, and a consequence that could be rate-limited
        /// by the slot it was never in makes no sense. And it does not check
        /// whether the caster still has that skill equipped — the moment that
        /// decided this was going to happen has already passed.
        ///
        /// The depth budget is the only rail. Two skills that trigger each other
        /// are an easy thing to author by accident, and without a ceiling the
        /// first hit would fill the frame.
        /// </summary>
        private void CastTriggered(
            ref SystemState state,
            in PendingCast cast,
            SkillDatabase skills,
            int maxDepth,
            in EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<ProjectileSpawn> spawns,
            NativeList<VfxEvent> effects)
        {
            if (cast.Depth > maxDepth || !skills.IsValidIndex(cast.SkillIndex))
                return;

            // The caster may have died, disconnected or simply stopped existing
            // between the hit and this frame. The skill still goes off — it was
            // already caused — just without anyone's damage bonus behind it.
            StatBlock stats = TryGetCharacter(ref state, cast.PlayerId, out Entity character)
                ? state.EntityManager.GetComponentData<PlayerStats>(character).Final
                : StatBlock.Zero();

            ResolvedSkill resolved = skills.Resolve(cast.SkillIndex, stats);

            resolved.Damage *= cast.DamageScale;
            resolved.ExplosionDamage *= cast.DamageScale;

            var context = new CastContext
            {
                PlayerId = cast.PlayerId,
                Origin = cast.Origin,

                // Where it landed is the aim. A triggered burst goes off at the
                // impact point rather than at arm's length beyond it.
                AimPoint = cast.Origin,
                PreferredTarget = cast.PreferredTarget,
                Depth = cast.Depth
            };

            float3 direction = Flatten(cast.Direction);

            for (int c = 0; c < resolved.Casts; c++)
            {
                Emit(
                    resolved, context, Spread(direction, c, resolved.Casts),
                    targets, hits, areas, spawns, effects);
            }
        }

        /// <summary>
        /// Produces one instance of the skill. Returns whether anything actually
        /// came of it, which is what decides if the cooldown is charged.
        /// </summary>
        private static bool Emit(
            in ResolvedSkill skill,
            in CastContext context,
            float3 direction,
            in EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<ProjectileSpawn> spawns,
            NativeList<VfxEvent> effects)
        {
            switch (skill.Effect)
            {
                case SkillEffectKind.Projectile:
                    spawns.Add(new ProjectileSpawn
                    {
                        Position = context.Origin + direction * MuzzleOffset,
                        Projectile = new SkillProjectile
                        {
                            Velocity = direction * skill.ProjectileSpeed,
                            Damage = skill.Damage,
                            Type = skill.Type,
                            SourcePlayerId = context.PlayerId,
                            Lifetime = skill.Range / math.max(1f, skill.ProjectileSpeed),
                            HitRadius = ProjectileHitRadius,
                            ImpactRadius = skill.Radius,
                            ForksRemaining = skill.Forks,
                            ChainsRemaining = skill.Chains,
                            ChainRange = skill.ChainRange,
                            ChainDelay = skill.ChainDelay,
                            ExplosionRadius = skill.ExplosionRadius,
                            ExplosionDamage = skill.ExplosionDamage,

                            // Carried on the projectile, like everything else it
                            // needs to resolve its own impact.
                            TriggerSkillIndex = skill.TriggerSkillIndex,
                            TriggerDamageScale = skill.TriggerDamageScale,
                            TriggerDepth = context.Depth
                        }
                    });
                    return true;

                case SkillEffectKind.MeleeArc:
                    areas.Add(MakeArea(skill, context, context.Origin, direction));
                    return true;

                case SkillEffectKind.AreaBurst:
                    areas.Add(MakeArea(
                        skill,
                        context,
                        ClampToRange(context.Origin, context.AimPoint, skill.Range),
                        direction));
                    return true;

                case SkillEffectKind.ChainBolt:
                    return EmitBolt(skill, context, direction, targets, hits, effects);

                default:
                    return false;
            }
        }

        /// <summary>
        /// A bolt does not carry a trigger. A trigger fires once per effect
        /// instance, and a chain is one effect that happens to touch several
        /// bodies — firing per jump would make the count depend on how crowded
        /// the room is, which is exactly the property the rule exists to avoid.
        /// </summary>
        private static bool EmitBolt(
            in ResolvedSkill skill,
            in CastContext context,
            float3 direction,
            in EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<VfxEvent> effects)
        {
            // Something caused this and named what it hit — a projectile
            // landing on a body. That answer beats any search: "the one I hit"
            // and "the one nearest to where I hit" are the same thing until two
            // enemies are standing together, which is when it matters.
            //
            // It may be gone: the blow that triggered this could have killed it
            // in the frame between. Then the searches below take over.
            int index = context.PreferredTarget != Entity.Null
                ? targets.IndexOf(context.PreferredTarget)
                : -1;

            // Aim next: what you are pointing at wins, so the bolt stays
            // something you steer rather than something that picks for you.
            if (index < 0)
            {
                index = targets.FindNearestInArc(
                    context.Origin, direction, BoltAcquireCosine, skill.Range);
            }

            // Nothing in the cone: take the nearest body in any direction. A
            // skill that goes silent because the aim was a few degrees off reads
            // as broken, and the aim still decides whenever it can.
            if (index < 0)
                index = targets.FindNearest(context.Origin, skill.Range);

            if (index < 0)
                return false;

            float3 struck = targets.PositionOf(index);

            var hit = new PendingHit
            {
                Target = targets.Entities[index],

                // Where the bolt landed, and therefore where the next jump looks
                // from. Leaving it at the default put every chain search and
                // every drawn line at the world origin.
                Origin = struck,

                Damage = skill.Damage,
                Type = skill.Type,
                SourcePlayerId = context.PlayerId,
                ChainsRemaining = skill.Chains,
                ChainRange = skill.ChainRange,
                ChainDelay = skill.ChainDelay,
                Delay = 0f,
                ExplosionRadius = skill.ExplosionRadius,
                ExplosionDamage = skill.ExplosionDamage
            };

            hit.Visited.Add(targets.Entities[index]);
            hits.Add(hit);

            // The first stroke, from the caster to whatever it found. Every jump
            // after this one is drawn by the hit system, which knows both of its
            // ends — but nothing else knows where the bolt came from.
            //
            // Unless it came from the target itself, which is what a bolt
            // triggered on impact does. Then there is no stroke to draw.
            if (math.distancesq(context.Origin, struck) >= MinimumStrikeLength * MinimumStrikeLength)
            {
                effects.Add(new VfxEvent
                {
                    Kind = VfxEventKind.BoltStrike,
                    Position = context.Origin,
                    EndPosition = struck,
                    Color = DamageTypePalette.For(skill.Type),
                    Magnitude = math.max(0.05f, skill.ChainDelay)
                });
            }

            return true;
        }

        private static PendingArea MakeArea(
            in ResolvedSkill skill,
            in CastContext context,
            float3 position,
            float3 direction) => new PendingArea
        {
            Position = position,
            Direction = direction,

            // A skill with no radius authored would otherwise hit nothing at
            // all, which reads as the skill being broken rather than unfinished.
            Radius = math.max(1f, skill.Radius),
            ArcCosine = skill.ArcCosine,
            Damage = skill.Damage,
            Type = skill.Type,
            SourcePlayerId = context.PlayerId,
            Delay = 0f,
            ExplosionRadius = skill.ExplosionRadius,
            ExplosionDamage = skill.ExplosionDamage,
            TriggerSkillIndex = skill.TriggerSkillIndex,
            TriggerDamageScale = skill.TriggerDamageScale,
            TriggerDepth = context.Depth
        };

        private void AppendEvents(
            ref SystemState state,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<VfxEvent> effects)
        {
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

            if (effects.Length == 0)
                return;

            DynamicBuffer<VfxEvent> effectBuffer = SystemAPI.GetSingletonBuffer<VfxEvent>();
            for (int i = 0; i < effects.Length; i++)
                effectBuffer.Add(effects[i]);
        }

        private void SpawnProjectiles(ref SystemState state, NativeList<ProjectileSpawn> spawns)
        {
            if (spawns.Length == 0)
                return;

            using NativeArray<Entity> free = _freeProjectileQuery.ToEntityArray(Allocator.Temp);
            ProjectileSpawn.ActivateAll(state.EntityManager, free, spawns);
        }

        private bool TryGetCharacter(ref SystemState state, int playerId, out Entity character)
        {
            character = Entity.Null;

            using NativeArray<Entity> entities = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != playerId)
                    continue;

                character = entities[i];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Who is casting, from where, and how deep into a chain of triggers.
        ///
        /// A struct rather than four more parameters: every emit path needs all
        /// of it, and a player press and a trigger differ in exactly these
        /// fields and nothing else — which is what lets both go through one Emit.
        /// </summary>
        private struct CastContext
        {
            public int PlayerId;
            public float3 Origin;
            public float3 AimPoint;

            /// <summary>The body that caused this cast, or Null for a player press.</summary>
            public Entity PreferredTarget;

            public int Depth;
        }

        /// <summary>Flattens onto the ground plane, with a fallback for a zero aim.</summary>
        private static float3 Flatten(float3 direction)
        {
            direction.y = 0f;

            return math.lengthsq(direction) > 1e-4f
                ? math.normalize(direction)
                : new float3(0f, 0f, 1f);
        }

        private static float3 Spread(float3 direction, int index, int count)
        {
            if (count <= 1)
                return direction;

            // Centred on the aim, so an odd number of casts still has one going
            // exactly where the player pointed.
            float angle = (index - (count - 1) * 0.5f) * math.radians(MulticastSpreadDegrees);
            math.sincos(angle, out float sin, out float cos);

            return new float3(
                direction.x * cos - direction.z * sin,
                0f,
                direction.x * sin + direction.z * cos);
        }

        private static float3 ClampToRange(float3 origin, float3 target, float range)
        {
            float3 offset = target - origin;
            offset.y = 0f;

            float distanceSq = math.lengthsq(offset);
            if (distanceSq <= range * range)
                return new float3(target.x, origin.y, target.z);

            float3 clamped = origin + offset / math.sqrt(distanceSq) * range;
            return new float3(clamped.x, origin.y, clamped.z);
        }
    }
}
