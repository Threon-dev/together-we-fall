using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Shared;
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
        // The multicast spread used to be a constant here. It is a folded
        // number now — SkillDatabase.DefaultSpreadDegrees is where it starts and
        // IncreasedSpread is what moves it — because a gem wanted to change it,
        // and a constant the fold cannot see is one the tooltip cannot explain.

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

        /// <summary>
        /// How far into its cooldown a melee arc lands. A share rather than
        /// seconds, so attack speed shortens the wind-up with the swing.
        /// </summary>
        private const float SwingStrikeShare = 0.3f;

        /// <summary>
        /// The longest wind-up, however slow the swing. Past this the blow
        /// stops reading as a response to the key.
        /// </summary>
        private const float MaxStrikeDelay = 0.3f;

        /// <summary>
        /// The fewest jumps a burst turned into a chain is worth.
        ///
        /// An area skill authored with no chains would otherwise become a
        /// single-target bolt under the keystone, which is not a trade-off, it
        /// is a punishment. Four is roughly what a blast catches in a crowd, so
        /// the keystone changes the SHAPE of the damage — a line through bodies
        /// instead of a circle on the ground — rather than the amount of it.
        /// </summary>
        private const int KeystoneChainCount = 4;

        private EntityQuery _enemyQuery;
        private EntityQuery _characterQuery;
        private EntityQuery _freeProjectileQuery;
        private EntityQuery _freeZoneQuery;

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

            // The same idea for zones: an unlit one is one whose flag is down.
            _freeZoneQuery = SystemAPI.QueryBuilder()
                .WithAll<ElementZone>()
                .WithDisabled<ZoneActive>()
                .Build();

            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<SkillTriggerSettings>();
            state.RequireForUpdate<SkillPrefabs>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            TickCooldowns(ref state, deltaTime);

            // Before the early-out: a swing lands on its own frame, whether or
            // not anybody pressed anything on it.
            using var landing = new NativeList<DelayedStrike>(0, Allocator.Temp);
            TickStrikes(ref state, deltaTime, landing);

            DynamicBuffer<SkillCastRequest> requests =
                SystemAPI.GetSingletonBuffer<SkillCastRequest>();
            DynamicBuffer<PendingCast> triggered = SystemAPI.GetSingletonBuffer<PendingCast>();

            if (requests.Length == 0 && triggered.Length == 0 && landing.Length == 0)
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
            using var zones = new NativeList<ZoneSpawn>(2, Allocator.Temp);
            using var effects = new NativeList<VfxEvent>(4, Allocator.Temp);

            // Swings pressed earlier first, so a press this frame that queues a
            // new one never overtakes the one already on its way.
            for (int i = 0; i < landing.Length; i++)
                Land(ref state, landing[i], targets, hits, areas, spawns, zones, effects);

            for (int i = 0; i < pending.Length; i++)
                Cast(ref state, pending[i], skills, targets, hits, areas, spawns, zones, effects);

            for (int i = 0; i < pendingTriggers.Length; i++)
            {
                CastTriggered(
                    ref state, pendingTriggers[i], skills, maxTriggerDepth,
                    targets, hits, areas, spawns, zones, effects);
            }

            // Buffer writes before structural ones: instantiating below would
            // invalidate every buffer taken above.
            AppendEvents(ref state, hits, areas, effects);
            SpawnProjectiles(ref state, spawns);
            SpawnZones(ref state, zones);
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

                    // Nothing to refill, or a blink still stepping: its cooldown
                    // starts when the last blow lands, not when the key went down.
                    if (slot.ChargesSpent <= 0 || slot.Held)
                        continue;

                    slot.CooldownRemaining -= deltaTime;

                    // One charge back, and the next one starts refilling at once.
                    if (slot.CooldownRemaining <= 0f)
                    {
                        slot.ChargesSpent--;
                        slot.CooldownRemaining = slot.ChargesSpent > 0 ? slot.Recharge : 0f;
                    }

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
            NativeList<ZoneSpawn> zones,
            NativeList<VfxEvent> effects)
        {
            if (!TryGetCharacter(ref state, request.PlayerId, out Entity character))
                return;

            // Silenced, or stunned, which includes silence. Checked before the
            // cooldown is read and long before one is charged: a key pressed
            // into a silence should cost nothing, so that it works the instant
            // the silence ends.
            //
            // Quietly, like the trigger-gem refusal beside it. A message every
            // frame the button is held is worse than nothing happening.
            if (IsSilenced(ref state, character))
                return;

            // Mid-blink, every key waits. The body is being moved by the host,
            // and a press from where the client thought it stood would go off
            // somewhere the character no longer is.
            if (IsBlinking(ref state, character))
                return;

            DynamicBuffer<SkillSlot> slots = state.EntityManager.GetBuffer<SkillSlot>(character);
            if (request.SlotIndex < 0 || request.SlotIndex >= slots.Length)
                return;

            SkillSlot slot = slots[request.SlotIndex];

            // The cooldown is the host's answer, not the client's. A client that
            // spams the button gets exactly as many casts as it is owed.
            //
            // Against the charges the last cast folded. The fold below asks again
            // and has the final word; this only spares a held button on an empty
            // key from gathering supports every frame.
            if (!slot.HasBinding || slot.ChargesSpent >= math.max(1, slot.Charges))
                return;

            // What this key casts is whatever is in the socket right now. An
            // empty socket, an unequipped weapon or a support gem where an
            // active should be all come out the same way: nothing happens.
            //
            // The unequipped case is checked here rather than merely intended.
            // It was neither before, and it mattered little while every skill
            // came from a gem the player had chosen to bind; it matters now that
            // a weapon carries its own attack, because taking the sword off and
            // still swinging it is precisely the thing "equipment matters"
            // cannot mean.
            if (!IsWorn(ref state, character, slot.Gear))
                return;

            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            if (!GemSockets.TryResolveActive(
                    state.EntityManager, items, skills, slot.Gear, slot.SocketIndex,
                    out int skillIndex, out int linkGroup))
            {
                return;
            }

            // The build, gathered fresh. The same gem beside different supports
            // is a different skill, and this is the line where that happens.
            //
            // The character goes in so the set bonuses that carry a support are
            // gathered with them: those act on every skill rather than on one
            // link group, and this is the line where "every skill" is decided.
            FixedList512Bytes<SkillModifierBlob> supports =
                GemSockets.GatherSupports(
                    state.EntityManager, items, slot.Gear, linkGroup, character);

            // A passive refuses the key, and only a passive: the first active in
            // a group keeps answering even with a trigger gem beside it, which
            // is what lets a weapon go on being swung by the hand that owns it
            // while the gem linked to it fires on its own.
            //
            // Enforced here rather than in the bind, which happened before the
            // gem arrived and cannot be re-run when it does. Silently, and on
            // purpose: the alternative is a refusal message every frame the
            // button is held.
            if (GemSockets.IsPassiveActive(
                    state.EntityManager, items, slot.Gear, slot.SocketIndex))
            {
                return;
            }

            StatBlock stats = state.EntityManager.GetComponentData<PlayerStats>(character).Final;

            // Only asked for when something asked. A build with no conditional
            // supports — which is every build that existed before this — pays
            // one comparison and never touches the world.
            CastConditions conditions =
                SkillConditions.AnyConditional(supports) || skills.AnyConditional(skillIndex)
                    ? ReadConditions(
                        ref state, targets, request.Origin, Flatten(request.Direction),
                        skills.RangeOf(skillIndex), Entity.Null)
                    : CastConditions.Unknown(default);

            ResolvedSkill resolved = skills.Resolve(skillIndex, stats, supports, conditions);
            ScaleOutgoing(ref state, character, ref resolved);

            KeystoneEffect keystone = KeystoneOf(ref state, character);

            // The one keystone that changes the cast's own numbers rather than
            // what the cast produces. There is no mana to save yet, so today it
            // is the downside alone — see KeystoneEffect for why it is here
            // anyway.
            if (keystone == KeystoneEffect.NoManaCostDoubleCooldown)
            {
                resolved.Cooldown *= 2f;

                // The upside, finally. The keystone was authored as the shape of
                // a trade it could not make — there was nothing to not pay — and
                // this is the one line its comment promised.
                resolved.ManaCost = 0f;
            }

            // Checked here and spent at the bottom, for the same reason the
            // cooldown is: a cast that finds nothing to do must cost nothing.
            // Refusing quietly, like the silence and the trigger-gem refusals
            // above — a message every frame the button is held is worse than
            // the empty bar the player is already looking at.
            if (!HasMana(ref state, character, resolved.ManaCost))
                return;

            // A charge gem pulled out since the last press: the cache said yes,
            // the fold says no. Written back so the bar stops advertising it.
            if (slot.ChargesSpent >= resolved.Charges)
            {
                slot.Charges = resolved.Charges;
                slots[request.SlotIndex] = slot;
                return;
            }

            // TEMPORARY DIAGNOSTIC — delete once the gem chain is trusted.
            //
            // Every link in that chain looks correct read on its own, so the
            // only thing left is to watch it run. This says what the host
            // decided, in the order it decided it: which hole, which group, how
            // many supports that group handed over, and what came out.
            UnityEngine.Debug.Log(
                $"[cast] socket {slot.SocketIndex} group {linkGroup} " +
                $"skill {skills.NameOf(skillIndex)} " +
                $"supports {supports.Length} -> forks {resolved.Forks} " +
                $"casts {resolved.Casts} chains {resolved.Chains}");

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
                Depth = 0,

                // Carried rather than read again inside Emit, because Emit is
                // static and because a keystone is a fact about this cast: it
                // belongs beside who cast it and where they were pointing.
                Keystone = keystone
            };

            // Which way this swing sweeps — every other one comes back. Decided
            // here, with the blow, so the arm and the slash read it from the
            // same place instead of each keeping a count that could drift.
            // Written back only if the cast goes off, at the bottom.
            bool hasCue = state.EntityManager.HasComponent<CastCue>(character);
            CastCue cue = hasCue ? state.EntityManager.GetComponentData<CastCue>(character) : default;

            if (resolved.Effect == SkillEffectKind.MeleeArc)
            {
                cue.Swings++;
                context.SweepRight = CastCue.SweepsRight(cue.Swings);
            }

            float3 direction = Flatten(request.Direction);
            float strikeDelay = StrikeDelayOf(resolved);

            bool produced;

            if (resolved.Effect == SkillEffectKind.BlinkStrike)
            {
                // Paid for now, like a swing, and refilled only once the last
                // step has landed — BlinkStrikeSystem lets go of the slot then.
                produced = BeginBlink(ref state, character, request, resolved, targets);
                slot.Held = produced;
            }
            else if (SkillModifiers.IsSupportive(resolved.Effect))
            {
                produced = EmitSupport(ref state, resolved, context, direction, hits, effects);
            }
            else if (strikeDelay > 0f && state.EntityManager.HasBuffer<DelayedStrike>(character))
            {
                // The swing has started; the blow lands when the blade comes
                // round. Paid for now: a melee arc goes off wherever it is
                // pointed, so there is no "found nothing" to wait for — and a
                // cooldown that began at the landing would let a held button
                // queue a second swing inside the first.
                state.EntityManager.GetBuffer<DelayedStrike>(character).Add(new DelayedStrike
                {
                    Skill = resolved,
                    PlayerId = context.PlayerId,
                    Origin = context.Origin,
                    AimPoint = context.AimPoint,
                    Direction = direction,
                    Keystone = keystone,
                    SweepRight = context.SweepRight,
                    Remaining = strikeDelay
                });

                produced = true;
            }
            else
            {
                produced = EmitCasts(resolved, context, direction, targets, hits, areas, spawns, zones, effects);
            }

            // A cast that found nothing to do costs nothing. Only a bolt can
            // fail this way — everything else goes off wherever it was pointed —
            // and charging a cooldown for a bolt that had no target reads as the
            // skill being broken rather than as having missed.
            if (!produced)
                return;

            // A charge, not the whole key. The timer starts only if nothing was
            // already refilling; otherwise the one running keeps its place.
            if (slot.ChargesSpent == 0)
                slot.CooldownRemaining = resolved.Cooldown;

            slot.ChargesSpent++;
            slot.Charges = resolved.Charges;
            slot.Recharge = resolved.Cooldown;
            slots[request.SlotIndex] = slot;

            SpendMana(ref state, character, resolved.ManaCost);

            // Not for a blink: the arm swings on each blow it lands, and
            // BlinkStrikeSystem counts those.
            if (hasCue && resolved.Effect != SkillEffectKind.BlinkStrike)
            {
                cue.Count++;
                cue.Effect = resolved.Effect;
                cue.Interval = resolved.Cooldown;
                cue.StrikeDelay = strikeDelay;
                state.EntityManager.SetComponentData(character, cue);
            }
        }

        /// <summary>
        /// Multicast is one cooldown and several effects, not several casts.
        /// Returns whether any of them came to something.
        /// </summary>
        private static bool EmitCasts(
            in ResolvedSkill resolved,
            in CastContext context,
            float3 direction,
            EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<ProjectileSpawn> spawns,
            NativeList<ZoneSpawn> zones,
            NativeList<VfxEvent> effects)
        {
            bool produced = false;

            for (int c = 0; c < resolved.Casts; c++)
            {
                // Both the facing and the aim swing, so a fan of bursts lands as
                // a fan rather than as one burst three times over.
                float angle = SpreadAngle(c, resolved.Casts, resolved.SpreadDegrees);

                CastContext copy = context;
                copy.AimPoint = SpreadAim(context.Origin, context.AimPoint, angle);

                produced |= Emit(
                    resolved, copy, Turn(direction, angle),
                    targets, hits, areas, spawns, zones, effects);
            }

            return produced;
        }

        /// <summary>
        /// The wind-up a skill waits before it lands. Only a melee arc has one:
        /// a bolt leaves the hand at the press, and a spell's own cast clip is
        /// short enough that the flash covers it.
        /// </summary>
        private static float StrikeDelayOf(in ResolvedSkill skill)
            => skill.Effect == SkillEffectKind.MeleeArc
                ? math.min(skill.Cooldown * SwingStrikeShare, MaxStrikeDelay)
                : 0f;

        /// <summary>
        /// Where a projectile appears: a little in front of the caster, or just
        /// short of a wall in between. Fired with its nose already through the
        /// wall, a bolt would start past the one thing meant to stop it — a
        /// ray from inside a collider finds nothing.
        /// </summary>
        private static float3 Muzzle(float3 origin, float3 direction)
        {
            float3 muzzle = origin + direction * MuzzleOffset;

            return WallQuery.Cast(origin, muzzle, WallQuery.Mask(), out float3 stop) ? stop : muzzle;
        }

        /// <summary>Counts every waiting swing down and hands over the ones that are due.</summary>
        private void TickStrikes(ref SystemState state, float deltaTime, NativeList<DelayedStrike> landing)
        {
            foreach (DynamicBuffer<DelayedStrike> iterated in
                     SystemAPI.Query<DynamicBuffer<DelayedStrike>>().WithAll<PlayerCharacter>())
            {
                // Copied into a local because a foreach variable is readonly,
                // and writing through its indexer counts as modifying it.
                DynamicBuffer<DelayedStrike> strikes = iterated;

                for (int i = strikes.Length - 1; i >= 0; i--)
                {
                    DelayedStrike strike = strikes[i];
                    strike.Remaining -= deltaTime;

                    if (strike.Remaining > 0f)
                    {
                        strikes[i] = strike;
                        continue;
                    }

                    landing.Add(strike);
                    strikes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A swing coming round. From where the swinger stands now rather than
        /// where they pressed: walking through a swing carries the blade along,
        /// and a blow left behind at the old spot would hit what they walked
        /// away from.
        /// </summary>
        private void Land(
            ref SystemState state,
            in DelayedStrike strike,
            EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<PendingArea> areas,
            NativeList<ProjectileSpawn> spawns,
            NativeList<ZoneSpawn> zones,
            NativeList<VfxEvent> effects)
        {
            float3 moved = CurrentPosition(ref state, strike.PlayerId, strike.Origin) - strike.Origin;

            var context = new CastContext
            {
                PlayerId = strike.PlayerId,
                Origin = strike.Origin + moved,
                AimPoint = strike.AimPoint + moved,
                PreferredTarget = Entity.Null,
                Depth = 0,
                Keystone = strike.Keystone,
                SweepRight = strike.SweepRight
            };

            EmitCasts(strike.Skill, context, strike.Direction, targets, hits, areas, spawns, zones, effects);
        }

        private float3 CurrentPosition(ref SystemState state, int playerId, float3 fallback)
        {
            if (!SystemAPI.TryGetSingletonBuffer<PlayerPositionElement>(
                    out DynamicBuffer<PlayerPositionElement> players, isReadOnly: true))
                return fallback;

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId == playerId)
                    return players[i].Position;
            }

            return fallback;
        }

        // The crit roll used to live here, once per press, and it was in the
        // wrong place: a cast is not a blow. One projectile out of three forks
        // crits and the other two do not, a chain crits on the second jump and
        // not the first, a burst crits on the body it caught and not its
        // neighbour. All of that is a fact about a blow landing, and the stage
        // that knows a blow is landing is SkillHitSystem — which is also where
        // OnCrit is announced now, with the body it actually happened to.
        //
        // What travels from here is the chance and the multiplier, carried the
        // same way the explosion and the cull already are.

        /// <summary>
        /// Whether the caster can pay, treating a character with no mana
        /// component as able to pay anything.
        ///
        /// That default is the same shape as the StatusGate one beside it: a
        /// character that never got the component is not a character who is
        /// permanently out of mana, it is a character mana does not apply to.
        /// The alternative fails closed, and the symptom would be every button
        /// in a half-built scene silently doing nothing.
        /// </summary>
        private bool HasMana(ref SystemState state, Entity character, float cost)
        {
            if (cost <= 0f)
                return true;

            return !state.EntityManager.HasComponent<Mana>(character) ||
                   state.EntityManager.GetComponentData<Mana>(character).Current >= cost;
        }

        private void SpendMana(ref SystemState state, Entity character, float cost)
        {
            if (cost <= 0f || !state.EntityManager.HasComponent<Mana>(character))
                return;

            Mana mana = state.EntityManager.GetComponentData<Mana>(character);
            mana.Current = math.max(0f, mana.Current - cost);

            state.EntityManager.SetComponentData(character, mana);
        }

        /// <summary>
        /// Casts a skill that something other than a player asked for.
        ///
        /// Three things it deliberately does not do. It charges no cooldown: a
        /// trigger is a consequence, and a consequence that could be rate-limited
        /// by the slot it was never in makes no sense. It charges no mana, for
        /// the same reason and with the same force — a player who paid for the
        /// shot has paid for what the shot causes, and billing them again for a
        /// chain of triggers would make an expensive build cost more the better
        /// it worked. What keeps THAT from being free damage is the depth budget
        /// below and the trigger gem's own cooldown, which is where the limit on
        /// automatic casting has always lived. And it does not check whether the
        /// caster still has that skill equipped — the moment that decided this
        /// was going to happen has already passed.
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
            NativeList<ZoneSpawn> zones,
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

            // Supports when the cause knew where it came from, and innate ones
            // when it did not.
            //
            // A condition trigger knows: it fired because of what is in a
            // particular hole, so it names the gear and the hole and the fold
            // gathers exactly the group the hotkey would have. A trigger on
            // impact does not and deliberately never will — a projectile that
            // remembered its weapon is a weapon that cannot be swapped while it
            // flies.
            //
            var supports = new FixedList512Bytes<SkillModifierBlob>();

            if (cast.Gear != Entity.Null &&
                GemSockets.TryReadSocket(
                    state.EntityManager, cast.Gear, cast.SocketIndex, out GearSocket socket))
            {
                supports = GemSockets.GatherSupports(
                    state.EntityManager, SystemAPI.GetSingleton<ItemDatabase>(),
                    cast.Gear, socket.LinkGroup, character);
            }

            // What this cast is actually aimed at, worked out before anything
            // asks a question about it.
            //
            // The cause named a body, and it may well be gone: a crit names
            // nobody at all, and a kill names a corpse. Either way the cast has
            // to go somewhere — a projectile fired down the default facing at a
            // dead man is the difference between a trigger gem that feels like
            // a build and one that looks broken.
            //
            // So: the body that caused it if it is still standing, otherwise
            // the nearest one within the skill's own reach, otherwise nothing
            // and the cast goes off where it was caused.
            Entity aimed = cast.PreferredTarget;
            float3 aimPoint = cast.Origin;
            float3 direction = Flatten(cast.Direction);

            int aimedIndex = aimed != Entity.Null ? targets.IndexOf(aimed) : -1;

            if (aimedIndex < 0)
            {
                aimedIndex = targets.FindNearest(cast.Origin, skills.RangeOf(cast.SkillIndex));
                aimed = aimedIndex >= 0 ? targets.Entities[aimedIndex] : Entity.Null;
            }

            if (aimedIndex >= 0)
            {
                aimPoint = targets.PositionOf(aimedIndex);

                // Only when there is a line to speak of. A burst that goes off
                // on top of its target has no direction, and normalising a zero
                // vector would point it at the world's z axis.
                float3 toTarget = aimPoint - cast.Origin;

                if (math.lengthsq(toTarget) > 1e-3f)
                    direction = Flatten(toTarget);
            }

            // Conditions, asked of the body that caused this.
            //
            // They used to be refused outright here, on the grounds that asking
            // "is the target burning" of a body a blast already killed gives a
            // misleading answer. The price was worse than the problem: every
            // conditional gem in a group holding a trigger gem was dead weight,
            // silently, and "dead weight, silently" is the one thing a socketed
            // gem must never be. A cause that named a body is asked about that
            // body; a cause whose body has since died falls back to the search,
            // and a search that finds nothing answers no — which is the same
            // honest nothing a key press gets when it is aimed at empty floor.
            CastConditions conditions =
                SkillConditions.AnyConditional(supports) || skills.AnyConditional(cast.SkillIndex)
                    ? ReadConditions(
                        ref state, targets, cast.Origin, direction,
                        skills.RangeOf(cast.SkillIndex), aimed)
                    : CastConditions.Unknown(default);

            ResolvedSkill resolved = skills.Resolve(
                cast.SkillIndex, stats, supports, conditions);

            resolved.Damage *= cast.DamageScale;
            resolved.ExplosionDamage *= cast.DamageScale;
            ScaleOutgoing(ref state, character, ref resolved);

            KeystoneEffect keystone = character != Entity.Null
                ? KeystoneOf(ref state, character)
                : KeystoneEffect.None;

            var context = new CastContext
            {
                PlayerId = cast.PlayerId,
                Origin = cast.Origin,
                AimPoint = aimPoint,
                PreferredTarget = aimed,
                Depth = cast.Depth,
                Keystone = keystone
            };

            // A triggered team spell goes to allies, once, whatever the fold
            // said about copies.
            if (SkillModifiers.IsSupportive(resolved.Effect))
            {
                EmitSupport(ref state, resolved, context, Flatten(cast.Direction), hits, effects);
                return;
            }

            for (int c = 0; c < resolved.Casts; c++)
            {
                float angle = SpreadAngle(c, resolved.Casts, resolved.SpreadDegrees);

                CastContext copy = context;
                copy.AimPoint = SpreadAim(context.Origin, context.AimPoint, angle);

                Emit(
                    resolved, copy, Turn(direction, angle),
                    targets, hits, areas, spawns, zones, effects);
            }
        }

        /// <summary>
        /// Where a cast visually starts.
        ///
        /// The muzzle for something that flies, the ground for something that
        /// is left behind, and the aimed point for a blast — which is the one
        /// case where the effect is nowhere near the hand that cast it. Each
        /// branch below computes this for its own purposes anyway; this is the
        /// same arithmetic in one place, so the flash cannot end up somewhere
        /// the skill did not.
        /// </summary>
        private static float3 CastVfxPoint(
            in ResolvedSkill skill, in CastContext context, float3 direction)
        {
            switch (skill.Effect)
            {
                case SkillEffectKind.Projectile:
                    return context.Origin + direction * MuzzleOffset;

                case SkillEffectKind.AreaBurst:
                    return ClampToRange(context.Origin, context.AimPoint, skill.Range);

                case SkillEffectKind.PersistentZone:
                    return OnGround(
                        ClampToRange(context.Origin, context.AimPoint, skill.Range),
                        context.AimPoint);

                // A swing is drawn at a body, like its hits are, and a body's
                // origin is on the floor — the set's lift puts both at the chest.
                // The caster's origin is the middle of their capsule, a metre
                // up, and lifting that by the same amount put the slash over
                // their head.
                case SkillEffectKind.MeleeArc:
                    return OnGround(context.Origin, context.AimPoint);

                default:
                    return context.Origin;
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
            NativeList<ZoneSpawn> zones,
            NativeList<VfxEvent> effects)
        {
            // Announced before anything is produced, once per cast, at the
            // point the effect visually starts. Before, because every branch
            // below may refuse — and a cast that produced nothing still went
            // off as far as the player's hand is concerned: the flash is the
            // feedback that the key was heard.
            //
            // Skipped entirely by a skill with no visual set, which is every
            // skill until somebody authors one.
            if (skill.VfxId != 0)
            {
                float3 point = CastVfxPoint(skill, context, direction);

                effects.Add(new VfxEvent
                {
                    Kind = VfxEventKind.SkillCast,
                    Position = point,

                    // The far end carries the facing, which is the one thing a
                    // muzzle flash and a directional slash both need. Reusing
                    // the chain link's field rather than adding a rotation to
                    // every event in the queue for the sake of two kinds.
                    EndPosition = point + direction,
                    SweepRight = context.SweepRight,
                    Color = DamageTypePalette.For(skill.Type),
                    Magnitude = skill.Radius,
                    VfxId = skill.VfxId
                });
            }

            switch (skill.Effect)
            {
                case SkillEffectKind.Projectile:
                    spawns.Add(new ProjectileSpawn
                    {
                        Position = Muzzle(context.Origin, direction),
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
                            PiercesRemaining = skill.Pierces,
                            ChainsRemaining = skill.Chains,
                            ChainRange = skill.ChainRange,
                            ChainDelay = skill.ChainDelay,
                            ExplosionRadius = skill.ExplosionRadius,
                            ExplosionDamage = skill.ExplosionDamage,
                            CullThreshold = skill.CullThreshold,
                            ManaOnKill = skill.ManaOnKill,

                            // Not spent here. Every body this ends up striking
                            // rolls its own, which is what makes a fork that
                            // crits on one side and not the other possible.
                            CritChance = skill.CritChance,
                            CritMultiplier = skill.CritMultiplier,

                            // Carried on the projectile, like everything else it
                            // needs to resolve its own impact.
                            TriggerSkillIndex = skill.TriggerSkillIndex,
                            TriggerDamageScale = skill.TriggerDamageScale,
                            TriggerDepth = context.Depth,

                            // And the status it marks whatever it hits with, if
                            // the skill names one. A fork inherits it for free,
                            // because a fork copies this struct.
                            AppliedStatus = skill.AppliedStatus,

                            // The look it flies with, and the look of the blow it
                            // becomes. Inherited by a fork like everything else
                            // in this struct.
                            VfxId = skill.VfxId
                        }
                    });
                    return true;

                case SkillEffectKind.MeleeArc:
                    areas.Add(MakeArea(skill, context, context.Origin, direction));
                    return true;

                case SkillEffectKind.AreaBurst:
                    // The keystone, answered here rather than in the area stage.
                    // That queue also carries corpse explosions, zone pulses and
                    // reaction blasts, and turning those into chains would mean
                    // a body detonating into lightning because of a ring. This
                    // is the last place that still knows the blast is a skill
                    // somebody cast.
                    if (context.Keystone == KeystoneEffect.AoeToChain)
                        return EmitChainedBurst(skill, context, direction, targets, hits, effects);

                    areas.Add(MakeArea(
                        skill,
                        context,
                        ClampToRange(context.Origin, context.AimPoint, skill.Range),
                        direction));
                    return true;

                case SkillEffectKind.ChainBolt:
                    return EmitBolt(skill, context, direction, targets, hits, effects);

                case SkillEffectKind.PersistentZone:
                    zones.Add(new ZoneSpawn
                    {
                        // On the ground rather than at the caster's height, which
                        // is what every other effect uses. A blast is invisible
                        // and a metre up costs nothing; a zone is a disc somebody
                        // has to look at, and a floating one reads as a bug.
                        Position = OnGround(
                            ClampToRange(context.Origin, context.AimPoint, skill.Range),
                            context.AimPoint),
                        Zone = new ElementZone
                        {
                            Element = skill.Type,
                            Radius = math.max(1f, skill.Radius),
                            RemainingDuration = math.max(0.5f, skill.ZoneDuration),
                            TickInterval = skill.ZoneTickInterval,

                            // The first pulse is a beat after it is lit, not on
                            // the frame it appears: a zone that hits the moment
                            // it is cast is a burst wearing a zone as a costume.
                            TickRemaining = skill.ZoneTickInterval,
                            Damage = skill.Damage,
                            SourcePlayerId = context.PlayerId,

                            // The rest of the fold, which a zone used to drop
                            // on the floor. A status gem, a kill gem or a
                            // trigger gem in the group is now spent on a zone
                            // skill instead of sitting in the socket doing
                            // nothing — the one thing a gem must never do.
                            //
                            // Chains are the deliberate omission. A pulse is an
                            // effect that repeats twelve times, and a chain per
                            // pulse would make the jumps a function of how long
                            // the fire burns rather than of what was socketed.
                            AppliedStatus = skill.AppliedStatus,
                            ExplosionRadius = skill.ExplosionRadius,
                            ExplosionDamage = skill.ExplosionDamage,
                            CullThreshold = skill.CullThreshold,
                            ManaOnKill = skill.ManaOnKill,

                            // Every pulse rolls for every body it catches, so a
                            // zone that burns for six seconds is a crit build's
                            // best friend rather than one lucky number.
                            CritChance = skill.CritChance,
                            CritMultiplier = skill.CritMultiplier,

                            // Fires on the first pulse and then never again.
                            TriggerSkillIndex = skill.TriggerSkillIndex,
                            TriggerDamageScale = skill.TriggerDamageScale,
                            TriggerDepth = context.Depth
                        }
                    });

                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// A blast under the AoeToChain keystone: the same skill, spent as jumps
        /// between bodies instead of as a circle on the ground.
        ///
        /// The blast radius becomes the jump range, which is the whole of what
        /// makes it read as the same skill inverted rather than as a different
        /// one: a wide burst chains far, a tight one rattles between neighbours.
        /// The jumps themselves are floored, because an area skill authored with
        /// no chains would otherwise become single-target — a keystone should
        /// change what damage looks like, not delete it.
        ///
        /// It goes through the ordinary bolt path, so it inherits the acquire
        /// cone, the visited list and the chain delay without any of that
        /// knowing a keystone exists.
        /// </summary>
        private static bool EmitChainedBurst(
            in ResolvedSkill skill,
            in CastContext context,
            float3 direction,
            in EnemyTargets targets,
            NativeList<PendingHit> hits,
            NativeList<VfxEvent> effects)
        {
            ResolvedSkill chained = skill;
            chained.Chains = math.max(skill.Chains, KeystoneChainCount);
            chained.ChainRange = math.max(skill.ChainRange, skill.Radius);

            // The delay a skill that never chained has authored is zero, and
            // zero delay is a chain nobody can see: every jump lands on the same
            // frame and the whole thing reads as one flash.
            chained.ChainDelay = math.max(0.05f, skill.ChainDelay);

            return EmitBolt(chained, context, direction, targets, hits, effects);
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
                VfxId = skill.VfxId,
                ChainsRemaining = skill.Chains,
                ChainRange = skill.ChainRange,
                ChainDelay = skill.ChainDelay,
                Delay = 0f,
                ExplosionRadius = skill.ExplosionRadius,
                ExplosionDamage = skill.ExplosionDamage,
                CullThreshold = skill.CullThreshold,
                ManaOnKill = skill.ManaOnKill,
                CritChance = skill.CritChance,
                CritMultiplier = skill.CritMultiplier,
                AppliedStatus = skill.AppliedStatus,

                // Depth zero is a key press; anything deeper got here because
                // something else fired.
                FromTrigger = context.Depth > 0
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

            // For the blows this blast produces. Its own effect went out with
            // the cast announcement, once, where it went off.
            VfxId = skill.VfxId,
            Delay = 0f,

            // Handed to one body inside the blast rather than to all of them.
            // A swing or a burst with a chain gem beside it now spends that
            // gem; the area stage is what keeps it to one chain per effect.
            ChainsRemaining = skill.Chains,
            ChainRange = skill.ChainRange,
            ChainDelay = skill.ChainDelay,
            ExplosionRadius = skill.ExplosionRadius,
            ExplosionDamage = skill.ExplosionDamage,
            CullThreshold = skill.CullThreshold,
            ManaOnKill = skill.ManaOnKill,
            CritChance = skill.CritChance,
            CritMultiplier = skill.CritMultiplier,
            TriggerSkillIndex = skill.TriggerSkillIndex,
            TriggerDamageScale = skill.TriggerDamageScale,
            TriggerDepth = context.Depth,
            AppliedStatus = skill.AppliedStatus
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

        private void SpawnZones(ref SystemState state, NativeList<ZoneSpawn> zones)
        {
            if (zones.Length == 0)
                return;

            using NativeArray<Entity> free = _freeZoneQuery.ToEntityArray(Allocator.Temp);
            ZoneSpawn.ActivateAll(state.EntityManager, free, zones);
        }

        /// <summary>
        /// Looks at whatever this cast is aimed at, once, for the conditional
        /// supports to ask about.
        ///
        /// The target is found the same way a bolt finds its first one — the
        /// aim cone first, then anything in range — so "what the condition asked
        /// about" and "what a bolt would hit" are the same body rather than two
        /// answers that agree most of the time.
        ///
        /// Reached only when at least one support is conditional, which is why
        /// it may afford two buffer reads on the main thread.
        /// </summary>
        private CastConditions ReadConditions(
            ref SystemState state,
            in EnemyTargets targets,
            float3 origin,
            float3 direction,
            float range,
            Entity preferred)
        {
            var conditions = CastConditions.Unknown(default);

            // The body that caused this cast, when something named one — a
            // projectile knows precisely what it landed on, and a trigger knows
            // whose death it is answering. Asked first for the same reason the
            // bolt asks it first: "the one I hit" and "the one nearest to where
            // I hit" come apart exactly in a crowd, which is where builds are
            // decided.
            //
            // It may already be gone, and then the searches below take over.
            int index = preferred != Entity.Null ? targets.IndexOf(preferred) : -1;

            if (index < 0)
                index = targets.FindNearestInArc(origin, direction, BoltAcquireCosine, range);

            if (index < 0)
                index = targets.FindNearest(origin, range);

            if (index < 0)
                return conditions;

            Entity target = targets.Entities[index];
            conditions.HasTarget = true;

            // Counted here rather than in a helper on EnemyTargets, because it
            // is the one question in this file that is about the fight and not
            // about a body — and it is one walk of an array already in hand, on
            // a path only a build with a crowd support ever takes.
            float3 around = targets.PositionOf(index);
            float crowdSq = SkillConditions.CrowdRadius * SkillConditions.CrowdRadius;

            for (int i = 0; i < targets.Length; i++)
            {
                float3 offset = targets.PositionOf(i) - around;
                offset.y = 0f;

                if (math.lengthsq(offset) <= crowdSq)
                    conditions.TargetNearbyCount++;
            }

            if (state.EntityManager.HasComponent<Health>(target))
            {
                Health health = state.EntityManager.GetComponentData<Health>(target);

                // Guarded, because a target whose maximum is zero would make
                // every low-life condition true rather than meaningless.
                conditions.TargetHealthFraction = health.Max > 0f
                    ? math.saturate(health.Current / health.Max)
                    : 1f;
            }

            if (!state.EntityManager.HasBuffer<ActiveStatusEffect>(target))
                return conditions;

            DynamicBuffer<ActiveStatusEffect> statuses =
                state.EntityManager.GetBuffer<ActiveStatusEffect>(target, isReadOnly: true);

            // Flattened into two masks, and they are two because they answer
            // two different questions. The element mask is the same byte a
            // projectile carries its pickups in — "is this burning" and "did
            // this shot fly through fire" are one question about one set. The
            // status mask is the wider one: a target can be stunned, and being
            // stunned is not an element.
            for (int i = 0; i < statuses.Length; i++)
            {
                conditions.TargetStatuses =
                    StatusMask.With(conditions.TargetStatuses, statuses[i].Type);

                if (StatusEffects.CarriesElement(statuses[i].Type))
                {
                    conditions.TargetElements =
                        ElementMask.With(conditions.TargetElements, statuses[i].Element);
                }
            }

            return conditions;
        }

        /// <summary>
        /// Starts a blink: finds the first body the way a bolt does — the aim
        /// cone, then anything in reach — and hands the stepping to
        /// BlinkStrikeSystem. False when nobody is in reach, so a blink at empty
        /// floor costs nothing, for the reason a bolt with no target does not.
        /// </summary>
        private static bool BeginBlink(
            ref SystemState state,
            Entity character,
            in SkillCastRequest request,
            in ResolvedSkill skill,
            in EnemyTargets targets)
        {
            if (!state.EntityManager.HasComponent<BlinkSequence>(character))
                return false;

            int index = targets.FindNearestInArc(
                request.Origin, Flatten(request.Direction), BoltAcquireCosine, skill.Range);

            if (index < 0)
                index = targets.FindNearest(request.Origin, skill.Range);

            if (index < 0)
                return false;

            state.EntityManager.SetComponentData(character, new BlinkSequence
            {
                PlayerId = request.PlayerId,
                SlotIndex = request.SlotIndex,
                Next = targets.Entities[index],
                Position = request.Origin,

                // The chains ARE the extra steps, so a chain gem beside the
                // blink is one more body.
                JumpsRemaining = 1 + skill.Chains,
                Reach = skill.ChainRange,
                Interval = skill.ChainDelay,
                Timer = 0f
            });

            state.EntityManager.SetComponentEnabled<BlinkSequence>(character, true);

            // Untouchable from the press to the end of the last blow.
            if (state.EntityManager.HasComponent<Invulnerable>(character))
                state.EntityManager.SetComponentEnabled<Invulnerable>(character, true);

            return true;
        }

        /// <summary>
        /// A team spell: health and a beneficial status for allies, never a blow.
        ///
        /// Allies are the position buffer — the list of who is playing — and each
        /// one's character is where the heal lands, through the ordinary hit
        /// queue, so the resolver stays the one place health changes. A single-
        /// target spell takes the ally you face, then the nearest in reach, then
        /// you: a heal key that does nothing when you stand alone reads as broken.
        /// </summary>
        private bool EmitSupport(
            ref SystemState state,
            in ResolvedSkill skill,
            in CastContext context,
            float3 direction,
            NativeList<PendingHit> hits,
            NativeList<VfxEvent> effects)
        {
            if (!SystemAPI.TryGetSingletonBuffer<PlayerPositionElement>(
                    out DynamicBuffer<PlayerPositionElement> players, isReadOnly: true))
                return false;

            if (skill.Effect == SkillEffectKind.AllyAura)
            {
                float radius = math.max(1f, skill.Radius);
                bool touched = false;

                for (int i = 0; i < players.Length; i++)
                {
                    if (!players[i].IsTargetable)
                        continue;

                    float3 offset = players[i].Position - context.Origin;
                    offset.y = 0f;

                    if (math.lengthsq(offset) > radius * radius ||
                        !TryGetCharacter(ref state, players[i].PlayerId, out Entity ally))
                    {
                        continue;
                    }

                    hits.Add(SupportHit(skill, context, ally, players[i].Position));
                    touched = true;
                }

                return touched;
            }

            int best = FindAlly(players, context.PlayerId, context.Origin, direction, skill.Range, BoltAcquireCosine);

            if (best < 0)
                best = FindAlly(players, context.PlayerId, context.Origin, direction, skill.Range, -1f);

            int targetId = best >= 0 ? players[best].PlayerId : context.PlayerId;
            float3 at = best >= 0 ? players[best].Position : context.Origin;

            if (!TryGetCharacter(ref state, targetId, out Entity target))
                return false;

            hits.Add(SupportHit(skill, context, target, at));

            // A line to whoever it reached, so a heal on someone else is seen
            // going somewhere. None on yourself: a line of no length is a smear.
            if (best >= 0)
            {
                effects.Add(new VfxEvent
                {
                    Kind = VfxEventKind.BoltStrike,
                    Position = context.Origin,
                    EndPosition = at,
                    Color = DamageTypePalette.For(skill.Type),
                    Magnitude = 0.15f
                });
            }

            return true;
        }

        /// <summary>The nearest other player in reach, inside the arc. -1 for none; an arc cosine of -1 is any direction.</summary>
        private static int FindAlly(
            DynamicBuffer<PlayerPositionElement> players,
            int casterId,
            float3 origin,
            float3 direction,
            float range,
            float arcCosine)
        {
            int best = -1;
            float bestSq = range * range;

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId == casterId || !players[i].IsTargetable)
                    continue;

                float3 offset = players[i].Position - origin;
                offset.y = 0f;

                float distanceSq = math.lengthsq(offset);

                if (distanceSq >= bestSq ||
                    !EnemyTargets.IsInsideArc(offset, distanceSq, direction, arcCosine))
                {
                    continue;
                }

                bestSq = distanceSq;
                best = i;
            }

            return best;
        }

        private static PendingHit SupportHit(
            in ResolvedSkill skill, in CastContext context, Entity ally, float3 at) => new PendingHit
        {
            Target = ally,
            Origin = at,
            Damage = skill.Damage,
            Type = skill.Type,
            SourcePlayerId = context.PlayerId,
            VfxId = skill.VfxId,

            // Only a status that helps. A team spell with a stun gem's status on
            // it would be a stun cast on your partner.
            AppliedStatus = StatusEffects.IsBeneficial(skill.AppliedStatus)
                ? skill.AppliedStatus
                : StatusEffectType.None,

            Supportive = true,
            FromTrigger = context.Depth > 0
        };

        /// <summary>
        /// What a damage buff on the caster does: every blow this cast produces
        /// is bigger. Read once, at the cast, like every other number the fold
        /// hands down — a projectile in flight keeps the buff it left with.
        /// </summary>
        private static void ScaleOutgoing(ref SystemState state, Entity character, ref ResolvedSkill resolved)
        {
            if (character == Entity.Null ||
                SkillModifiers.IsSupportive(resolved.Effect) ||
                !state.EntityManager.HasComponent<StatusGate>(character))
            {
                return;
            }

            float dealt = state.EntityManager.GetComponentData<StatusGate>(character).DamageDealtMultiplier;

            resolved.Damage *= dealt;
            resolved.ExplosionDamage *= dealt;
        }

        private static bool IsBlinking(ref SystemState state, Entity character)
            => state.EntityManager.HasComponent<BlinkSequence>(character) &&
               state.EntityManager.IsComponentEnabled<BlinkSequence>(character);

        /// <summary>
        /// Whether something is stopping this character casting.
        ///
        /// Asked of the gate, which is the one answer every status on a body
        /// has already been reduced to — so Stun and Silence are one question
        /// here rather than two systems reaching into a buffer.
        ///
        /// A character with no gate cannot be silenced, and today that is every
        /// character: players have no health, so nothing can put a status on
        /// one, so nothing ever will. The seam is deliberate and is one line
        /// rather than none, because the decision has been taken — control works
        /// both ways round — and the thing still missing is the source, not the
        /// rule. See ModifierConditionType.CasterRecentlyHit, which is honest in
        /// exactly the same way.
        /// </summary>
        private static bool IsSilenced(ref SystemState state, Entity character)
            => state.EntityManager.HasComponent<StatusGate>(character) &&
               state.EntityManager.GetComponentData<StatusGate>(character).BlocksCasting;

        /// <summary>
        /// The rule this caster is breaking, or None.
        ///
        /// Read off the character rather than carried in the request, because it
        /// is derived from what they are wearing right now — the same argument
        /// that keeps the stats there.
        /// </summary>
        private static KeystoneEffect KeystoneOf(ref SystemState state, Entity character)
            => state.EntityManager.HasComponent<KeystoneComponent>(character)
                ? state.EntityManager.GetComponentData<KeystoneComponent>(character).Effect
                : KeystoneEffect.None;

        /// <summary>
        /// Whether the character is wearing the gear a hotkey points at.
        ///
        /// Ten slots read once per cast, on the frames a button goes down rather
        /// than every frame. The alternative — clearing bar slots whenever
        /// something is taken off — would put the same fact in two places and
        /// leave the bar to be repaired by whoever remembered.
        /// </summary>
        private bool IsWorn(ref SystemState state, Entity character, Entity gear)
            => state.EntityManager.HasBuffer<EquippedItem>(character) &&
               GemSockets.IsWorn(
                   state.EntityManager.GetBuffer<EquippedItem>(character, true), gear);

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

            /// <summary>The rule the caster is breaking, or None.</summary>
            public KeystoneEffect Keystone;

            /// <summary>
            /// Which way a swing sweeps, for the slash to follow. False for
            /// everything that is not a player's melee press.
            /// </summary>
            public bool SweepRight;
        }

        /// <summary>Flattens onto the ground plane, with a fallback for a zero aim.</summary>
        private static float3 Flatten(float3 direction)
        {
            direction.y = 0f;

            return math.lengthsq(direction) > 1e-4f
                ? math.normalize(direction)
                : new float3(0f, 0f, 1f);
        }

        /// <summary>
        /// How far off the aim this copy of a multicast goes, in radians.
        ///
        /// Centred on the aim, so an odd number of casts still has one going
        /// exactly where the player pointed.
        /// </summary>
        private static float SpreadAngle(int index, int count, float spreadDegrees)
            => count <= 1 ? 0f : (index - (count - 1) * 0.5f) * math.radians(spreadDegrees);

        /// <summary>Turns a direction on the ground plane by an angle.</summary>
        internal static float3 Turn(float3 direction, float angle)
        {
            if (angle == 0f)
                return direction;

            math.sincos(angle, out float sin, out float cos);

            return new float3(
                direction.x * cos - direction.z * sin,
                direction.y,
                direction.x * sin + direction.z * cos);
        }

        /// <summary>
        /// Where this copy of a multicast is aimed.
        ///
        /// The aim swings with the direction, around the caster. Without it
        /// every copy of a burst and every copy of a zone landed on exactly the
        /// same square metre — three novas inside one another, which is a damage
        /// multiplier wearing a multicast as a costume, and a spread gem beside
        /// them that could not possibly do anything.
        /// </summary>
        private static float3 SpreadAim(float3 origin, float3 aimPoint, float angle)
            => angle == 0f ? aimPoint : origin + Turn(aimPoint - origin, angle);

        /// <summary>
        /// Takes the height from where the player is pointing rather than from
        /// the caster. The pointer aim is a point on the ground, which is exactly
        /// the height a patch of burning floor wants to be at.
        /// </summary>
        private static float3 OnGround(float3 position, float3 aimPoint)
            => new float3(position.x, aimPoint.y, position.z);

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
