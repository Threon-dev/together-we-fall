using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Enemies;
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
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ElementReactionDatabase>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var hits = new NativeList<PendingHit>(8, Allocator.TempJob);

            state.CompleteDependency();

            new TickStatusesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Database = SystemAPI.GetSingleton<ElementReactionDatabase>(),
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

                    Burn(entity, transform.Position, ref status);
                    statuses[i] = status;
                }
            }

            private void Burn(Entity entity, float3 position, ref ElementalStatus status)
            {
                if (!Database.TryGetStatus(status.Definition, out StatusBlob definition))
                    return;

                // Most statuses only mark their target. Shock and chill exist to
                // be reacted with, and never tick at all.
                if (definition.DamagePerSecond <= 0f || definition.TickInterval <= 0f)
                    return;

                status.TickRemaining -= DeltaTime;
                if (status.TickRemaining > 0f)
                    return;

                // Added rather than reset, so a long frame does not quietly stretch
                // the rhythm — and clamped, so an interval shorter than a frame
                // cannot run the counter into the ground and fire every frame.
                status.TickRemaining = math.max(
                    definition.TickInterval, status.TickRemaining + definition.TickInterval);

                Hits.Add(new PendingHit
                {
                    Target = entity,

                    // Where the body is. A tick has nowhere else to come from,
                    // and an unfilled origin would put the effect at the centre
                    // of the map.
                    Origin = position,

                    // A tick is worth an interval of damage, so changing the
                    // interval changes the rhythm and not the total.
                    Damage = definition.DamagePerSecond * definition.TickInterval * status.Stacks,
                    Type = status.Element,
                    SourcePlayerId = status.SourcePlayerId,

                    ChainsRemaining = 0,
                    Delay = 0f,
                    ExplosionRadius = 0f,
                    ExplosionDamage = 0f,

                    // The status doing its work, not a new blow. It reacts with
                    // nothing and refreshes nothing, least of all itself.
                    FromReaction = true
                });
            }
        }
    }
}
