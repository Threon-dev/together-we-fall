using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Audio.Systems
{
    /// <summary>
    /// Creates the sound queue, and makes sure it never outlives its frame.
    ///
    /// Its own entity, beside the VFX queue rather than on it. Both are things
    /// a client derives and neither is authoritative, but they are drained by
    /// different presenters with different budgets, and a shared buffer would
    /// mean each one walking past the other's events every frame.
    ///
    /// The clearing is the part worth explaining. A presenter drains the queue
    /// in LateUpdate, so by the time this runs at the top of the next frame
    /// there is normally nothing left and the call costs nothing. When there IS
    /// something left, it means nobody was listening — no presenter in the
    /// scene, or one that failed to initialise — and the events would otherwise
    /// pile up for the length of the session.
    ///
    /// Leaving that to the presenter would make the documented one-frame
    /// lifetime a property of a MonoBehaviour being present, which is exactly
    /// the dependency the whole seam exists to avoid: a headless build produces
    /// these events too, and has nothing to draw them with.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct AudioEventRegistrySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Entity registry = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(registry, "AudioEvents");

            state.EntityManager.AddComponent<AudioEventsSingleton>(registry);
            state.EntityManager.AddBuffer<AudioEvent>(registry);

            state.RequireForUpdate<AudioEventsSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<AudioEvent> events = SystemAPI.GetSingletonBuffer<AudioEvent>();

            // Anything still here belongs to a moment that has passed, and
            // playing it late would be worse than not playing it.
            if (events.Length > 0)
                events.Clear();
        }
    }
}
