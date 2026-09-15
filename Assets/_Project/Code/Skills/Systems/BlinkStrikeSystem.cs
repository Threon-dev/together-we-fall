using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Steps a blinking character from body to body, and strikes each one with
    /// whatever the primary key casts. A dash is the same sequence with a single
    /// landing chosen at the press: it waits out the slide and strikes once.
    ///
    /// The strike is a PendingCast naming the primary key's gear and hole, so it
    /// goes through the same door a condition trigger does: the link group's
    /// supports are gathered, no cooldown and no mana are charged — the blink
    /// paid — and it resolves in SkillCastSystem this same frame, which is why
    /// this runs before it.
    ///
    /// Where the body goes is decided here and written as a PlayerWarp; the
    /// GameObject that owns the transform reads it and moves. The host decides,
    /// the bridge obeys — the same split as every other request and result.
    ///
    /// Main thread, because the landing is checked against walls with PhysX, and
    /// because it runs only on the frames somebody is mid-blink.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SkillCastSystem))]
    public partial struct BlinkStrikeSystem : ISystem
    {
        /// <summary>The key whose skill each blow is. Left mouse.</summary>
        private const int PrimarySlot = 0;

        /// <summary>How far past the body the step lands.</summary>
        private const float BehindDistance = 1.3f;   // TUNE

        /// <summary>
        /// How far off straight-behind the step lands, alternating sides. Not
        /// random: a blink that zigzags reads as intent, and the host needs no
        /// dice for it.
        /// </summary>
        private const float SideAngleDegrees = 35f;  // TUNE

        /// <summary>The shortest pause between steps, whatever the skill authored.</summary>
        private const float MinInterval = 0.08f;

        /// <summary>How far short of a wall the step stops, so the capsule is not put inside it.</summary>
        private const float WallMargin = 0.4f;

        private EntityQuery _blinkingQuery;
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _blinkingQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, BlinkSequence, PlayerWarp>()
                .Build();

            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Respects the enabled flag, so a floor where nobody is blinking
            // costs one check.
            if (_blinkingQuery.IsEmpty)
                return;

            float deltaTime = SystemAPI.Time.DeltaTime;
            SkillDatabase skills = SystemAPI.GetSingleton<SkillDatabase>();
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            using NativeArray<Entity> characters = _blinkingQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<Entity> enemies = _enemyQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var targets = new EnemyTargets { Entities = enemies, Transforms = transforms };
            using var strikes = new NativeList<PendingCast>(characters.Length, Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                Entity character = characters[i];
                BlinkSequence sequence = state.EntityManager.GetComponentData<BlinkSequence>(character);

                sequence.Timer -= deltaTime;

                if (sequence.Timer > 0f)
                {
                    state.EntityManager.SetComponentData(character, sequence);
                    continue;
                }

                // A dash has arrived: one blow from where it stopped, then let go.
                if (sequence.StrikeOnArrival)
                {
                    QueuePrimary(
                        ref state, character, sequence.PlayerId, sequence.Interval,
                        sequence.Next, sequence.Position, sequence.Facing, skills, items, strikes);

                    Finish(ref state, character, sequence);
                    continue;
                }

                // Out of steps, or out of bodies. Either way the last blow has
                // had its interval to land, so the character is let go.
                if (sequence.JumpsRemaining <= 0 ||
                    !TryStep(ref state, character, ref sequence, targets, skills, items, strikes))
                {
                    Finish(ref state, character, sequence);
                    continue;
                }

                sequence.JumpsRemaining--;
                sequence.Timer = math.max(MinInterval, sequence.Interval);
                state.EntityManager.SetComponentData(character, sequence);
            }

            if (strikes.Length == 0)
                return;

            DynamicBuffer<PendingCast> queue = SystemAPI.GetSingletonBuffer<PendingCast>();

            for (int i = 0; i < strikes.Length; i++)
                queue.Add(strikes[i]);
        }

        private static bool TryStep(
            ref SystemState state,
            Entity character,
            ref BlinkSequence sequence,
            in EnemyTargets targets,
            SkillDatabase skills,
            ItemDatabase items,
            NativeList<PendingCast> strikes)
        {
            // The body the cast chose, if it is still standing; otherwise the
            // nearest one not struck yet, from where the last step landed.
            int index = sequence.Next != Entity.Null ? targets.IndexOf(sequence.Next) : -1;
            sequence.Next = Entity.Null;

            if (index < 0)
                index = targets.FindNearestUnvisited(sequence.Position, sequence.Reach, sequence.Visited);

            if (index < 0)
                return false;

            Entity target = targets.Entities[index];
            float3 body = targets.PositionOf(index);

            // At the character's own height: a body's origin is on the floor,
            // and the capsule's is a metre up.
            float3 from = new float3(body.x, sequence.Position.y, body.z);

            float3 approach = from - sequence.Position;
            approach.y = 0f;
            approach = math.lengthsq(approach) > 1e-4f
                ? math.normalize(approach)
                : new float3(0f, 0f, 1f);

            float side = math.radians(SideAngleDegrees) * ((sequence.JumpsMade & 1) == 0 ? 1f : -1f);
            float3 behind = SkillCastSystem.Turn(approach, side);

            float distance = BehindDistance;

            if (WallQuery.Cast(from, from + behind * BehindDistance, WallQuery.Mask(), out float3 stop))
                distance = math.max(0f, math.distance(from, stop) - WallMargin);

            float3 landing = from + behind * distance;
            float3 facing = -behind;

            if (sequence.Visited.Length < sequence.Visited.Capacity)
                sequence.Visited.Add(target);

            sequence.Position = landing;
            sequence.JumpsMade++;

            PlayerWarp warp = state.EntityManager.GetComponentData<PlayerWarp>(character);
            warp.Version++;
            warp.Position = landing;
            warp.Facing = facing;
            warp.Holding = true;
            warp.SlideSeconds = 0f;
            state.EntityManager.SetComponentData(character, warp);

            QueuePrimary(
                ref state, character, sequence.PlayerId, sequence.Interval,
                target, landing, facing, skills, items, strikes);

            return true;
        }

        /// <summary>
        /// The primary key's skill, cast from the landing at the body. Nothing
        /// when that key is empty, unworn or is itself a blink — the step still
        /// happens, it simply lands no blow.
        /// </summary>
        private static void QueuePrimary(
            ref SystemState state,
            Entity character,
            int playerId,
            float interval,
            Entity target,
            float3 landing,
            float3 facing,
            SkillDatabase skills,
            ItemDatabase items,
            NativeList<PendingCast> strikes)
        {
            DynamicBuffer<SkillSlot> bar = state.EntityManager.GetBuffer<SkillSlot>(character, isReadOnly: true);

            if (bar.Length <= PrimarySlot || !bar[PrimarySlot].HasBinding)
                return;

            SkillSlot primary = bar[PrimarySlot];

            if (!state.EntityManager.HasBuffer<EquippedItem>(character) ||
                !GemSockets.IsWorn(
                    state.EntityManager.GetBuffer<EquippedItem>(character, isReadOnly: true), primary.Gear))
            {
                return;
            }

            if (!GemSockets.TryResolveActive(
                    state.EntityManager, items, skills, primary.Gear, primary.SocketIndex,
                    out int skillIndex, out _))
            {
                return;
            }

            SkillEffectKind effect = skills.EffectOf(skillIndex);

            if (effect == SkillEffectKind.BlinkStrike || effect == SkillEffectKind.DashStrike)
                return;

            strikes.Add(new PendingCast
            {
                SkillIndex = skillIndex,
                PlayerId = playerId,
                Origin = landing,
                Direction = facing,
                DamageScale = 1f,
                PreferredTarget = target,

                // Zero: the player pressed for this. What it triggers counts
                // from here, and the damage meter files it as a press.
                Depth = 0,
                Gear = primary.Gear,
                SocketIndex = primary.SocketIndex
            });

            // The arm swings on every blow, paced to the gap between steps.
            if (!state.EntityManager.HasComponent<CastCue>(character))
                return;

            CastCue cue = state.EntityManager.GetComponentData<CastCue>(character);
            cue.Count++;
            cue.Effect = effect;
            cue.Interval = math.max(MinInterval, interval);
            cue.StrikeDelay = 0f;

            if (effect == SkillEffectKind.MeleeArc)
                cue.Swings++;

            state.EntityManager.SetComponentData(character, cue);
        }

        private static void Finish(ref SystemState state, Entity character, in BlinkSequence sequence)
        {
            state.EntityManager.SetComponentEnabled<BlinkSequence>(character, false);

            if (state.EntityManager.HasComponent<Invulnerable>(character))
                state.EntityManager.SetComponentEnabled<Invulnerable>(character, false);

            // The cooldown starts now.
            DynamicBuffer<SkillSlot> slots = state.EntityManager.GetBuffer<SkillSlot>(character);

            if (sequence.SlotIndex >= 0 && sequence.SlotIndex < slots.Length)
            {
                SkillSlot slot = slots[sequence.SlotIndex];
                slot.Held = false;
                slots[sequence.SlotIndex] = slot;
            }

            PlayerWarp warp = state.EntityManager.GetComponentData<PlayerWarp>(character);
            warp.Holding = false;
            state.EntityManager.SetComponentData(character, warp);
        }
    }
}
