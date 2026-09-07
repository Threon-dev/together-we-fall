using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
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
    /// It also applies statuses, and that is deliberately not a second system.
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
        public void OnCreate(ref SystemState state)
        {
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

            // Run, not Schedule, because the queues below are drained on this
            // thread in this same update. Run passes a default dependency, so
            // whatever already reads these buffers has to be finished first —
            // the same call the projectile system makes for the same reason.
            state.CompleteDependency();

            new ReactJob
            {
                Database = SystemAPI.GetSingleton<ElementReactionDatabase>(),
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime,
                Areas = areas,
                Effects = effects
            }.Run();

            Append(ref state, areas, effects);
        }

        private void Append(
            ref SystemState state, NativeList<PendingArea> areas, NativeList<VfxEvent> effects)
        {
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
        /// WithAll on the enemy tag, which is enableable, so a body already
        /// fading is out of this: a corpse taking splash damage should not light
        /// up with reactions on its way off the screen.
        /// </summary>
        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct ReactJob : IJobEntity
        {
            public ElementReactionDatabase Database;
            public float ElapsedTime;

            public NativeList<PendingArea> Areas;
            public NativeList<VfxEvent> Effects;

            private void Execute(
                in LocalTransform transform,
                DynamicBuffer<DamageEvent> events,
                DynamicBuffer<ElementalStatus> statuses)
            {
                if (events.Length == 0)
                    return;

                for (int i = 0; i < events.Length; i++)
                {
                    DamageEvent damage = events[i];

                    // A burn tick, or the blast a reaction already produced.
                    // It does its damage and nothing else — no reaction of its
                    // own, and no refreshing of the status that caused it.
                    if (damage.FromReaction)
                        continue;

                    bool marked = React(ref damage, transform.Position, statuses);

                    // Written back because a bonus-damage reaction changes the
                    // number, and the resolver reads this buffer next.
                    events[i] = damage;

                    // Then the blow leaves its own mark. After the reaction, so
                    // that fire landing on a burning target reacts with what was
                    // there rather than with the ignite it is about to apply.
                    //
                    // Unless the reaction already left one. A status is keyed by
                    // element, so a reaction producing something cold followed by
                    // the plain chill of the cold blow that caused it would
                    // overwrite the interesting half a line after producing it.
                    if (!marked)
                        ApplyDefault(damage.Type, damage.SourcePlayerId, statuses);
                }
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
            private bool React(
                ref DamageEvent damage, float3 position, DynamicBuffer<ElementalStatus> statuses)
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
                    return Resolve(rule, ref damage, position, statuses, existing: -1);
                }

                int newest = -1;
                float newestTime = float.MinValue;
                var chosen = default(ElementReactionRuleBlob);

                for (int i = 0; i < statuses.Length; i++)
                {
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

                return Resolve(chosen, ref damage, position, statuses, newest);
            }

            /// <summary>
            /// Carries out one rule. Returns whether it left a status behind.
            /// </summary>
            private bool Resolve(
                in ElementReactionRuleBlob rule,
                ref DamageEvent damage,
                float3 position,
                DynamicBuffer<ElementalStatus> statuses,
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
                            Apply(rule.ResultStatus, result, damage.SourcePlayerId, statuses);
                            marked = true;
                        }

                        break;

                    case ElementReactionKind.Explosion:
                        Areas.Add(new PendingArea
                        {
                            Position = position,
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
                if (rule.ConsumesExisting && existing >= 0)
                    statuses.RemoveAtSwapBack(existing);

                // One generic flash in the colour of whatever came out of it.
                // Per-pair effects are a later problem; this is enough to see
                // that something combined rather than merely landed.
                Effects.Add(new VfxEvent
                {
                    Kind = VfxEventKind.ElementBurst,
                    Position = position,
                    Color = DamageTypePalette.For(announced),
                    Magnitude = rule.Kind == ElementReactionKind.Explosion
                        ? rule.Radius
                        : 1.2f
                });

                return marked;
            }

            /// <summary>
            /// Marks the target with the element of the blow that just landed.
            ///
            /// This is the whole of status application: an element leaves what
            /// the table says it leaves, and an element that leaves nothing is an
            /// ordinary answer rather than a hole. Physical marks nothing today.
            /// </summary>
            private void ApplyDefault(
                DamageType element, int playerId, DynamicBuffer<ElementalStatus> statuses)
            {
                int index = Database.DefaultStatusOf(element);

                if (index < 0 || !Database.TryGetStatus(index, out StatusBlob status))
                    return;

                Apply(index, status, playerId, statuses);
            }

            /// <summary>
            /// Adds a status, or refreshes the one already holding that element.
            ///
            /// Keyed by element rather than by which definition produced it, so a
            /// target can never be ignited twice under two names, and the buffer
            /// cannot grow past the number of elements there are. It is also what
            /// makes "the other element on this target" a question with a single
            /// answer, which is what the reaction search above depends on.
            /// </summary>
            private void Apply(
                int definition,
                in StatusBlob status,
                int playerId,
                DynamicBuffer<ElementalStatus> statuses)
            {
                for (int i = 0; i < statuses.Length; i++)
                {
                    if (statuses[i].Element != status.Element)
                        continue;

                    ElementalStatus existing = statuses[i];

                    existing.Definition = definition;
                    existing.RemainingDuration = status.Duration;
                    existing.Stacks = math.min(status.MaxStacks, existing.Stacks + 1);
                    existing.SourcePlayerId = playerId;

                    // Refreshing counts as new. A target being kept alight is
                    // carrying a fresher fire than one shocked ten seconds ago,
                    // and that is the order the reaction search wants.
                    existing.AppliedAt = ElapsedTime;

                    statuses[i] = existing;
                    return;
                }

                statuses.Add(new ElementalStatus
                {
                    Element = status.Element,
                    Definition = definition,
                    RemainingDuration = status.Duration,
                    TickRemaining = status.TickInterval,
                    Stacks = 1,
                    SourcePlayerId = playerId,
                    AppliedAt = ElapsedTime
                });
            }
        }
    }
}
