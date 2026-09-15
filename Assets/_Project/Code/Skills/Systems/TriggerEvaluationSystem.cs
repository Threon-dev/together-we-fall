using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;
using TogetherWeFall.Combat.Systems;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Decides which sockets cast themselves this frame.
    ///
    /// The one stage of the pipeline that reads no skill and produces a cast.
    /// Everything else in Skills answers "what does this do"; this answers "did
    /// something just happen that a gem was waiting for", and the answer goes
    /// into the very same PendingCast queue a trigger support on impact uses. By
    /// the time SkillCastSystem sees it, an automatic cast and a triggered one
    /// are indistinguishable — which is the point, because the alternative is a
    /// second cast path with its own idea of what a support means.
    ///
    /// It subscribes rather than polls: the two things it can react to announce
    /// themselves into TriggerEvent, so a frame in which nothing died and nothing
    /// caught fire costs a length check and a cooldown tick. The one exception is
    /// OnLowHealth, which is a STATE rather than an event and is therefore asked
    /// every frame — of a component characters do not have yet, so it too costs
    /// a HasComponent and nothing more.
    ///
    /// Main thread and not Bursted, like the cast system it feeds: it walks
    /// buffers on gear entities through the EntityManager, and it does so on the
    /// frames something died rather than on every frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathReactionSystem))]
    public partial struct TriggerEvaluationSystem : ISystem
    {
        /// <summary>
        /// Seeded once, advanced per roll.
        ///
        /// A field on the system rather than a fresh Random per frame: seeding
        /// from the frame number would make every trigger in a frame roll the
        /// same number, and seeding from time would make the result depend on
        /// how long the process had been running. This is host-side combat, not
        /// dungeon generation, so it needs to be fair rather than reproducible.
        /// </summary>
        private Random _random;

        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, EquippedItem, TriggerCooldown>()
                .Build();

            _random = new Random(0x9E3779B9u);

            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<SkillTriggerSettings>();
            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate(_characterQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            DynamicBuffer<TriggerEvent> queue = SystemAPI.GetSingletonBuffer<TriggerEvent>();

            // Taken and cleared up front, exactly as the hit and area stages do:
            // an event that fired nothing must not sit in the queue waiting to
            // fire nothing again next frame.
            using NativeArray<TriggerEvent> events = queue.ToNativeArray(Allocator.Temp);
            queue.Clear();

            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            SkillDatabase skills = SystemAPI.GetSingleton<SkillDatabase>();

            // The depth an automatic cast enters at, and it is the ceiling on
            // purpose: whatever it casts cannot itself trigger anything on
            // impact. That is the "one level of indirection" rule, enforced with
            // the rail that already exists rather than with a second one — a
            // trigger gem may fire a skill, and that skill is the end of the
            // line.
            //
            // It does not stop a trigger firing again off its own kills. The
            // cooldown does that, which is exactly why the cooldown is not
            // optional: a three second trigger that keeps re-arming itself is a
            // trigger that fires every three seconds.
            int depth = SystemAPI.GetSingleton<SkillTriggerSettings>().MaxDepth;

            // Where each player is standing, for the one condition that is about
            // the player rather than about something they hit. The same buffer
            // the enemies read, for the same reason: it is already the answer to
            // "where is player N", and a character entity is a sheet with no
            // body of its own.
            SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<PlayerPositionElement> players);

            using var casts = new NativeList<PendingCast>(4, Allocator.Temp);

            foreach ((DynamicBuffer<TriggerCooldown> cooldowns,
                      DynamicBuffer<EquippedItem> worn,
                      RefRO<PlayerCharacter> character,
                      Entity entity) in
                     SystemAPI.Query<DynamicBuffer<TriggerCooldown>,
                         DynamicBuffer<EquippedItem>,
                         RefRO<PlayerCharacter>>().WithEntityAccess())
            {
                Tick(cooldowns, deltaTime);

                Evaluate(
                    ref state, items, skills, entity, character.ValueRO.PlayerId,
                    depth, PositionOf(players, character.ValueRO.PlayerId),
                    worn, cooldowns, events, casts);
            }

            if (casts.Length == 0)
                return;

            // Drained by SkillCastSystem, which has already run this frame, so
            // an automatic cast goes off on the next one. The same sixteen
            // milliseconds a trigger support on impact waits, and for the same
            // reason: one ordering of the stages, rather than a cast system that
            // runs twice.
            DynamicBuffer<PendingCast> pending = SystemAPI.GetSingletonBuffer<PendingCast>();
            for (int i = 0; i < casts.Length; i++)
                pending.Add(casts[i]);
        }

        /// <summary>
        /// Runs the cooldowns down and drops the ones that expired.
        ///
        /// Dropping rather than keeping at zero is what bounds the buffer: a
        /// character carries as many entries as it has triggers that fired
        /// recently, and none at all the rest of the time.
        /// </summary>
        private static void Tick(DynamicBuffer<TriggerCooldown> cooldowns, float deltaTime)
        {
            // Backwards, so removing one swaps in an entry this pass has already
            // dealt with rather than one it has not.
            for (int i = cooldowns.Length - 1; i >= 0; i--)
            {
                TriggerCooldown cooldown = cooldowns[i];
                cooldown.Remaining -= deltaTime;

                if (cooldown.Remaining <= 0f)
                {
                    cooldowns.RemoveAtSwapBack(i);
                    continue;
                }

                cooldowns[i] = cooldown;
            }
        }

        /// <summary>
        /// Walks one character's sockets and fires whatever is owed.
        ///
        /// A socket at a time rather than an event at a time, because a trigger
        /// gem is what has a cooldown and a chance: three hundred kills in one
        /// frame are three hundred opportunities for one gem, and the loop stops
        /// at the first one that pays out. That is what keeps a chain reaction
        /// from being three hundred casts without pretending it was one kill.
        /// </summary>
        private void Evaluate(
            ref SystemState state,
            ItemDatabase items,
            SkillDatabase skills,
            Entity character,
            int playerId,
            int depth,
            float3 casterPosition,
            DynamicBuffer<EquippedItem> worn,
            DynamicBuffer<TriggerCooldown> cooldowns,
            in NativeArray<TriggerEvent> events,
            NativeList<PendingCast> casts)
        {
            EntityManager entityManager = state.EntityManager;

            // Nothing was announced, and there is no state worth asking about.
            // A frame in which nothing died and nothing caught fire ends here
            // rather than walking sixty sockets to find that out.
            //
            // OnLowHealth is the exception, because it is asked rather than
            // announced — and it needs a Health component the character does not
            // have yet, so today this is every quiet frame.
            if (events.Length == 0 && !entityManager.HasComponent<Health>(character))
                return;

            for (int slot = 0; slot < worn.Length; slot++)
            {
                Entity gear = worn[slot].Item;

                if (!worn[slot].HasItem || !entityManager.HasBuffer<GearSocket>(gear))
                    continue;

                DynamicBuffer<GearSocket> sockets =
                    entityManager.GetBuffer<GearSocket>(gear, isReadOnly: true);

                for (int socket = 0; socket < sockets.Length; socket++)
                {
                    // What this hole casts, and what is linked to it. The same
                    // question the hotkey asks, answered the same way — an
                    // automatic socket and a bound one differ only in what
                    // decided to press the key.
                    if (!GemSockets.TryResolveActive(
                            entityManager, items, skills, gear, socket,
                            out int skillIndex, out int linkGroup))
                    {
                        continue;
                    }

                    FixedList512Bytes<SkillModifierBlob> supports =
                        GemSockets.GatherSupports(entityManager, items, gear, linkGroup);

                    if (!GemSockets.TryGetTrigger(supports, out SkillModifierBlob trigger))
                        continue;

                    // Only the passives. The first active in the group is the
                    // one the player is pressing — firing it here as well would
                    // be the same skill going off twice for reasons nobody can
                    // see, which is exactly what the old rule did to a weapon's
                    // welded attack.
                    if (!GemSockets.IsPassiveActive(entityManager, items, gear, socket))
                        continue;

                    if (IsOnCooldown(cooldowns, gear, socket))
                        continue;

                    if (!TryFire(
                            ref state, character, playerId, casterPosition, trigger, events,
                            out float3 position, out Entity target))
                    {
                        continue;
                    }

                    casts.Add(new PendingCast
                    {
                        SkillIndex = skillIndex,
                        PlayerId = playerId,
                        Origin = position,

                        // Nothing aimed this, so it faces the way everything
                        // unaimed in this pipeline faces. A bolt and a burst
                        // ignore it; a projectile needs something rather than a
                        // zero vector it would have to guess around.
                        Direction = new float3(0f, 0f, 1f),
                        DamageScale = 1f,
                        PreferredTarget = target,
                        Depth = depth,

                        // The socket that fired, so the fold gathers the same
                        // supports the key would have. This is the one cause
                        // that still knows which weapon it came out of.
                        Gear = gear,
                        SocketIndex = socket
                    });

                    cooldowns.Add(new TriggerCooldown
                    {
                        Gear = gear,
                        SocketIndex = socket,
                        Remaining = trigger.TriggerCooldown
                    });
                }
            }
        }

        private static bool IsOnCooldown(
            DynamicBuffer<TriggerCooldown> cooldowns, Entity gear, int socketIndex)
        {
            for (int i = 0; i < cooldowns.Length; i++)
            {
                if (cooldowns[i].Gear == gear && cooldowns[i].SocketIndex == socketIndex)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this trigger goes off, and where.
        ///
        /// A state condition is asked directly; an event condition walks the
        /// frame's announcements and rolls its chance against each one until one
        /// pays out. Rolling per opportunity rather than once per frame is what
        /// makes a chance mean what it says — but the loop stops on the first
        /// success, so the work is bounded by the queue rather than by the crowd.
        /// </summary>
        private bool TryFire(
            ref SystemState state,
            Entity character,
            int playerId,
            float3 casterPosition,
            in SkillModifierBlob trigger,
            in NativeArray<TriggerEvent> events,
            out float3 position,
            out Entity target)
        {
            position = casterPosition;
            target = Entity.Null;

            if (trigger.TriggerCondition == TriggerConditionType.OnLowHealth)
                return TryFireOnLowHealth(ref state, character, trigger);

            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Condition != trigger.TriggerCondition ||
                    events[i].PlayerId != playerId)
                {
                    continue;
                }

                if (_random.NextFloat() > trigger.ProcChance)
                    continue;

                position = events[i].Position;
                target = events[i].Target;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The one condition that is a state rather than an announcement.
        ///
        /// It reads a Health component off the character, and characters do not
        /// have one: players cannot be hurt yet. So this answers no today and
        /// will answer the truth the day health lands, without this file
        /// changing — which is the entire reason it is written as a component
        /// read rather than as a stub.
        ///
        /// It rolls its chance too. For a state that means a gem on a three
        /// second cooldown goes off every few of those while the wearer is hurt,
        /// rather than the instant they dip under the threshold.
        /// </summary>
        private bool TryFireOnLowHealth(
            ref SystemState state, Entity character, in SkillModifierBlob trigger)
        {
            if (!state.EntityManager.HasComponent<Health>(character))
                return false;

            Health health = state.EntityManager.GetComponentData<Health>(character);

            if (health.Max <= 0f || health.Current / health.Max > trigger.Threshold)
                return false;

            return _random.NextFloat() <= trigger.ProcChance;
        }

        /// <summary>
        /// Where a player is standing, or the origin when the registry has not
        /// heard of them.
        ///
        /// A scan of a buffer that holds one entry per player. The same shape as
        /// every other lookup by player id in this project, and for the same
        /// reason: four is not a number worth hashing.
        /// </summary>
        private static float3 PositionOf(
            in DynamicBuffer<PlayerPositionElement> players, int playerId)
        {
            if (!players.IsCreated)
                return float3.zero;

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId == playerId)
                    return players[i].Position;
            }

            return float3.zero;
        }
    }
}
