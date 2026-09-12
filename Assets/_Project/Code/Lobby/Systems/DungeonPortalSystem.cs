using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Curtain;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Lobby.Systems
{
    /// <summary>
    /// The way out: collects who has agreed to descend, and once everybody has,
    /// takes the screen down and says the scene may be swapped.
    ///
    /// Three steps that are deliberately separate, because each one is a
    /// different kind of thing. Agreeing is a client request. Deciding that
    /// everybody has agreed is a host decision, and it reads the player position
    /// buffer for the roster — the same list that already answers "who is
    /// playing" for the enemies. And the moment it is safe to leave is the
    /// curtain being fully down, which is simulation state precisely so that a
    /// system can wait on it without asking a MonoBehaviour.
    ///
    /// What this never does is load a scene. That is the one part that needs
    /// Unity, and it lives in SceneLoadBridge, reading the phase this writes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DungeonPortalSystem : ISystem
    {
        /// <summary>
        /// Seconds for the fade out. Long enough to read as a departure, short
        /// enough that nobody waits — and in unscaled time, like the rest of the
        /// curtain, so a hit-stop cannot stretch it.
        /// </summary>
        private const float FadeSeconds = 0.8f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonPortal>();
            state.RequireForUpdate<PlayerPositionsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<PlayerPositionElement> players =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>(isReadOnly: true);

            foreach ((DynamicBuffer<PortalReadyRequest> requests,
                      DynamicBuffer<LobbyReadyPlayer> ready,
                      RefRW<SceneTransition> transition,
                      RefRO<DungeonPortal> portal) in
                     SystemAPI.Query<DynamicBuffer<PortalReadyRequest>,
                         DynamicBuffer<LobbyReadyPlayer>,
                         RefRW<SceneTransition>,
                         RefRO<DungeonPortal>>())
            {
                ApplyRequests(requests, ready);

                // Once the curtain is down nothing may change its mind: a player
                // un-readying while the screen is black would leave the lobby
                // dark with no way out of it.
                if (transition.ValueRO.Phase != SceneTransitionPhase.Idle)
                {
                    Advance(ref state, transition);
                    continue;
                }

                if (players.Length == 0 || ready.Length < players.Length)
                    continue;

                Begin(ref state, transition, portal.ValueRO.TargetScene);
            }
        }

        /// <summary>
        /// Folds this frame of toggles into the ready list.
        ///
        /// A list of who is ready rather than a flag per player, because the
        /// question asked of it is "are they all", and a list answers that with
        /// a length. Requests naming a player twice are idempotent, which
        /// matters because a panel is entitled to send what it sees rather than
        /// a difference.
        /// </summary>
        private static void ApplyRequests(
            DynamicBuffer<PortalReadyRequest> requests, DynamicBuffer<LobbyReadyPlayer> ready)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                PortalReadyRequest request = requests[i];
                int index = IndexOf(ready, request.PlayerId);

                if (request.Ready && index < 0)
                    ready.Add(new LobbyReadyPlayer { PlayerId = request.PlayerId });
                else if (!request.Ready && index >= 0)
                    ready.RemoveAt(index);
            }

            requests.Clear();
        }

        private void Begin(
            ref SystemState state,
            RefRW<SceneTransition> transition,
            in FixedString32Bytes target)
        {
            transition.ValueRW.Target = target;
            transition.ValueRW.Phase = SceneTransitionPhase.Fading;

            if (SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<CurtainRequest> curtain))
            {
                curtain.Add(new CurtainRequest
                {
                    Target = 1f,
                    Duration = FadeSeconds,
                    Colour = new float4(0f, 0f, 0f, 1f),
                    Reason = CurtainReason.FloorTransition
                });
            }

            UnityEngine.Debug.Log(
                $"[DungeonPortalSystem] Everyone is ready. Descending to '{target}'.");
        }

        /// <summary>
        /// Waits for the screen to be black, then hands over.
        ///
        /// Without a curtain in the scene there is nothing to wait for, and the
        /// transition goes through immediately rather than hanging — a build
        /// with no presenter has always played identically, and that has to stay
        /// true of the one thing that is allowed to wait on one.
        /// </summary>
        private void Advance(ref SystemState state, RefRW<SceneTransition> transition)
        {
            if (transition.ValueRO.Phase != SceneTransitionPhase.Fading)
                return;

            if (SystemAPI.TryGetSingleton(out CurtainState curtain) && !curtain.IsCovered)
                return;

            transition.ValueRW.Phase = SceneTransitionPhase.Ready;
        }

        private static int IndexOf(DynamicBuffer<LobbyReadyPlayer> ready, int playerId)
        {
            for (int i = 0; i < ready.Length; i++)
            {
                if (ready[i].PlayerId == playerId)
                    return i;
            }

            return -1;
        }
    }
}
