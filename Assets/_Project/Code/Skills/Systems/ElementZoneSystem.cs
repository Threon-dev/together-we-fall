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

                // A pulse detonates nothing and casts nothing. Zero is a real
                // skill index, so the trigger is said out loud rather than left
                // at its default.
                ExplosionRadius = 0f,
                ExplosionDamage = 0f,
                TriggerSkillIndex = -1,

                // The one area effect that repeats on a timer, and therefore the
                // one that must not be announced. The disc is already burning on
                // the screen; a ring, a camera shake and a hit-stop twice a
                // second would be punctuating a sentence that never ends.
                Silent = true
            });
        }
    }
}
