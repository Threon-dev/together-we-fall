using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// The one place two elements meeting means anything.
    ///
    /// It runs between the hit stage and the resolver, on the one buffer where
    /// everything that wants to hurt something has already gathered. That is the
    /// moment both halves of this feature converge: a status the target has been
    /// carrying for two seconds and an element a projectile picked up crossing a
    /// fire wall arrive at exactly the same place, at exactly the same time, and
    /// are answered by exactly the same lookup. Neither the projectile nor the
    /// status knows there is such a thing as a reaction.
    ///
    /// It also applies every status in the game — the element a blow leaves
    /// behind, the one a reaction produces, and the one a skill named outright,
    /// which is how anything that is not elemental ever lands. Three sources,
    /// one method, one set of rules about immunity and stacking; a second system
    /// for control statuses would be a second answer to "may this land".
    ///
    /// Applying is deliberately not a second system.
    /// Reacting and marking are two steps of one pass over one buffer, in a fixed
    /// order — react with what is already there, THEN leave your own mark, or the
    /// incoming fire would meet the ignite it just caused. Split in two, the
    /// second system would have to be told which events the first had already
    /// spent, which is an intermediate state written down; the same argument that
    /// kept inventory removal and placement inside one system.
    ///
    /// One reaction per blow. A target that is both shocked and chilled and takes
    /// a fire hit reacts once, with the newest status that HAS a rule — newest
    /// because the most recent thing the player set up is the one they are
    /// waiting to see pay off, and "that has a rule" because a newest status with
    /// no rule authored would otherwise silently swallow the combination the
    /// player did build. Cascading reactions are deliberately out.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Skills.Systems.SkillHitSystem))]
    [UpdateBefore(typeof(DamageResolutionSystem))]
    public partial struct ElementReactionSystem : ISystem
    {
        /// <summary>
        /// One zone, as much of it as marking a body needs.
        ///
        /// A flat copy handed to the job rather than a lookup, because the job
        /// walks enemies and a zone is a different entity entirely — the same
        /// shape as KeystoneSet, and for the same reason.
        /// </summary>
        private struct ZoneAura
        {
            public float3 Position;
            public float Radius;
            public DamageType Element;

            /// <summary>The status the zone's skill named, or None.</summary>
            public StatusEffectType Applied;

            public int PlayerId;
        }

        /// <summary>
        /// The zones burning this frame, small enough to be a job field.
        ///
        /// Its own struct rather than a bare FixedList so the job field reads
        /// as what it is, and so the capacity lives beside the thing it caps.
        /// </summary>
        private struct ZoneAuraSet
        {
            public FixedList512Bytes<ZoneAura> Zones;
        }

        /// <summary>
        /// How far a status may have decayed before standing in the zone
        /// renews it.
        ///
        /// Not every frame, and that is the whole design of it. Re-applying at
        /// sixty hertz would drive a stacking status to its cap in three frames
        /// — a poison cloud would be at maximum stacks before anybody had
        /// decided to walk out of it. Renewing at half-life means a body
        /// standing in the cloud keeps the mark alive and builds it up at the
        /// rhythm the pulses already set, and a body that leaves carries what it
        /// has out with it and watches it run down.
        /// </summary>
        private const float ZoneRenewFraction = 0.5f;

        private EntityQuery _characterQuery;
        private ComponentLookup<LocalTransform> _positions;

        public void OnCreate(ref SystemState state)
        {
            _positions = state.GetComponentLookup<LocalTransform>(isReadOnly: true);

            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, KeystoneComponent>()
                .Build();

            state.RequireForUpdate<ElementReactionDatabase>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // TempJob rather than Temp for anything in a job field, even for Run:
            // scheduling rejects Temp containers outright, because Temp memory is
            // not required to outlive the call that made it.
            using var areas = new NativeList<PendingArea>(4, Allocator.TempJob);
            using var effects = new NativeList<VfxEvent>(8, Allocator.TempJob);
            using var triggers = new NativeList<TriggerEvent>(4, Allocator.TempJob);

            // Run, not Schedule, because the queues below are drained on this
            // thread in this same update. Run passes a default dependency, so
            // whatever already reads these buffers has to be finished first —
            // the same call the projectile system makes for the same reason.
            state.CompleteDependency();
            _positions.Update(ref state);

            new ReactJob
            {
                Positions = _positions,

                Database = SystemAPI.GetSingleton<ElementReactionDatabase>(),
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime,

                // The zones burning on the floor right now, so a body standing
                // in one is marked by BEING there rather than by being caught
                // by a pulse. See ZoneAura for why that is a different thing.
                Zones = GatherZones(ref state),

                // Rebuilt every frame from the characters, because this job
                // walks enemies and holds nothing but a player id — there is no
                // entity here to look a component up with. Four entries at most.
                Keystones = GatherKeystones(),
                Areas = areas,
                Effects = effects,
                Triggers = triggers
            }.Run();

            Append(ref state, areas, effects, triggers);
        }

        /// <summary>
        /// Every zone currently burning, flattened into something a job can
        /// hold.
        ///
        /// Rebuilt every frame rather than kept, for the same reason the
        /// keystones are: the components ARE the answer, and a published copy
        /// would be a second one to keep in step. There are a handful of zones
        /// at most — the pool is the ceiling — so the walk is nothing.
        ///
        /// Past capacity the extra zones are dropped rather than allocating in
        /// a per-frame path. Twenty-one is far more than the pool hands out.
        /// </summary>
        private ZoneAuraSet GatherZones(ref SystemState state)
        {
            var set = new ZoneAuraSet();

            foreach ((RefRO<ElementZone> zone, RefRO<LocalTransform> transform) in
                     SystemAPI.Query<RefRO<ElementZone>, RefRO<LocalTransform>>()
                         .WithAll<ZoneActive>())
            {
                if (set.Zones.Length >= set.Zones.Capacity)
                    break;

                set.Zones.Add(new ZoneAura
                {
                    Position = transform.ValueRO.Position,
                    Radius = zone.ValueRO.Radius,
                    Element = zone.ValueRO.Element,
                    Applied = zone.ValueRO.AppliedStatus,
                    PlayerId = zone.ValueRO.SourcePlayerId
                });
            }

            return set;
        }

        private KeystoneSet GatherKeystones()
        {
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<KeystoneComponent> keystones =
                _characterQuery.ToComponentDataArray<KeystoneComponent>(Allocator.Temp);

            return KeystoneSet.Gather(characters, keystones);
        }

        private void Append(
            ref SystemState state,
            NativeList<PendingArea> areas,
            NativeList<VfxEvent> effects,
            NativeList<TriggerEvent> triggers)
        {
            if (triggers.Length > 0)
            {
                DynamicBuffer<TriggerEvent> queue = SystemAPI.GetSingletonBuffer<TriggerEvent>();
                for (int i = 0; i < triggers.Length; i++)
                    TriggerEvents.Announce(queue, triggers[i]);
            }

            if (areas.Length > 0)
            {
                // Drained by the area system, which has already run this frame,
                // so a reaction blast goes off on the next one. The same
                // sixteen milliseconds a triggered skill waits, and for the same
                // reason: one ordering of the stages, not a stage that runs twice.
                DynamicBuffer<PendingArea> queue = SystemAPI.GetSingletonBuffer<PendingArea>();
                for (int i = 0; i < areas.Length; i++)
                    queue.Add(areas[i]);
            }

            if (effects.Length == 0)
                return;

            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            for (int i = 0; i < effects.Length; i++)
                vfx.Add(effects[i]);
        }

        /// <summary>
        /// WithAny on the enemy tag, which is enableable, so a body already
        /// fading is out of this: a corpse taking splash damage should not light
        /// up with reactions on its way off the screen. Player characters are in
        /// it too — a team spell puts its status on an ally here, the same Apply
        /// every status goes through — but a character is a sheet, not a body:
        /// it has no position, so it stands in no zone and announces no trigger.
        /// </summary>
        [BurstCompile]
        [WithAny(typeof(EnemyTag), typeof(PlayerCharacter))]
        private partial struct ReactJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<LocalTransform> Positions;

            public ElementReactionDatabase Database;
            public float ElapsedTime;
            public KeystoneSet Keystones;

            /// <summary>The zones on the floor, for the presence pass.</summary>
            public ZoneAuraSet Zones;

            public NativeList<PendingArea> Areas;
            public NativeList<VfxEvent> Effects;
            public NativeList<TriggerEvent> Triggers;

            /// <summary>
            /// The body a blow landed on, and everything applying a status to it
            /// needs to know.
            ///
            /// Gathered once per entity and handed down, rather than five
            /// parameters threaded through four methods. The buffers are handles
            /// and copying one copies nothing, so this is free — and it means
            /// adding the next thing an application has to consult is one field
            /// rather than a signature change everywhere.
            /// </summary>
            private struct Afflicted
            {
                public Entity Entity;
                public float3 Position;
                public DynamicBuffer<ActiveStatusEffect> Statuses;
                public DynamicBuffer<CrowdControlImmunity> Immunities;
                public CrowdControlResistance Resistance;

                /// <summary>Whether this is something standing in the world rather than a character sheet.</summary>
                public bool IsBody;
            }

            private void Execute(
                Entity entity,
                DynamicBuffer<DamageEvent> events,
                DynamicBuffer<ActiveStatusEffect> statuses,
                DynamicBuffer<CrowdControlImmunity> immunities,
                in CrowdControlResistance resistance)
            {
                if (events.Length == 0 && Zones.Zones.Length == 0)
                    return;

                bool isBody = Positions.TryGetComponent(entity, out LocalTransform body);

                var target = new Afflicted
                {
                    Entity = entity,
                    Position = isBody ? body.Position : float3.zero,
                    Statuses = statuses,
                    Immunities = immunities,
                    Resistance = resistance,
                    IsBody = isBody
                };

                // Standing in something counts, whether or not it hit you this
                // frame. Before the blows, so a body that has just walked into
                // a cloud is already poisoned when the pulse that catches it
                // arrives — and so the pulse refreshes a mark rather than
                // creating one a frame later.
                if (target.IsBody)
                    StandingInZones(target);

                for (int i = 0; i < events.Length; i++)
                {
                    DamageEvent damage = events[i];

                    // A team spell: its status and nothing else — no element
                    // mark and no reaction. A heal is not a physical blow.
                    if (damage.Supportive)
                    {
                        Applied(damage, target);
                        continue;
                    }

                    // A burn tick, or the blast a reaction already produced.
                    // It does its damage and nothing else — no reaction of its
                    // own, and no refreshing of the status that caused it.
                    if (damage.FromReaction)
                        continue;

                    bool marked = React(ref damage, target);

                    // Written back because a bonus-damage reaction changes the
                    // number, and the resolver reads this buffer next.
                    events[i] = damage;

                    // What the SKILL said it does, as opposed to what its element
                    // leaves behind. This is how everything that is not elemental
                    // ever lands: nothing stuns on its own and no pair of
                    // elements produces a stun, so a control status arrives
                    // because a skill named one and for no other reason.
                    //
                    // Before the element's own mark, because it is the deliberate
                    // half — and because the mark below stands down if this
                    // already applied the same status.
                    StatusEffectType named = Applied(damage, target);

                    // Then the blow leaves its own mark. After the reaction, so
                    // that fire landing on a burning target reacts with what was
                    // there rather than with the ignite it is about to apply.
                    //
                    // Unless the reaction already left one. A reaction producing
                    // something cold followed by the plain chill of the cold blow
                    // that caused it would overwrite the interesting half a line
                    // after producing it.
                    if (!marked)
                        ApplyDefault(damage.Type, damage.SourcePlayerId, named, target);
                }
            }

            /// <summary>
            /// Applies the status this blow was told to apply, and answers with
            /// which one that was.
            ///
            /// A skill names a status by type rather than by index, because the
            /// skill database and the status table are baked by two authoring
            /// objects with no shared ordering. A name nothing answers to is
            /// simply inert — the same failure a gem naming a skill that is not
            /// in the database already has.
            /// </summary>
            private StatusEffectType Applied(in DamageEvent damage, Afflicted target)
            {
                if (damage.AppliedStatus == StatusEffectType.None)
                    return StatusEffectType.None;

                if (!Database.TryGetStatusOfType(
                        damage.AppliedStatus, out int index, out StatusBlob status))
                {
                    return StatusEffectType.None;
                }

                Apply(index, status, damage.SourcePlayerId, target);
                return damage.AppliedStatus;
            }

            /// <summary>
            /// Finds the one element this blow reacts with, and resolves it.
            ///
            /// Carried elements are considered before statuses, because a carried
            /// element is something the player arranged this second — steering a
            /// bolt through a wall of fire — while a status is the residue of
            /// something that already happened. When both could fire, the
            /// deliberate one should be what the player sees.
            ///
            /// Returns whether the reaction left a status of its own, which is
            /// the one case where the blow does not then leave its ordinary mark.
            /// </summary>
            private bool React(ref DamageEvent damage, Afflicted target)
            {
                byte carried = ElementMask.Without(damage.CarriedElements, damage.Type);

                for (int element = 0; element < ElementMask.Count; element++)
                {
                    var candidate = (DamageType)element;

                    if (!ElementMask.Has(carried, candidate))
                        continue;

                    if (!Database.TryGetRule(candidate, damage.Type, out ElementReactionRuleBlob rule))
                        continue;

                    // No status index to consume: a carried element is spent by
                    // arriving, whatever the rule says about consuming.
                    return Resolve(rule, ref damage, target, existing: -1);
                }

                DynamicBuffer<ActiveStatusEffect> statuses = target.Statuses;

                int newest = -1;
                float newestTime = float.MinValue;
                var chosen = default(ElementReactionRuleBlob);

                for (int i = 0; i < statuses.Length; i++)
                {
                    // A stun is on the target and is not an element, so it is not
                    // one half of a pair. Skipped rather than answered with
                    // Physical, which is what a status carrying no element would
                    // otherwise look like from here.
                    if (!StatusEffects.CarriesElement(statuses[i].Type))
                        continue;

                    if (statuses[i].Element == damage.Type)
                        continue;

                    if (!Database.TryGetRule(
                            statuses[i].Element, damage.Type, out ElementReactionRuleBlob rule))
                    {
                        continue;
                    }

                    if (statuses[i].AppliedAt <= newestTime)
                        continue;

                    newestTime = statuses[i].AppliedAt;
                    newest = i;
                    chosen = rule;
                }

                if (newest < 0)
                    return false;

                return Resolve(chosen, ref damage, target, newest);
            }

            /// <summary>
            /// Carries out one rule. Returns whether it left a status behind.
            /// </summary>
            private bool Resolve(
                in ElementReactionRuleBlob rule,
                ref DamageEvent damage,
                Afflicted target,
                int existing)
            {
                DamageType announced = damage.Type;
                bool marked = false;

                switch (rule.Kind)
                {
                    case ElementReactionKind.BonusDamage:
                        damage.Amount *= rule.DamageMultiplier;
                        break;

                    case ElementReactionKind.ApplyStatus:
                        if (Database.TryGetStatus(rule.ResultStatus, out StatusBlob result))
                        {
                            announced = result.Element;
                            Apply(rule.ResultStatus, result, damage.SourcePlayerId, target);
                            marked = true;
                        }

                        break;

                    case ElementReactionKind.Explosion:
                        Areas.Add(new PendingArea
                        {
                            Position = target.Position,
                            Direction = new float3(0f, 0f, 1f),
                            Radius = rule.Radius,

                            // A full circle: a reaction has no facing.
                            ArcCosine = -1f,
                            Damage = damage.Amount * rule.DamageMultiplier,
                            Type = damage.Type,
                            SourcePlayerId = damage.SourcePlayerId,
                            Delay = 0f,

                            // Detonates nothing further and casts nothing. Zero
                            // is a real skill index, so the trigger has to be
                            // said out loud rather than left at its default.
                            ExplosionRadius = 0f,
                            ExplosionDamage = 0f,
                            TriggerSkillIndex = -1,

                            // And it applies nothing. A reaction blast that
                            // stunned everything it caught would be a stun-lock
                            // reached through the one path immunity is granted
                            // by, and the caster never asked for it.
                            AppliedStatus = StatusEffectType.None,

                            // And its damage starts no further reactions. Without
                            // this one field a crowd where everything is burning
                            // detonates itself from a single hit.
                            FromReaction = true
                        });

                        break;
                }

                // Consuming is about the status that was already there. A rule
                // that consumes but fired off a carried element has nothing to
                // spend — the element was gone the moment it arrived.
                //
                // The keystone overrules the rule, which is exactly what a
                // keystone is for: the table stops being "some of these keep
                // paying out" and becomes "every mark is worth one combination".
                // One comparison, on a list that is empty for anybody not
                // wearing one.
                bool consumes = rule.ConsumesExisting ||
                                Keystones.Has(
                                    damage.SourcePlayerId, KeystoneEffect.ReactionsAlwaysConsume);

                if (consumes && existing >= 0)
                {
                    // Consuming a hard control is still that control ending, so
                    // it opens the immunity window like any other ending. A
                    // reaction that could cleanse a stun and let the next one
                    // land immediately would be the one hole in diminishing
                    // returns, reached without anyone meaning to make it.
                    GrantImmunity(target, target.Statuses[existing]);
                    target.Statuses.RemoveAtSwapBack(existing);
                }

                // One generic flash in the colour of whatever came out of it.
                // Per-pair effects are a later problem; this is enough to see
                // that something combined rather than merely landed.
                Effects.Add(new VfxEvent
                {
                    Kind = VfxEventKind.ElementBurst,
                    Position = target.Position,
                    Color = DamageTypePalette.For(announced),
                    Magnitude = rule.Kind == ElementReactionKind.Explosion
                        ? rule.Radius
                        : 1.2f
                });

                return marked;
            }

            /// <summary>
            /// Marks a body for standing in a zone, rather than for being hit
            /// by one.
            ///
            /// This is the difference between a patch of poison and a pulse of
            /// poison damage. A pulse happens every second and a half, so a body
            /// that ran across the cloud between two of them took nothing at all
            /// and walked out clean — which is not what a cloud is. Presence is
            /// asked every frame instead: step in and you are marked on that
            /// frame, stay and it is renewed, walk out and it runs down.
            ///
            /// Renewed rather than re-applied, which is the whole reason
            /// ZoneRenewFraction exists: applying at sixty hertz would pin a
            /// stacking status at its cap instantly. The pulses still do the
            /// stacking, at the rhythm the skill was authored with.
            ///
            /// It goes through the same Apply every other status does, so a zone
            /// obeys immunity, diminishing returns and the keystone without
            /// knowing any of them exist.
            /// </summary>
            private void StandingInZones(Afflicted target)
            {
                for (int i = 0; i < Zones.Zones.Length; i++)
                {
                    ZoneAura zone = Zones.Zones[i];

                    float3 offset = target.Position - zone.Position;

                    // Flat, like every other radius test in the pipeline: a disc
                    // on the floor and a body standing on it differ in height by
                    // whatever the art happens to be.
                    offset.y = 0f;

                    if (math.lengthsq(offset) > zone.Radius * zone.Radius)
                        continue;

                    // What the element leaves on its own — the poison of a chaos
                    // cloud, the burn of a fire wall.
                    int index = Database.DefaultStatusOf(zone.Element);

                    if (index >= 0 && Database.TryGetStatus(index, out StatusBlob mark) &&
                        NeedsRenewing(mark, target))
                    {
                        Apply(index, mark, zone.PlayerId, target);
                    }

                    // And the one the skill named outright, which is the only
                    // way a zone ever roots or stuns.
                    if (zone.Applied == StatusEffectType.None)
                        continue;

                    if (Database.TryGetStatusOfType(zone.Applied, out int named, out StatusBlob status) &&
                        NeedsRenewing(status, target))
                    {
                        Apply(named, status, zone.PlayerId, target);
                    }
                }
            }

            /// <summary>
            /// Whether a body standing in a zone is owed this mark: it has none,
            /// or the one it has is more than half spent.
            /// </summary>
            private static bool NeedsRenewing(in StatusBlob status, Afflicted target)
            {
                for (int i = 0; i < target.Statuses.Length; i++)
                {
                    if (target.Statuses[i].Type != status.Type)
                        continue;

                    return target.Statuses[i].RemainingDuration <
                           status.Duration * ZoneRenewFraction;
                }

                return true;
            }

            /// <summary>
            /// Marks the target with the element of the blow that just landed.
            ///
            /// This is the whole of elemental status application: an element
            /// leaves what the table says it leaves, and an element that leaves
            /// nothing is an ordinary answer rather than a hole. Physical marks
            /// nothing today.
            /// </summary>
            private void ApplyDefault(
                DamageType element,
                int playerId,
                StatusEffectType already,
                Afflicted target)
            {
                int index = Database.DefaultStatusOf(element);

                if (index < 0 || !Database.TryGetStatus(index, out StatusBlob status))
                    return;

                // The skill already named this one. Applying it twice for one
                // blow would double its stacks, and a cold skill that explicitly
                // chills is exactly the asset somebody would author.
                if (status.Type == already)
                    return;

                Apply(index, status, playerId, target);
            }

            /// <summary>
            /// Adds a status, or refreshes the one already holding that type.
            ///
            /// Keyed by type rather than by which definition produced it, so a
            /// target can never be stunned twice under two names, and the buffer
            /// cannot grow past the number of statuses there are. For the
            /// elemental half that also keeps "the other element on this target"
            /// a question with a single answer, which is what the reaction search
            /// depends on — the table admits one status per element as that
            /// element's mark, and the baker says so when two claim one.
            ///
            /// This is also where a control status can be refused, and the two
            /// reasons are deliberately different in kind. Innate resistance is
            /// what a boss IS, and diminishing returns are what anybody becomes
            /// after being controlled; a boss without the first would be a boss
            /// that a trigger gem and a stun skill reduce to a cutscene with gaps
            /// in it.
            /// </summary>
            private void Apply(
                int definition,
                in StatusBlob status,
                int playerId,
                Afflicted target)
            {
                ApplyOne(definition, status, playerId, target);

                // The keystone, and the reason it is a second call here rather
                // than a recursive one inside the method below: Burst does not
                // compile recursion at all, and "applying a status may apply a
                // status" is exactly the shape that reads as recursion. Two
                // straight-line calls, with the second impossible to reach from
                // itself — Vulnerable is not a Slow.
                if (status.Type != StatusEffectType.Slow)
                    return;

                if (!Keystones.Has(playerId, KeystoneEffect.SlowsAlsoWeaken))
                    return;

                if (Database.TryGetStatusOfType(
                        StatusEffectType.Vulnerable, out int index, out StatusBlob vulnerable))
                {
                    ApplyOne(index, vulnerable, playerId, target);
                }
            }

            /// <summary>
            /// Adds or refreshes exactly one status, and nothing follows from
            /// it. The whole of application; Apply above is what decides how
            /// many times this happens.
            /// </summary>
            private void ApplyOne(
                int definition,
                in StatusBlob status,
                int playerId,
                Afflicted target)
            {
                if (!Admits(status.Type, target))
                    return;

                // Only control is shortened by innate resistance. A boss that
                // also burned for less would be resisting fire, which is a
                // different statistic and one this prototype does not have.
                float duration = status.Duration;
                if (StatusEffects.CategoryOf(status.Type) == StatusCategory.CrowdControl)
                    duration *= math.max(0f, target.Resistance.DurationMultiplier);

                if (duration <= 0f)
                    return;

                // Announced whether it is a fresh mark or a refresh, because a
                // gem that fires "when you apply a status" means the act, not
                // the novelty — keeping a crowd alight is applying fire.
                //
                // Capped by the list rather than by anything here: three hundred
                // burning bodies are three hundred applications and, for a
                // trigger on a cooldown, one cast.
                // Not for a character: "on status applied" gems aim at the body
                // the status landed on, and a sheet has nowhere to aim.
                if (target.IsBody && Triggers.Length < TriggerEvents.MaxPerFrame)
                {
                    Triggers.Add(new TriggerEvent
                    {
                        Condition = TriggerConditionType.OnStatusApplied,
                        PlayerId = playerId,
                        Position = target.Position,
                        Target = target.Entity
                    });
                }

                DynamicBuffer<ActiveStatusEffect> statuses = target.Statuses;

                for (int i = 0; i < statuses.Length; i++)
                {
                    if (statuses[i].Type != status.Type)
                        continue;

                    ActiveStatusEffect existing = statuses[i];

                    existing.Definition = definition;
                    existing.Element = status.Element;

                    // Two ways of arriving again, and the flag is on the asset
                    // rather than decided here. A poison wants the time added; a
                    // stun must never have it added, because "stunned during a
                    // stun" is how a crowd stops being a fight.
                    existing.RemainingDuration = status.StacksDuration
                        ? math.min(
                            existing.RemainingDuration + duration, duration * status.MaxStacks)
                        : math.max(existing.RemainingDuration, duration);

                    existing.Stacks = math.min(status.MaxStacks, existing.Stacks + 1);
                    existing.Magnitude = status.MagnitudePerStack * existing.Stacks;
                    existing.SourcePlayerId = playerId;

                    // Refreshing counts as new. A target being kept alight is
                    // carrying a fresher fire than one shocked ten seconds ago,
                    // and that is the order the reaction search wants.
                    existing.AppliedAt = ElapsedTime;

                    statuses[i] = existing;
                    return;
                }

                statuses.Add(new ActiveStatusEffect
                {
                    Type = status.Type,
                    Element = status.Element,
                    Definition = definition,
                    RemainingDuration = duration,
                    TickRemaining = status.TickInterval,
                    Stacks = 1,
                    Magnitude = status.MagnitudePerStack,
                    SourcePlayerId = playerId,
                    AppliedAt = ElapsedTime
                });
            }

            /// <summary>
            /// Whether this target may be put under this status at all.
            ///
            /// Ignored outright rather than applied with a shortened duration,
            /// which was the choice offered and is the one the player can read.
            /// A stun that lands for a fifth of a second is a stun that looks
            /// like it worked and did nothing; one that does not land is one the
            /// player learns the rhythm of.
            /// </summary>
            private bool Admits(StatusEffectType type, Afflicted target)
            {
                if (!StatusEffects.IsHardControl(type))
                    return true;

                if (target.Resistance.ImmuneToHardControl)
                    return false;

                DynamicBuffer<CrowdControlImmunity> immunities = target.Immunities;

                for (int i = 0; i < immunities.Length; i++)
                {
                    if (immunities[i].Type == type)
                        return false;
                }

                return true;
            }

            /// <summary>
            /// Opens the diminishing-returns window for a control that has just
            /// been spent by a reaction.
            ///
            /// The same rule as the one at expiry, in the other place a status
            /// can end. Two copies rather than one shared helper because the
            /// systems are separate and a static shared between two Burst jobs
            /// in two files is a worse seam than eight lines — but they are the
            /// same rule, and the day they differ this comment is the reason to
            /// suspect it.
            /// </summary>
            private void GrantImmunity(Afflicted target, in ActiveStatusEffect status)
            {
                if (!StatusEffects.IsHardControl(status.Type))
                    return;

                if (!Database.TryGetStatus(status.Definition, out StatusBlob definition))
                    return;

                float window = definition.Duration * definition.ImmunityMultiplier;
                if (window <= 0f)
                    return;

                DynamicBuffer<CrowdControlImmunity> immunities = target.Immunities;

                for (int i = 0; i < immunities.Length; i++)
                {
                    if (immunities[i].Type != status.Type)
                        continue;

                    CrowdControlImmunity existing = immunities[i];
                    existing.Remaining = math.max(existing.Remaining, window);
                    immunities[i] = existing;
                    return;
                }

                immunities.Add(new CrowdControlImmunity
                {
                    Type = status.Type,
                    Remaining = window
                });
            }
        }
    }
}
