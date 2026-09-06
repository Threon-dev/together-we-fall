using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Interaction;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The client half of everything a player asks the world to do: interacting
    /// and casting.
    ///
    /// One bridge rather than one per action, because these are the same kind of
    /// thing. Every line here says "this player pressed this, standing here,
    /// pointing there" and stops. What was in reach, whether the cooldown was
    /// up, how many projectiles come out — none of it is decided in this file.
    /// In a networked build this whole class becomes the input command stream,
    /// and the systems behind it do not notice.
    ///
    /// Reading the buttons is not done here either: devices are read in
    /// PlayerInputReader and nowhere else, so this asks that one file what the
    /// player is holding.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerActionPublisher : MonoBehaviour
    {
        private PlayerInputReader _input;
        private System.Func<bool> _uiCapturesInput;
        private EntityManager _entityManager;
        private EntityQuery _interactionQuery;
        private EntityQuery _skillEventsQuery;
        private int _playerId;
        private bool _hasWorld;

        /// <summary>
        /// The player id is passed in rather than serialised again so there is
        /// one answer to "which player is this" per GameObject, not two that can
        /// drift apart.
        /// </summary>
        /// <param name="uiCapturesInput">
        /// Asked every frame whether a panel is currently taking the player's
        /// clicks. Optional, and a delegate rather than a reference to the
        /// inventory: this file has no business knowing which panel, or that a
        /// panel is what did it. In a networked build the answer stays local —
        /// a client suppressing its own commands is the one kind of throttling
        /// that is nobody else's business.
        /// </param>
        public void Initialize(
            PlayerInputReader input, int playerId, System.Func<bool> uiCapturesInput = null)
        {
            _input = input;
            _playerId = playerId;
            _uiCapturesInput = uiCapturesInput;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(PlayerActionPublisher)}] ECS world is unavailable — interaction " +
                    "and casting are disabled.", this);
                return;
            }

            _entityManager = world.EntityManager;

            _interactionQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<InteractionRequestsSingleton>());

            _skillEventsQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<SkillEventsSingleton>());

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || _input == null)
                return;

            // Dragging an item is a left click, and so is the first skill.
            // Without this, tidying the bag empties the cooldowns too.
            if (_uiCapturesInput != null && _uiCapturesInput())
                return;

            PublishInteraction();

            // Read once and shared: the aim is the same for every slot, and
            // resolving the pointer against the ground twice a frame would be
            // two answers where there should be one.
            PlayerMoveIntent intent = _input.ReadIntent(transform.position);
            PublishCasts(intent);
        }

        private void PublishInteraction()
        {
            if (!_input.WasInteractPressed() || _interactionQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _interactionQuery.GetSingletonEntity();

            _entityManager.GetBuffer<InteractionRequest>(registry).Add(new InteractionRequest
            {
                PlayerId = _playerId,
                Position = (float3)transform.position
            });
        }

        /// <summary>
        /// One request per held button per frame.
        ///
        /// Deliberately not throttled here. A client that asks sixty times a
        /// second gets exactly as many casts as its cooldowns allow, because the
        /// cooldown lives on the host — and a client that could throttle itself
        /// could also choose not to.
        /// </summary>
        private void PublishCasts(in PlayerMoveIntent intent)
        {
            if (_skillEventsQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _skillEventsQuery.GetSingletonEntity();
            DynamicBuffer<SkillCastRequest> requests =
                _entityManager.GetBuffer<SkillCastRequest>(registry);

            for (int slot = 0; slot < _input.CastSlotCount; slot++)
            {
                if (!_input.IsCastHeld(slot))
                    continue;

                requests.Add(new SkillCastRequest
                {
                    PlayerId = _playerId,
                    SlotIndex = slot,
                    Origin = (float3)transform.position,
                    Direction = (float3)intent.AimDirection,
                    AimPoint = (float3)intent.AimPoint
                });
            }
        }
    }
}
