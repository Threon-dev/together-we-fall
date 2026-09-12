using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Runs zones down and makes them pulse.
    ///
    /// A pulse is an ordinary PendingArea, so a wall of fire hurts things through
    /// the same stage a blast does — which means its damage marks what it catches
    /// with its element, and everything downstream treats it like any other blow.
    /// That is what makes "stand in the fire and get ignited" cost no code at all.
    ///
    /// On the main thread and unbursted, unlike almost everything else in the
    /// fight. There are a handful of zones at most: a job to iterate ten entities
    /// would cost more to schedule than to run, and this way the buffer write is
    /// straightforward rather than a list gathered and drained.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SkillCastSystem))]
    [UpdateBefore(typeof(SkillAreaSystem))]
    public partial struct ElementZoneSystem : ISystem
    {
        private EntityQuery _activeQuery;

        public void OnCreate(ref SystemState state)
        {
            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<ElementZone, ZoneActive, LocalTransform>()
                .Build();

            state.RequireForUpdate<SkillEventsSingleton>();
            state.RequireForUpdate(_activeQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            DynamicBuffer<PendingArea> areas = SystemAPI.GetSingletonBuffer<PendingArea>();

            // Nothing in the loop is structural, so the buffer above stays valid
            // throughout — but a zone that burns out is retired afterwards all
            // the same, because releasing it while iterating would change the
            // set the query is walking.
            using var expired = new NativeList<Entity>(4, Allocator.Temp);

            foreach ((RefRW<ElementZone> zone, RefRO<LocalTransform> transform, Entity entity) in
                     SystemAPI.Query<RefRW<ElementZone>, RefRO<LocalTransform>>()
                         .WithAll<ZoneActive>()
                         .WithEntityAccess())
            {
                zone.ValueRW.RemainingDuration -= deltaTime;

                if (zone.ValueRO.RemainingDuration <= 0f)
                {
                    expired.Add(entity);
                    continue;
                }

                Pulse(ref zone.ValueRW, transform.ValueRO.Position, deltaTime, areas);
            }

            if (expired.Length == 0)
                return;

            ZoneSpawn.Release(state.EntityManager, expired.AsArray());
        }

        private static void Pulse(
            ref ElementZone zone,
            float3 position,
            float deltaTime,
            DynamicBuffer<PendingArea> areas)
        {
            // A zone that only tags costs nothing per frame. Which is a real
            // configuration: a cloud that changes what flies through it without
            // hurting anybody standing in it is a perfectly good skill.
            if (zone.Damage <= 0f || zone.TickInterval <= 0f)
                return;

            zone.TickRemaining -= deltaTime;
            if (zone.TickRemaining > 0f)
                return;

            zone.TickRemaining = math.max(zone.TickInterval, zone.TickRemaining + zone.TickInterval);

            // Once, on the first pulse that actually lands. A trigger fires per
            // effect instance, and a zone is one effect with a long life — per
            // pulse would make the count depend on the duration, which is the
            // same failure the blast rule exists to avoid.
            int trigger = zone.TriggerSkillIndex;
            zone.TriggerSkillIndex = -1;

            areas.Add(new PendingArea
            {
                Position = position,
                Direction = new float3(0f, 0f, 1f),
                Radius = zone.Radius,

                // A full circle: a patch of ground has no facing.
                ArcCosine = -1f,
                Damage = zone.Damage,
                Type = zone.Element,
                SourcePlayerId = zone.SourcePlayerId,
                Delay = 0f,

                // Everything the cast decided, carried by every pulse. A pulse
                // used to detonate nothing, mark nothing and finish nothing,
                // which made a socket full of kill-phase gems silently worth
                // nothing on the two zone skills.
                //
                // Chains are the one thing left out on purpose: see the note
                // beside the zone spawn in SkillCastSystem.
                AppliedStatus = zone.AppliedStatus,
                ExplosionRadius = zone.ExplosionRadius,
                ExplosionDamage = zone.ExplosionDamage,
                CullThreshold = zone.CullThreshold,
                ManaOnKill = zone.ManaOnKill,
                CritChance = zone.CritChance,
                CritMultiplier = zone.CritMultiplier,

                TriggerSkillIndex = trigger,
                TriggerDamageScale = zone.TriggerDamageScale,
                TriggerDepth = zone.TriggerDepth,

                // The one area effect that repeats on a timer, and therefore the
                // one that must not be announced. The disc is already burning on
                // the screen; a ring, a camera shake and a hit-stop twice a
                // second would be punctuating a sentence that never ends.
                Silent = true
            });
        }
    }
}
