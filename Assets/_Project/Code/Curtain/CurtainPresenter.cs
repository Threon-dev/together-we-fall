using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

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
    /// A UI Toolkit element rather than a full-screen quad or a post effect:
    /// the panel already exists, a coloured rectangle is what a curtain is, and
    /// this way it covers the UI as well as the world. A shader would cover
    /// only the world, which is the wrong half.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class CurtainPresenter : MonoBehaviour
    {
        [Tooltip("Panel the curtain is drawn into. Without it the fades still " +
                 "happen in the simulation and simply cannot be seen.")]
        [SerializeField] private UIDocument _document;

        private EntityManager _entityManager;
        private EntityQuery _curtainQuery;
        private bool _hasWorld;

        private VisualElement _screen;
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
            _curtainQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<CurtainSingleton>());

            if (_document == null)
            {
                Debug.LogWarning(
                    $"[{nameof(CurtainPresenter)}] No UIDocument assigned — fades will run " +
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
                _screen.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;

                // Only when it appears. Other panels are built lazily and would
                // otherwise end up on top of a curtain that was already there.
                if (shouldShow)
                    _screen.BringToFront();
            }

            if (!shouldShow)
                return;

            float4 colour = state.Colour;
            _screen.style.backgroundColor =
                new Color(colour.x, colour.y, colour.z, state.Opacity);
        }

        /// <summary>
        /// Builds the element on first use.
        ///
        /// Lazily rather than in Initialize because a UIDocument fills its root
        /// in OnEnable, and the bootstrap that calls Initialize deliberately
        /// runs before every other component in the scene.
        /// </summary>
        private bool EnsureScreen()
        {
            if (_screen != null)
                return true;

            if (_document == null)
                return false;

            VisualElement root = _document.rootVisualElement;
            if (root == null)
                return false;

            _screen = new VisualElement();
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0f;
            _screen.style.top = 0f;
            _screen.style.right = 0f;
            _screen.style.bottom = 0f;
            _screen.style.display = DisplayStyle.None;

            // A curtain is something to look at, not something to click on.
            // Whether input should be suppressed while the screen is black is a
            // decision for whoever asked for the fade, not a side effect of it
            // being drawn.
            _screen.pickingMode = PickingMode.Ignore;

            root.Add(_screen);
            return true;
        }

        private void OnDestroy()
        {
            if (_hasWorld)
                _curtainQuery.Dispose();
        }
    }
}
