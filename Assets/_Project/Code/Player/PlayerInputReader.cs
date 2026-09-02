using UnityEngine;
using UnityEngine.InputSystem;
using TogetherWeFall.CameraRig;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The single place in the project that reads input devices and uses a
    /// Camera. Everything else receives a ready-made <see cref="PlayerMoveIntent"/>
    /// already expressed in world space. This is the same boundary the project
    /// conventions require of the enemy ECS systems: they must know nothing
    /// about keyboards, pointers or cameras.
    ///
    /// Actions are built in code rather than loaded from an .inputactions asset.
    /// For a prototype that trades a binding UI for the guarantee that input
    /// works the moment the scene runs, with no asset to wire up or keep in sync.
    /// Because every action is created in one method here, moving to an asset
    /// later is a change to this file alone.
    ///
    /// Each action carries bindings for several device families, so a new
    /// platform is a binding line rather than a code path.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("Height of the ground plane the pointer is projected onto. The " +
                 "arena is flat, so this is solved analytically — no colliders, " +
                 "no layers, and nothing to hit but the ground.")]
        [SerializeField] private float _groundHeight = 0f;

        [Tooltip("Below this magnitude a stick is treated as centred. Without a " +
                 "dead zone a resting stick would fight the pointer for control " +
                 "of the aim every frame.")]
        [SerializeField, Range(0.05f, 0.9f)] private float _aimStickDeadZone = 0.25f;

        private TopDownCameraRig _cameraRig;

        private InputAction _moveAction;
        private InputAction _pointerAction;
        private InputAction _aimStickAction;

        private Vector3 _lastAimDirection = Vector3.forward;
        private bool _initialized;

        public void Initialize(TopDownCameraRig cameraRig)
        {
            _cameraRig = cameraRig;

            CreateActions();
            EnableActions();

            _initialized = true;
        }

        private void CreateActions()
        {
            _moveAction = new InputAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            // Pointer rather than Mouse: the same binding covers mouse, pen and
            // touch, which is most of what "another platform" means here.
            _pointerAction = new InputAction("Point", InputActionType.Value, "<Pointer>/position");

            _aimStickAction = new InputAction("AimStick", InputActionType.Value);
            _aimStickAction.AddBinding("<Gamepad>/rightStick");
        }

        private void EnableActions()
        {
            _moveAction.Enable();
            _pointerAction.Enable();
            _aimStickAction.Enable();
        }

        /// <summary>
        /// Reads this frame's input. Called from PlayerMotor inside Update so
        /// that the "read intent, then apply movement" order is explicit rather
        /// than dependent on MonoBehaviour execution order in the scene.
        /// </summary>
        public PlayerMoveIntent ReadIntent(Vector3 playerPosition)
        {
            if (!_initialized)
                return PlayerMoveIntent.None;

            Vector3 move = ReadMoveDirection();
            bool hasAim = TryReadAimDirection(playerPosition, out Vector3 aim);

            return new PlayerMoveIntent(move, aim, hasAim);
        }

        private Vector3 ReadMoveDirection()
        {
            Vector2 raw = _moveAction.ReadValue<Vector2>();

            if (raw.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            // Camera space, so W leads into the screen rather than along world Z.
            Vector3 direction =
                _cameraRig.GroundForward * raw.y +
                _cameraRig.GroundRight * raw.x;

            return direction.normalized;
        }

        private bool TryReadAimDirection(Vector3 playerPosition, out Vector3 direction)
        {
            // A deflected stick wins over the pointer: on a gamepad the pointer
            // is stale rather than absent, and letting it through would snap
            // aim back to wherever the mouse was last left.
            Vector2 stick = _aimStickAction.ReadValue<Vector2>();
            if (stick.sqrMagnitude >= _aimStickDeadZone * _aimStickDeadZone)
            {
                direction = (_cameraRig.GroundForward * stick.y +
                             _cameraRig.GroundRight * stick.x).normalized;
                _lastAimDirection = direction;
                return true;
            }

            if (TryGetGroundPointUnderPointer(out Vector3 point))
            {
                Vector3 toPoint = point - playerPosition;
                toPoint.y = 0f;

                // Right on top of the character the direction is noise, so hold
                // the previous facing instead of spinning wildly.
                if (toPoint.sqrMagnitude > 0.01f)
                {
                    _lastAimDirection = toPoint.normalized;
                    direction = _lastAimDirection;
                    return true;
                }
            }

            direction = _lastAimDirection;
            return true;
        }

        private bool TryGetGroundPointUnderPointer(out Vector3 point)
        {
            point = default;

            Camera camera = _cameraRig.Camera;
            if (camera == null)
                return false;

            Vector2 screenPosition = _pointerAction.ReadValue<Vector2>();
            Ray ray = camera.ScreenPointToRay(screenPosition);
            var ground = new Plane(Vector3.up, new Vector3(0f, _groundHeight, 0f));

            if (!ground.Raycast(ray, out float distance))
                return false;

            point = ray.GetPoint(distance);
            return true;
        }

        private void OnDestroy()
        {
            // Actions built in code own native resources and are not collected
            // with the component.
            _moveAction?.Dispose();
            _pointerAction?.Dispose();
            _aimStickAction?.Dispose();
        }
    }
}
