using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Curtain.Systems
{
    /// <summary>
    /// Owns the curtain: creates it, drains what was asked of it, and moves it.
    ///
    /// The movement is here rather than in the presenter because the number is
    /// what other systems gate on. A floor transition wants to know the screen
    /// is black before it tears the world down, and it has to be able to ask
    /// without a MonoBehaviour being present — a headless server has no curtain
    /// to look at and must still reach the same moment at the same time.
    ///
    /// Unscaled time, for the same reason the feel effects use it: a hit-stop
    /// that drops the world to five percent would otherwise turn a half-second
    /// fade into ten. A curtain is not part of the action it is covering.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct CurtainSystem : ISystem
    {
        /// <summary>Used when a request does not name one.</summary>
        private const float DefaultDuration = 0.6f;

        /// <summary>
        /// The world starts covered and immediately clears.
        ///
        /// A default rather than something the bootstrap has to remember: the
        /// first frames of a run are a dungeon being generated and a player
        /// being warped onto it, and nobody should watch that happen.
        /// </summary>
        private const float StartupFadeSeconds = 0.8f;

        public void OnCreate(ref SystemState state)
        {
            Entity curtain = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(curtain, "Curtain");

            state.EntityManager.AddComponent<CurtainSingleton>(curtain);
            state.EntityManager.AddBuffer<CurtainRequest>(curtain);

            state.EntityManager.AddComponentData(curtain, new CurtainState
            {
                Opacity = 1f,
                Target = 0f,
                Duration = StartupFadeSeconds,
                Colour = new float4(0f, 0f, 0f, 1f),
                Reason = CurtainReason.FloorTransition
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity curtain = SystemAPI.GetSingletonEntity<CurtainSingleton>();

            CurtainState curtainState = state.EntityManager.GetComponentData<CurtainState>(curtain);
            DynamicBuffer<CurtainRequest> requests =
                state.EntityManager.GetBuffer<CurtainRequest>(curtain);

            if (requests.Length > 0)
            {
                // The last one stands. Applying them in turn would mean a fade
                // that starts towards one target and ends towards another
                // within a single frame, which is the same as applying only the
                // last one but harder to explain.
                Apply(ref curtainState, requests[requests.Length - 1]);
                requests.Clear();
            }

            Advance(ref curtainState, UnityEngine.Time.unscaledDeltaTime);

            state.EntityManager.SetComponentData(curtain, curtainState);
        }

        private static void Apply(ref CurtainState state, in CurtainRequest request)
        {
            state.Target = math.saturate(request.Target);
            state.Reason = request.Reason;

            if (request.Duration > 0f)
                state.Duration = request.Duration;

            // A request that names no colour keeps the one already in use, so
            // asking for a plain fade does not need to restate what black is.
            if (request.Colour.w > 0f)
                state.Colour = request.Colour;
        }

        /// <summary>
        /// Moves the opacity towards its target at a constant rate.
        ///
        /// Linear on purpose. An eased curtain hides its own arrival, and the
        /// thing most likely to be waiting on this is a system that wants the
        /// screen covered as early as the player believes it is.
        /// </summary>
        private static void Advance(ref CurtainState state, float unscaledDelta)
        {
            if (!state.IsMoving)
            {
                state.Opacity = state.Target;
                return;
            }

            // A duration of zero would divide by nothing; treat it as instant,
            // which is what asking for a zero-second fade means.
            float step = state.Duration > 0f
                ? unscaledDelta / state.Duration
                : 1f;

            state.Opacity = math.abs(state.Target - state.Opacity) <= step
                ? state.Target
                : state.Opacity + math.sign(state.Target - state.Opacity) * step;
        }
    }
}
