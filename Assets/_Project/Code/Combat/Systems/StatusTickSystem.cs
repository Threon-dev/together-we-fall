using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Combat.Systems
{
    /// <summary>
    /// The one place the status buffer is walked.
    ///
    /// It does four things in one pass, and they are one pass on purpose. It
    /// runs every status down and drops the ones that are over; it makes the
    /// ones that burn hurt; it hands out crowd-control immunity as hard control
    /// ends; and it reduces whatever is left to the single answer the rest of
    /// the game reads — StatusGate and StatusVisual.
    ///
    /// The specification this grew from asked for a separate StatusExpirySystem
    /// and four more systems for the control effects. Expiry is one comparison
    /// inside a loop that has already loaded the entry, and splitting it out
    /// would leave a frame in which a status with no duration left still stops
    /// its target moving — an intermediate state written into data, which is the
    /// same argument that kept inventory removal and placement in one system.
    /// The four control systems would each walk the same buffer to answer half
    /// a question, and the two consumers — movement and casting — would then
    /// have to consult all four rather than one field.
    ///
    /// A tick produces a PendingHit on the afflicted entity rather than a
    /// DamageEvent directly, which keeps the rule this pipeline is built on
    /// intact: SkillHitSystem is still the only thing that writes damage. That
    /// is not tidiness for its own sake. A hit gets the per-frame budget for
    /// free — three hundred burning enemies are three hundred hits, exactly like
    /// a blast that caught three hundred — and it goes through the same stage as
    /// everything else, so nothing about a burn needs its own path.
    ///
    /// Ticks are marked so they cause no reaction of their own. A burn that
    /// refreshed its own status would be a burn that never goes out, and the
    /// symptom would not look like a bug in this file — it would look like an
    /// enemy that cannot be killed by anything else.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TogetherWeFall.Skills.Systems.SkillHitSystem))]
    public partial struct StatusTickSystem : ISystem
    {
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
            using var hits = new NativeList<PendingHit>(8, Allocator.TempJob);

            state.CompleteDependency();
            _positions.Update(ref state);

            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<KeystoneComponent> keystones =
                _characterQuery.ToComponentDataArray<KeystoneComponent>(Allocator.Temp);

            new TickStatusesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Database = SystemAPI.GetSingleton<ElementReactionDatabase>(),

                // Rebuilt from the characters each frame: the burn knows who lit
                // it as a player id, and a player id is not something a
                // ComponentLookup can be asked about.
                Keystones = KeystoneSet.Gather(characters, keystones),
                Hits = hits,
                Positions = _positions
            }.Run();

            if (hits.Length == 0)
                return;

            DynamicBuffer<PendingHit> queue = SystemAPI.GetSingletonBuffer<PendingHit>();
            for (int i = 0; i < hits.Length; i++)
                queue.Add(hits[i]);
        }

        /// <summary>
        /// WithAny on the enemy tag, which is enableable — so a body that has
        /// died stops burning, and its statuses stop costing anything, without
        /// this system knowing that death exists. Player characters too, so a
        /// buff an ally cast runs out and folds into their gate like any status.
        /// </summary>
        [BurstCompile]
        [WithAny(typeof(EnemyTag), typeof(PlayerCharacter))]
        private partial struct TickStatusesJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<LocalTransform> Positions;

            public float DeltaTime;
            public ElementReactionDatabase Database;
            public KeystoneSet Keystones;

            public NativeList<PendingHit> Hits;

            private void Execute(
                Entity entity,
                DynamicBuffer<ActiveStatusEffect> statuses,
                DynamicBuffer<CrowdControlImmunity> immunities,
                ref StatusGate gate,
                ref StatusVisual visual)
            {
                // Only a burn needs it, and a character sheet has none to give.
                float3 position = Positions.TryGetComponent(entity, out LocalTransform body)
                    ? body.Position
                    : float3.zero;

                RunImmunitiesDown(immunities);

                // Rebuilt from nothing every frame rather than adjusted as
                // statuses come and go. A running total would be a second copy
                // of what the buffer already says, and the day one application
                // path forgets to subtract its share, an enemy is slowed forever
                // with nothing on it to explain why.
                gate = StatusGate.Neutral;
                visual = default;

                if (statuses.Length == 0)
                    return;

                // Backwards, so removing an expired status swaps in an entry
                // this pass has already dealt with rather than one it has not.
                for (int i = statuses.Length - 1; i >= 0; i--)
                {
                    ActiveStatusEffect status = statuses[i];
                    status.RemainingDuration -= DeltaTime;

                    if (status.RemainingDuration <= 0f)
                    {
                        GrantImmunity(immunities, status);
                        statuses.RemoveAtSwapBack(i);
                        continue;
                    }

                    // A burn that resolved all at once has nothing left to run
                    // down, so it leaves rather than sitting at zero damage for
                    // the rest of its duration.
                    if (Burn(entity, position, ref status))
                    {
                        statuses.RemoveAtSwapBack(i);
                        continue;
                    }

                    Accumulate(status, ref gate, ref visual);
                    statuses[i] = status;
                }
            }

            /// <summary>
            /// Runs the diminishing-returns windows down and drops the ones that
            /// are over. Backwards, for the same reason as the statuses above.
            /// </summary>
            private void RunImmunitiesDown(DynamicBuffer<CrowdControlImmunity> immunities)
            {
                for (int i = immunities.Length - 1; i >= 0; i--)
                {
                    CrowdControlImmunity immunity = immunities[i];
                    immunity.Remaining -= DeltaTime;

                    if (immunity.Remaining <= 0f)
                    {
                        immunities.RemoveAtSwapBack(i);
                        continue;
                    }

                    immunities[i] = immunity;
                }
            }

            /// <summary>
            /// Opens the window in which this target cannot be put under the
            /// same control again.
            ///
            /// On any removal rather than only on expiry. A rule that only
            /// covered expiry would mean control cleansed early — by a reaction
            /// that consumes it, or by anything else that reaches into the
            /// buffer later — comes back with no gap at all, which is precisely
            /// the loop this exists to close.
            ///
            /// The multiplier is read off the definition rather than fixed here,
            /// so a half-second stun and a four-second one are not protected
            /// against for the same length of time.
            /// </summary>
            private void GrantImmunity(
                DynamicBuffer<CrowdControlImmunity> immunities, in ActiveStatusEffect status)
            {
                if (!StatusEffects.IsHardControl(status.Type))
                    return;

                if (!Database.TryGetStatus(status.Definition, out StatusBlob definition))
                    return;

                float window = definition.Duration * definition.ImmunityMultiplier;
                if (window <= 0f)
                    return;

                for (int i = 0; i < immunities.Length; i++)
                {
                    if (immunities[i].Type != status.Type)
                        continue;

                    // The longer of the two. A second stun ending early must not
                    // shorten the protection an earlier, longer one bought.
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

            /// <summary>
            /// Folds one status into the answers the rest of the game reads.
            ///
            /// Every status goes through here, whatever category it is in, and
            /// most of them contribute nothing — a burn stops no movement and
            /// scales no number. That is the shape worth keeping: the cost of a
            /// status that does not gate anything is two comparisons, and there
            /// is one place to look when a target is behaving as though it were
            /// under something it is not.
            ///
            /// Multipliers multiply rather than add, so two slows compound
            /// instead of being able to reach zero between them — which is
            /// exactly the standstill that diminishing returns exist to prevent,
            /// arrived at through the one status that has no immunity.
            /// </summary>
            private static void Accumulate(
                in ActiveStatusEffect status, ref StatusGate gate, ref StatusVisual visual)
            {
                gate.BlocksMovement |= StatusEffects.BlocksMovement(status.Type);
                gate.BlocksCasting |= StatusEffects.BlocksCasting(status.Type);
                gate.Flees |= StatusEffects.Flees(status.Type);

                StatusModifierTarget target = StatusEffects.ModifierOf(status.Type, out float sign);
                float factor = 1f + sign * status.Magnitude;

                switch (target)
                {
                    case StatusModifierTarget.MoveSpeed:
                        gate.MoveSpeedMultiplier =
                            StatusEffects.ClampMove(gate.MoveSpeedMultiplier * factor);
                        break;

                    case StatusModifierTarget.DamageTaken:
                        gate.DamageTakenMultiplier =
                            StatusEffects.ClampDamage(gate.DamageTakenMultiplier * factor);
                        break;

                    case StatusModifierTarget.DamageDealt:
                        gate.DamageDealtMultiplier =
                            StatusEffects.ClampDamage(gate.DamageDealtMultiplier * factor);
                        break;
                }

                visual.Icons = StatusMask.With(visual.Icons, status.Type);

                // Loudest wins rather than a blend. Two colours averaged give a
                // third that means neither, and what has to be readable across a
                // crowd is "that one cannot move".
                if (visual.HasTint &&
                    StatusEffects.TintPriority(status.Type) <= visual.TintPriority)
                {
                    return;
                }

                visual.HasTint = true;
                visual.TintPriority = (byte)StatusEffects.TintPriority(status.Type);
                visual.Tint = StatusEffects.TintOf(status.Type, status.Element);
            }

            /// <summary>
            /// Makes one status hurt, if it is the kind that does. Returns
            /// whether it is finished.
            /// </summary>
            private bool Burn(Entity entity, float3 position, ref ActiveStatusEffect status)
            {
                if (!Database.TryGetStatus(status.Definition, out StatusBlob definition))
                    return false;

                // Most statuses only mark their target, or gate it. Shock and
                // chill exist to be reacted with, and a stun takes the fight
                // away rather than health; none of them tick at all.
                if (definition.DamagePerSecond <= 0f || definition.TickInterval <= 0f)
                    return false;

                // The keystone: everything the burn was ever going to do,
                // delivered now, and then it is over.
                //
                // A real trade rather than free damage. Nothing stays alight, so
                // nothing is left for a second element to react with — which for
                // a fire build is the difference between arranging a reaction and
                // simply hitting things.
                if (Keystones.Has(status.SourcePlayerId, KeystoneEffect.StatusInstantResolve))
                {
                    Hits.Add(MakeTick(
                        entity, position, status,
                        definition.DamagePerSecond * status.RemainingDuration * status.Stacks));

                    return true;
                }

                status.TickRemaining -= DeltaTime;
                if (status.TickRemaining > 0f)
                    return false;

                // Added rather than reset, so a long frame does not quietly stretch
                // the rhythm — and clamped, so an interval shorter than a frame
                // cannot run the counter into the ground and fire every frame.
                status.TickRemaining = math.max(
                    definition.TickInterval, status.TickRemaining + definition.TickInterval);

                // A tick is worth an interval of damage, so changing the
                // interval changes the rhythm and not the total.
                Hits.Add(MakeTick(
                    entity, position, status,
                    definition.DamagePerSecond * definition.TickInterval * status.Stacks));

                return false;
            }

            /// <summary>
            /// One helping of status damage, whether it arrived on a rhythm or
            /// all at once.
            ///
            /// One method because the two differ only in the number: a burn that
            /// resolves instantly is still a burn, and it must carry the same
            /// FromReaction flag — a status that could react would refresh
            /// itself, and an instant one that refreshed itself would never stop
            /// arriving.
            /// </summary>
            private static PendingHit MakeTick(
                Entity entity, float3 position, in ActiveStatusEffect status, float damage)
                => new PendingHit
                {
                    Target = entity,

                    // Where the body is. A tick has nowhere else to come from,
                    // and an unfilled origin would put the effect at the centre
                    // of the map.
                    Origin = position,

                    Damage = damage,
                    Type = status.Element,
                    SourcePlayerId = status.SourcePlayerId,

                    ChainsRemaining = 0,
                    Delay = 0f,
                    ExplosionRadius = 0f,
                    ExplosionDamage = 0f,

                    // A burn applies no status of its own. Naming one here would
                    // be the same loop FromReaction closes, reached through a
                    // different door.
                    AppliedStatus = StatusEffectType.None,

                    // The status doing its work, not a new blow. It reacts with
                    // nothing and refreshes nothing, least of all itself.
                    FromReaction = true
                };
        }
    }
}
