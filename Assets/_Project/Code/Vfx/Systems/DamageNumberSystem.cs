using Unity.Entities;
using Unity.Transforms;
using TogetherWeFall.Combat;
using TogetherWeFall.Combat.Systems;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Vfx.Systems
{
    /// <summary>
    /// Turns the damage an entity took this frame into something to draw.
    ///
    /// It exists as its own pass rather than as two lines inside the resolver
    /// for one reason: the resolver runs in parallel, and appending to a shared
    /// queue from a parallel job is the kind of thing that works until two
    /// enemies die on different threads. So the resolver leaves a flag on each
    /// entity, and this walks the ones that were actually hurt.
    ///
    /// That flag is also what does the batching. Five projectiles landing on one
    /// enemy in one frame are five damage events, one flag, and one number —
    /// which is the difference between a figure the player can read and five of
    /// them stacked on the same pixel.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageResolutionSystem))]
    public partial struct DamageNumberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VfxEventsSingleton>();

            // DamageFeedback is enableable, so this only runs on frames where
            // something was actually hurt.
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<DamageFeedback, LocalTransform>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<VfxEvent> events = SystemAPI.GetSingletonBuffer<VfxEvent>();

            // No WithPresent: this wants only the entities whose flag is up,
            // which is the whole point of the flag.
            foreach ((RefRO<DamageFeedback> feedback,
                      RefRO<LocalTransform> transform,
                      EnabledRefRW<DamageFeedback> raised) in
                     SystemAPI.Query<RefRO<DamageFeedback>, RefRO<LocalTransform>,
                         EnabledRefRW<DamageFeedback>>())
            {
                raised.ValueRW = false;

                events.Add(new VfxEvent
                {
                    Kind = VfxEventKind.DamageNumber,
                    Position = transform.ValueRO.Position,
                    Color = DamageTypePalette.For(feedback.ValueRO.Type),
                    Magnitude = feedback.ValueRO.Amount,
                    Emphasis = feedback.ValueRO.Killing
                });
            }
        }
    }
}
