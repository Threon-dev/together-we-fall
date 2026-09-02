using UnityEngine;
using UnityEngine.InputSystem;

namespace TogetherWeFall.CameraRig
{
    /// <summary>
    /// Top-down camera in the PoE2 style: fixed tilt and azimuth, follows a
    /// target, zooms with the wheel. Orbiting around the player is deliberately
    /// absent — a fixed yaw lets the player memorise on-screen directions (WASD
    /// always leads the same way) and keeps the camera purely local, which
    /// matters once replication arrives.
    /// </summary>
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        [Header("Viewing angle")]
        [SerializeField, Range(30f, 80f)] private float _pitch = 55f;
        [SerializeField, Range(0f, 360f)] private float _yaw = 45f;

        [Header("Distance / zoom")]
        [SerializeField] private float _distance = 18f;
        [SerializeField] private Vector2 _distanceRange = new Vector2(8f, 35f);
        [SerializeField] private float _zoomStep = 2f;
        [SerializeField] private float _zoomLerp = 10f;

        [Header("Following")]
        [SerializeField] private Vector3 _targetOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float _followLerp = 12f;

        [Header("References")]
        [SerializeField] private Camera _camera;

        private Transform _target;
        private InputAction _zoomAction;
        private float _desiredDistance;
        private bool _initialized;

        /// <summary>
        /// PlayerInputReader needs the camera to turn clicks into world points.
        /// Exposed explicitly so that nothing has to reach for Camera.main.
        /// </summary>
        public Camera Camera => _camera;

        public void Initialize(Transform target)
        {
            _target = target;

            // The camera owns its own zoom action instead of routing it through
            // PlayerInputReader: zoom is a view preference, not a gameplay
            // intent, and it must never end up in anything that is later sent to
            // a server.
            _zoomAction = new InputAction("Zoom", InputActionType.Value);
            _zoomAction.AddBinding("<Mouse>/scroll/y");
            _zoomAction.AddBinding("<Gamepad>/dpad/y");
            _zoomAction.Enable();

            _desiredDistance = Mathf.Clamp(_distance, _distanceRange.x, _distanceRange.y);
            _distance = _desiredDistance;
            _initialized = true;

            // Snap into place immediately, otherwise the first frame shows a
            // jump from the authored scene position to the computed one.
            ApplyTransform(instant: true);
        }

        private void LateUpdate()
        {
            if (!_initialized || _target == null)
                return;

            // Mouse scroll arrives in notches of 120 on Windows, so it is
            // normalised to a step count rather than used as a raw magnitude.
            float scroll = Mathf.Clamp(_zoomAction.ReadValue<float>(), -1f, 1f);
            if (!Mathf.Approximately(scroll, 0f))
            {
                _desiredDistance = Mathf.Clamp(
                    _desiredDistance - scroll * _zoomStep,
                    _distanceRange.x,
                    _distanceRange.y);
            }

            _distance = Mathf.Lerp(_distance, _desiredDistance, _zoomLerp * Time.deltaTime);
            ApplyTransform(instant: false);
        }

        private void ApplyTransform(bool instant)
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = _target.position + _targetOffset;
            Vector3 desiredPosition = focus - rotation * Vector3.forward * _distance;

            transform.position = instant
                ? desiredPosition
                : Vector3.Lerp(transform.position, desiredPosition, _followLerp * Time.deltaTime);

            transform.rotation = rotation;
        }

        /// <summary>
        /// Ground-plane "forward" from the camera's point of view. Needed by
        /// WASD so that W leads away from the camera rather than along world Z.
        /// </summary>
        public Vector3 GroundForward
        {
            get
            {
                Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                return forward.normalized;
            }
        }

        public Vector3 GroundRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        private void OnDestroy()
        {
            _zoomAction?.Dispose();
        }
    }
}
