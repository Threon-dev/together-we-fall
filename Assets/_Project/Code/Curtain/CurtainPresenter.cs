using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TogetherWeFall.UI;

namespace TogetherWeFall.Curtain
{
    /// <summary>
    /// Paints the curtain the simulation is holding.
    ///
    /// A bridge out of ECS: it reads the opacity and colour and puts them on a
    /// full-screen element. It never writes the opacity back, which is what
    /// makes a build without this component play identically — every system
    /// that waits on a fade is waiting on a number that moves with or without
    /// anything to look at.
    ///
    /// Request is the one thing that goes the other way, and it appends to the
    /// request queue rather than setting the state — the same shape as the
    /// inventory panel appending an EquipRequest. Asking is not deciding.
    ///
    /// A canvas image rather than a full-screen quad or a post effect: the
    /// canvas already exists, a coloured rectangle is what a curtain is, and
    /// this way it covers the UI as well as the world. A shader would cover
    /// only the world, which is the wrong half.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class CurtainPresenter : MonoBehaviour
    {
        [Tooltip("Canvas the curtain is drawn on. Without it the fades still " +
                 "happen in the simulation and simply cannot be seen.")]
        [SerializeField] private Canvas _canvas;

        private EntityManager _entityManager;
        private EntityQuery _curtainQuery;
        private bool _hasWorld;

        private Image _screen;
        private bool _visible;

        public void Initialize()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(CurtainPresenter)}] ECS world is unavailable — the screen will " +
                    "never fade.", this);
                return;
            }

            _entityManager = world.EntityManager;
            // Both, and CurtainState is the one that matters: GetSingleton<T>
            // only reaches components the query actually declares, so a query
            // built on the marker alone finds the entity and then refuses to
            // read anything off it.
            _curtainQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CurtainSingleton>(),
                ComponentType.ReadOnly<CurtainState>());

            if (_canvas == null)
            {
                Debug.LogWarning(
                    $"[{nameof(CurtainPresenter)}] No Canvas assigned — fades will run " +
                    "unseen. Everything else is unaffected.", this);
            }

            _hasWorld = true;
        }

        /// <summary>
        /// Asks for the screen to be covered or cleared.
        ///
        /// For callers that live outside ECS. A system with access to the
        /// singleton should append to the buffer itself rather than reaching
        /// through a MonoBehaviour to do it.
        /// </summary>
        public void Request(float target, float duration, CurtainReason reason)
        {
            if (!_hasWorld || _curtainQuery.IsEmptyIgnoreFilter)
                return;

            Entity curtain = _curtainQuery.GetSingletonEntity();

            _entityManager.GetBuffer<CurtainRequest>(curtain).Add(new CurtainRequest
            {
                Target = target,
                Duration = duration,
                Reason = reason
            });
        }

        /// <summary>
        /// LateUpdate, so the opacity painted is the one this frame's simulation
        /// arrived at rather than last frame's.
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasWorld || _curtainQuery.IsEmptyIgnoreFilter || !EnsureScreen())
                return;

            CurtainState state = _curtainQuery.GetSingleton<CurtainState>();

            bool shouldShow = state.Opacity > 0.001f;

            if (shouldShow != _visible)
            {
                _visible = shouldShow;

                // The object rather than the component, so a hidden curtain
                // costs the canvas nothing at all. There is no bring-to-front
                // any more: the curtain owns its own canvas and that canvas
                // sorts above every other one, which is a thing said once at
                // build time instead of every time it appears.
                _screen.gameObject.SetActive(shouldShow);
            }

            if (!shouldShow)
                return;

            float4 colour = state.Colour;
            _screen.color = new Color(colour.x, colour.y, colour.z, state.Opacity);
        }

        /// <summary>
        /// Builds the image on first use.
        ///
        /// A canvas has its rect from the moment it exists, so this no longer
        /// has to wait the way it did under UI Toolkit — but it still builds on
        /// first paint rather than in Initialize, because that is one less order
        /// to depend on and the bootstrap calls Initialize before every other
        /// component in the scene.
        ///
        /// A curtain is something to look at, not something to click on. Nothing
        /// here turns raycasts off, because the canvas it is built on has no
        /// raycaster at all — see the scene builder. Whether input should be
        /// suppressed while the screen is black is a decision for whoever asked
        /// for the fade, not a side effect of it being drawn.
        /// </summary>
        private bool EnsureScreen()
        {
            if (_screen != null)
                return true;

            if (_canvas == null)
                return false;

            _screen = Ugui.Box(_canvas.transform, "Curtain", Color.clear);
            _screen.gameObject.SetActive(false);

            return true;
        }

        private void OnDestroy()
        {
            // Netcode disposes its worlds before the scene is torn down when play mode ends.
            if (_hasWorld && World.DefaultGameObjectInjectionWorld is { IsCreated: true })
                _curtainQuery.Dispose();
        }
    }
}
