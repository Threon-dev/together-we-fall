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
    /// Runs statuses down, and makes the ones that burn hurt.
    ///
    /// A tick produces a PendingHit on the afflicted entity rather than a
    /// DamageEvent directly, which keeps the rule this pipeline is built on
    /// intact: SkillHitSystem is still the only thing that writes damage. That is
    /// not tidiness for its own sake. A hit gets the per-frame budget for free —
    /// three hundred burning enemies are three hundred hits, exactly like a blast
    /// that caught three hundred — and it goes through the same stage as
    /// everything else, so nothing about a burn needs its own path.
    ///
    /// Ticks are marked so they cause no reaction of their own. A burn that
    /// refreshed its own status would be a burn that never goes out, and the
    /// symptom would not look like a bug in this file — it would look like an
    /// enemy that cannot be killed by anything else.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TogetherWeFall.Skills.Systems.SkillHitSystem))]
    public partial struct StatusTickSystem : ISystem
    {
        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
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
                Hits = hits
            }.Run();

            if (hits.Length == 0)
                return;

            DynamicBuffer<PendingHit> queue = SystemAPI.GetSingletonBuffer<PendingHit>();
            for (int i = 0; i < hits.Length; i++)
                queue.Add(hits[i]);
        }

        /// <summary>
        /// WithAll on the enemy tag, which is enableable — so a body that has
        /// died stops burning, and its statuses stop costing anything, without
        /// this system knowing that death exists.
        /// </summary>
        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct TickStatusesJob : IJobEntity
        {
            public float DeltaTime;
            public ElementReactionDatabase Database;
            public KeystoneSet Keystones;

            public NativeList<PendingHit> Hits;

            private void Execute(
                Entity entity,
                in LocalTransform transform,
                DynamicBuffer<ElementalStatus> statuses)
            {
                if (statuses.Length == 0)
                    return;

                // Backwards, so removing an expired status swaps in an entry
                // this pass has already dealt with rather than one it has not.
                for (int i = statuses.Length - 1; i >= 0; i--)
                {
                    ElementalStatus status = statuses[i];
                    status.RemainingDuration -= DeltaTime;

                    if (status.RemainingDuration <= 0f)
                    {
                        statuses.RemoveAtSwapBack(i);
                        continue;
                    }

                    // A burn that resolved all at once has nothing left to run
                    // down, so it leaves rather than sitting at zero damage for
                    // the rest of its duration.
                    if (Burn(entity, transform.Position, ref status))
                    {
                        statuses.RemoveAtSwapBack(i);
                        continue;
                    }

                    statuses[i] = status;
                }
            }

            /// <summary>
            /// Makes one status hurt, if it is the kind that does. Returns
            /// whether it is finished.
            /// </summary>
            private bool Burn(Entity entity, float3 position, ref ElementalStatus status)
            {
                if (!Database.TryGetStatus(status.Definition, out StatusBlob definition))
                    return false;

                // Most statuses only mark their target. Shock and chill exist to
                // be reacted with, and never tick at all.
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
                Entity entity, float3 position, in ElementalStatus status, float damage)
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

                    // The status doing its work, not a new blow. It reacts with
                    // nothing and refreshes nothing, least of all itself.
                    FromReaction = true
                };
        }
    }
}
