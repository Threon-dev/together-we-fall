using Unity.Burst;
using Unity.Entities;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// Creates the queues the combat pipeline passes work through, once, at
    /// world startup.
    ///
    /// Five buffers on one entity, and they are the pipeline: cast requests come
    /// in, hits and area effects are what the stages hand each other, trigger
    /// events are what the far end says back, and the tally is what the overlay
    /// reads. Created by a system rather than baked so
    /// they exist in every scene, including the arena, where no SubScene carries
    /// a skill database at all.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct SkillRegistrySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Entity registry = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(registry, "SkillEvents");

            state.EntityManager.AddComponent<SkillEventsSingleton>(registry);
            state.EntityManager.AddComponent<CombatTally>(registry);

            state.EntityManager.AddBuffer<SkillCastRequest>(registry);
            state.EntityManager.AddBuffer<PendingCast>(registry);
            state.EntityManager.AddBuffer<PendingHit>(registry);
            state.EntityManager.AddBuffer<PendingArea>(registry);

            // The fifth queue, and the only one that flows the other way: the
            // four above carry work forward through the pipeline, while this one
            // carries word back from its end — something died, something caught
            // fire — to the stage that decides whether that starts a new cast.
            state.EntityManager.AddBuffer<TriggerEvent>(registry);

            state.Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
}
