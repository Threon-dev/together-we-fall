using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The single bridge for the player's position between the GameObject world
    /// and the ECS world.
    ///
    /// Deliberately one small file: when the project moves to networking, this
    /// is what a snapshot unpacker replaces, and nothing else has to change.
    /// That is why it contains nothing but position copies — out every frame,
    /// and back in when the host warps the body — and no gameplay logic that
    /// would have to be carried across.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerPositionPublisher : MonoBehaviour
    {
        private int _playerId;

        private EntityManager _entityManager;
        private EntityQuery _registryQuery;
        private EntityQuery _warpQuery;
        private PlayerMotor _motor;
        private uint _seenWarp;
        private bool _hasWorld;

        /// <summary>
        /// Who this GameObject is, as far as every system is concerned. Exposed
        /// so the other player-side bridges use the same answer rather than each
        /// serialising an id of their own that can silently drift.
        /// </summary>
        public int PlayerId => _playerId;

        public void Initialize(int playerId)
        {
            // The connection's NetworkId, handed over by GameBootstrap. In a
            // networked game two bodies must not both answer to the same id.
            _playerId = playerId;

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
            _warpQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<PlayerWarp>());
            _motor = GetComponent<PlayerMotor>();
            _hasWorld = true;
        }

        /// <summary>
        /// LateUpdate rather than Update: the position must be published after
        /// PlayerMotor has changed it this frame. Otherwise the enemies would
        /// chase the previous frame's position every single frame.
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasWorld)
                return;

            // In first, so the position published below is the one the host
            // just put the body at.
            ApplyWarp();

            if (_registryQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _registryQuery.GetSingletonEntity();
            DynamicBuffer<PlayerPositionElement> buffer =
                _entityManager.GetBuffer<PlayerPositionElement>(registry);

            var element = new PlayerPositionElement
            {
                PlayerId = _playerId,
                Position = (float3)transform.position,
                Facing = (float3)transform.forward,
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
            // Netcode disposes its worlds before the scene is torn down when play mode ends.
            if (!_hasWorld || World.DefaultGameObjectInjectionWorld is { IsCreated: true } == false || _registryQuery.IsEmptyIgnoreFilter)
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

        /// <summary>
        /// Moves the body where the host warped it, once per warp, and tells the
        /// motor whether the host is still holding it.
        /// </summary>
        private void ApplyWarp()
        {
            if (_motor == null || _warpQuery.IsEmptyIgnoreFilter)
                return;

            using NativeArray<PlayerCharacter> characters =
                _warpQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<PlayerWarp> warps =
                _warpQuery.ToComponentDataArray<PlayerWarp>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != _playerId)
                    continue;

                PlayerWarp warp = warps[i];
                _motor.Held = warp.Holding;

                if (warp.Version != _seenWarp)
                {
                    _seenWarp = warp.Version;
                    if (warp.SlideSeconds > 0f)
                        _motor.Slide(warp.Position, warp.Facing, warp.SlideSeconds);
                    else
                        _motor.Teleport(warp.Position, warp.Facing);
                }

                return;
            }
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
