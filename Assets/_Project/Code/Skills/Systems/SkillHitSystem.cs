using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Audio;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Turns hits into damage intents, and makes chains jump.
    ///
    /// This is the only place a DamageEvent is written. Everything upstream —
    /// projectiles, area bursts, bolts — produces hits, and one system decides
    /// what a hit means. That is what keeps "how much damage" in one file while
    /// the number of ways to cause a hit keeps growing.
    ///
    /// Chains are the reason the hit stage exists at all rather than damage
    /// being written where it is caused. A jump is a hit that has not happened
    /// yet: it knows its delay, how many jumps are left, and who it has already
    /// visited. Without the delay every jump lands on the same frame and the
    /// chain reads as a single flash — the convention asks for fifty to a
    /// hundred milliseconds between jumps, and the skill asset carries it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SkillAreaSystem))]
    public partial struct SkillHitSystem : ISystem
    {
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate<SkillBudgetSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<PendingHit> hits = SystemAPI.GetSingletonBuffer<PendingHit>();
            if (hits.Length == 0)
                return;

            // Taken and cleared: the loop below appends the jumps it produces,
            // and walking a buffer that is growing underneath is a bug waiting
            // for the first chain support somebody sockets.
            using NativeArray<PendingHit> queued = hits.ToNativeArray(Allocator.Temp);
            hits.Clear();

            using NativeArray<Entity> enemies = _enemyQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> enemyTransforms =
                _enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var targets = new EnemyTargets { Entities = enemies, Transforms = enemyTransforms };

            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            DynamicBuffer<AudioEvent> audio = SystemAPI.GetSingletonBuffer<AudioEvent>();

            float deltaTime = SystemAPI.Time.DeltaTime;
            float dealt = 0f;

            // A blast that caught three hundred enemies is three hundred hits.
            // Spreading them over a few frames costs a few milliseconds of
            // latency on the tail of a crowd nobody is watching individually.
            int budget = SystemAPI.GetSingleton<SkillBudgetSettings>().MaxHitsPerFrame;

            for (int i = 0; i < queued.Length; i++)
            {
                PendingHit hit = queued[i];
                hit.Delay -= deltaTime;

                if (hit.Delay > 0f || budget <= 0)
                {
                    hits.Add(hit);
                    continue;
                }

                budget--;
                dealt += Apply(ref state, hit, targets, hits, vfx, audio);
            }

            if (dealt <= 0f)
                return;

            RefRW<CombatTally> tally = SystemAPI.GetSingletonRW<CombatTally>();
            tally.ValueRW.DamageDealt += dealt;
        }

        /// <summary>
        /// Writes the damage intent and queues the next jump. Returns how much
        /// was actually asked for, which is zero when the target is already gone.
        /// </summary>
        private float Apply(
            ref SystemState state,
            in PendingHit hit,
            in EnemyTargets targets,
            DynamicBuffer<PendingHit> hits,
            DynamicBuffer<VfxEvent> vfx,
            DynamicBuffer<AudioEvent> audio)
        {
            float dealt = 0f;

            // The target may have died while this hit was waiting out its delay.
            // That stops the damage, not the chain: the jump has a position of
            // its own and carries on from there.
            if (state.EntityManager.Exists(hit.Target) &&
                state.EntityManager.HasBuffer<DamageEvent>(hit.Target))
            {
                state.EntityManager.GetBuffer<DamageEvent>(hit.Target).Add(new DamageEvent
                {
                    Amount = hit.Damage,
                    Type = hit.Type,
                    SourcePlayerId = hit.SourcePlayerId,
                    ExplosionRadius = hit.ExplosionRadius,
                    ExplosionDamage = hit.ExplosionDamage,

                    // The last leg of the journey the elements made: gathered by
                    // a projectile in flight, handed to the hit, and written here
                    // beside the damage they arrived with. What they are worth is
                    // decided one stage later, on the target, by the same system
                    // that answers for statuses.
                    CarriedElements = hit.CarriedElements,

                    // And the status the skill said it applies. It is resolved
                    // one stage later by the same system that answers for the
                    // elements beside it — which is why a stun and an ignite
                    // need no separate path, only a different name.
                    AppliedStatus = hit.AppliedStatus,
                    FromReaction = hit.FromReaction,

                    // Nothing in combat reads this. It is here because a chain
                    // jump copies the whole hit, so a build that casts on kill
                    // stays labelled all the way down a chain — and the damage
                    // meter is the only thing that ever asks.
                    FromTrigger = hit.FromTrigger
                });

                dealt = hit.Damage;
            }

            if (hit.ChainsRemaining <= 0)
                return dealt;

            int next = targets.FindNearestUnvisited(hit.Origin, hit.ChainRange, hit.Visited);

            if (next < 0)
            {
                // Nothing new within reach. Rather than stopping, jump back to
                // anyone but the body it is standing on — a chain in a small
                // group should rattle between them, not die on the second hop.
                // Excluding the current target is what stops it hitting the same
                // body twice in a row and looking like it froze.
                var justStruck = new FixedList64Bytes<Entity>();
                justStruck.Add(hit.Target);

                next = targets.FindNearestUnvisited(hit.Origin, hit.ChainRange, justStruck);
            }

            if (next < 0)
                return dealt;

            PendingHit jump = hit;
            jump.Target = targets.Entities[next];
            jump.Origin = targets.PositionOf(next);
            jump.ChainsRemaining = hit.ChainsRemaining - 1;
            jump.Delay = hit.ChainDelay;

            // Past the visited list capacity a long chain may revisit a target.
            // Better that than dropping the jump: a chain that stops early looks
            // like the support does not work.
            if (jump.Visited.Length < jump.Visited.Capacity)
                jump.Visited.Add(jump.Target);

            hits.Add(jump);

            // Announced here, where the jump is decided, so the line is drawn
            // between the two bodies the chain actually connected — and drawn
            // for the length of the delay, which is what makes a chain readable
            // as a chain rather than one simultaneous flash.
            vfx.Add(new VfxEvent
            {
                Kind = VfxEventKind.ChainLink,
                Position = hit.Origin,
                EndPosition = jump.Origin,
                Color = DamageTypePalette.For(hit.Type),
                Magnitude = math.max(0.05f, hit.ChainDelay)
            });

            audio.Add(new AudioEvent
            {
                Cue = AudioCue.ChainZap,
                Position = jump.Origin,
                Volume = 0.7f
            });

            return dealt;
        }
    }
}
