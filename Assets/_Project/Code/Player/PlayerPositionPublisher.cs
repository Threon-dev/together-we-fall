using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The single bridge from the GameObject world into the ECS world.
    ///
    /// Deliberately one small file: when the project moves to networking, this
    /// is what a snapshot unpacker replaces, and nothing else has to change.
    /// That is why it contains nothing but a position copy — no gameplay logic
    /// that would have to be carried across.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerPositionPublisher : MonoBehaviour
    {
        [Tooltip("Player identifier. Always 0 in a local game; in a networked " +
                 "one this comes from the connection id.")]
        [SerializeField] private int _playerId;

        private EntityManager _entityManager;
        private EntityQuery _registryQuery;
        private bool _hasWorld;

        public void Initialize()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(PlayerPositionPublisher)}] ECS world is unavailable — " +
                    "the player position will not be published.", this);
                return;
            }

            _entityManager = world.EntityManager;
            _registryQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerPositionsSingleton>());
            _hasWorld = true;
        }

        /// <summary>
        /// LateUpdate rather than Update: the position must be published after
        /// PlayerMotor has changed it this frame. Otherwise the enemies would
        /// chase the previous frame's position every single frame.
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasWorld || _registryQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _registryQuery.GetSingletonEntity();
            DynamicBuffer<PlayerPositionElement> buffer =
                _entityManager.GetBuffer<PlayerPositionElement>(registry);

            var element = new PlayerPositionElement
            {
                PlayerId = _playerId,
                Position = (float3)transform.position,
                IsTargetable = true
            };

            int index = FindIndex(buffer, _playerId);
            if (index >= 0)
                buffer[index] = element;
            else
                buffer.Add(element);
        }

        private void OnDisable()
        {
            // The player left the scene — drop them out of targeting, but keep
            // the element in the buffer so other indices do not shift.
            if (!_hasWorld || _registryQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _registryQuery.GetSingletonEntity();
            DynamicBuffer<PlayerPositionElement> buffer =
                _entityManager.GetBuffer<PlayerPositionElement>(registry);

            int index = FindIndex(buffer, _playerId);
            if (index < 0)
                return;

            PlayerPositionElement element = buffer[index];
            element.IsTargetable = false;
            buffer[index] = element;
        }

        private static int FindIndex(DynamicBuffer<PlayerPositionElement> buffer, int playerId)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].PlayerId == playerId)
                    return i;
            }

            return -1;
        }
    }
}
