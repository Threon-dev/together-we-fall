using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using TogetherWeFall.Player;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// Draws everybody who is not this screen's player: one body per remote
    /// avatar ghost, put where the ghost is.
    ///
    /// A presenter in the same sense as VfxPresenter — it reads the client world
    /// and never writes back. The body is a copy of an inactive template the
    /// scene builder made from the same model, controller and weapon list as the
    /// local player, so a friend walks, runs and swings exactly like you do.
    /// </summary>
    public sealed class RemotePlayerPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerAnimationPresenter _bodyTemplate;

        private readonly Dictionary<int, PlayerAnimationPresenter> _bodies =
            new Dictionary<int, PlayerAnimationPresenter>();

        private readonly List<int> _gone = new List<int>();

        private EntityQuery _avatarQuery;
        private int _localPlayerId;
        private bool _hasWorld;

        public void Initialize(int localPlayerId)
        {
            _localPlayerId = localPlayerId;

            if (_bodyTemplate == null)
            {
                Debug.LogError(
                    $"[{nameof(RemotePlayerPresenter)}] No body template — other players " +
                    "will be invisible.", this);
                return;
            }

            _bodyTemplate.gameObject.SetActive(false);

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            _avatarQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerAvatar>(),
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<LocalTransform>());

            _hasWorld = true;
        }

        private void LateUpdate()
        {
            if (!_hasWorld)
                return;

            using NativeArray<GhostOwner> owners =
                _avatarQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _avatarQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            _gone.Clear();
            _gone.AddRange(_bodies.Keys);

            for (int i = 0; i < owners.Length; i++)
            {
                int playerId = owners[i].NetworkId;

                if (playerId == _localPlayerId)
                    continue;

                _gone.Remove(playerId);

                Vector3 position = transforms[i].Position;
                Quaternion rotation = transforms[i].Rotation;

                if (!_bodies.TryGetValue(playerId, out PlayerAnimationPresenter body))
                {
                    // Placed before it is switched on, so the presenter's first
                    // velocity is not a sprint from wherever the template stood.
                    body = Instantiate(_bodyTemplate, position, rotation);
                    body.name = $"RemotePlayer{playerId}";
                    body.gameObject.SetActive(true);
                    body.Initialize(playerId);
                    _bodies[playerId] = body;
                }

                body.transform.SetPositionAndRotation(position, rotation);
            }

            for (int i = 0; i < _gone.Count; i++)
            {
                Destroy(_bodies[_gone[i]].gameObject);
                _bodies.Remove(_gone[i]);
            }
        }

        private void OnDestroy()
        {
            if (_hasWorld && World.DefaultGameObjectInjectionWorld is { IsCreated: true })
                _avatarQuery.Dispose();
        }
    }
}
