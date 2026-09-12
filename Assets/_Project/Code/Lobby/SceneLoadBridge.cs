using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TogetherWeFall.Lobby
{
    /// <summary>
    /// Loads the scene the simulation asked for, and does nothing else.
    ///
    /// The decision to leave, the agreement of every player and the moment the
    /// screen is black all happen in ECS, where a headless host reaches them at
    /// the same time as a client. Only the last step needs Unity, and this is
    /// it — one call, no logic, in the same spirit as the other bridges: it does
    /// not know why the lobby is being left, who agreed, or what is on the other
    /// side.
    ///
    /// Deliberately not part of GameBootstrap. Bootstrap wires a scene together
    /// at its start; this is about ending one.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class SceneLoadBridge : MonoBehaviour
    {
        private EntityQuery _transitionQuery;
        private bool _hasWorld;

        /// <summary>
        /// A load is not instant, and the phase stays Ready until the world is
        /// torn down with the scene. Without this the bridge would ask for the
        /// same load every frame in between.
        /// </summary>
        private bool _loading;

        public void Initialize()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(SceneLoadBridge)}] ECS world is unavailable — the portal will " +
                    "fade the screen and never load anything.", this);
                return;
            }

            _transitionQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SceneTransition>());

            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || _loading || _transitionQuery.IsEmptyIgnoreFilter)
                return;

            SceneTransition transition = _transitionQuery.GetSingleton<SceneTransition>();

            if (transition.Phase != SceneTransitionPhase.Ready)
                return;

            FixedString32Bytes target = transition.Target;

            if (target.Length == 0)
            {
                Debug.LogError(
                    $"[{nameof(SceneLoadBridge)}] The transition names no scene. Set Target " +
                    "Scene on the portal NPC, and make sure that scene is in the build " +
                    "settings.", this);

                // Once, not every frame: the phase never changes back on its own.
                _loading = true;
                return;
            }

            _loading = true;
            SceneManager.LoadScene(target.ToString());
        }

        private void OnDestroy()
        {
            if (_hasWorld)
                _transitionQuery.Dispose();
        }
    }
}
