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

        [Header("Feedback")]
        [Tooltip("How fast the shake wobbles. Low values read as a lurch, high " +
                 "ones as a rattle.")]
        [SerializeField] private float _shakeFrequency = 26f;

        [Tooltip("How quickly a shake dies away. Higher is snappier.")]
        [SerializeField] private float _shakeDecay = 7f;

        [SerializeField] private float _punchDecay = 6f;

        [Tooltip("Ceiling on accumulated shake. Two hundred enemies dying at once " +
                 "must not throw the camera across the room.")]
        [SerializeField] private float _maxShake = 0.8f;

        [SerializeField] private float _maxPunch = 4f;

        [Header("References")]
        [SerializeField] private Camera _camera;

        private Transform _target;
        private InputAction _zoomAction;
        /// <summary>Below this the camera is treated as being at rest.</summary>
        private const float RestingShake = 0.03f;

        private float _desiredDistance;
        private float _shake;
        private float _punch;
        private bool _initialized;

        /// <summary>
        /// PlayerInputReader needs the camera to turn clicks into world points.
        /// Exposed explicitly so that nothing has to reach for Camera.main.
        /// </summary>
        public Camera Camera => _camera;

        /// <summary>
        /// Adds a knock to the camera. Accumulates, so a hundred deaths in one
        /// frame shake harder than one — up to a ceiling, because past a point
        /// more shake is not more impact, it is just unreadable.
        ///
        /// Done here rather than through Cinemachine impulse deliberately. This
        /// rig is fifty lines of fixed-angle follow with no brain, no virtual
        /// camera and nothing to blend; adopting Cinemachine to get one impulse
        /// would replace all of it to gain a shake that is fifteen lines.
        /// </summary>
        public void AddShake(float strength)
        {
            _shake = Mathf.Min(_shake + Mathf.Max(0f, strength), _maxShake);
        }

        /// <summary>Pulls the camera back and lets it spring in. A punch, not a zoom.</summary>
        public void AddZoomPunch(float amount)
        {
            _punch = Mathf.Min(_punch + Mathf.Max(0f, amount), _maxPunch);
        }

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

            DecayFeedback();
            ApplyTransform(instant: false);
        }

        /// <summary>
        /// Unscaled time throughout, and that is the whole point: the shake
        /// exists to punctuate a hit-stop, and a shake measured in scaled time
        /// would freeze during exactly the fortieth of a second it was meant to
        /// sell.
        /// </summary>
        private void DecayFeedback()
        {
            float decay = Time.unscaledDeltaTime;

            _shake *= Mathf.Exp(-_shakeDecay * decay);
            _punch *= Mathf.Exp(-_punchDecay * decay);

            // Cut off at something perceptually zero rather than numerically
            // zero. An exponential never reaches nothing, and a few centimetres
            // of camera wobble that never quite stops is the difference between
            // a fight that settles and one that hums.
            if (_shake < RestingShake)
                _shake = 0f;

            if (_punch < RestingShake)
                _punch = 0f;
        }

        private void ApplyTransform(bool instant)
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = _target.position + _targetOffset;
            Vector3 desiredPosition = focus - rotation * Vector3.forward * (_distance + _punch);

            transform.position = instant
                ? desiredPosition
                : Vector3.Lerp(transform.position, desiredPosition, _followLerp * Time.deltaTime);

            // Added after the follow rather than before it, so the shake is not
            // smoothed away by the same lerp that smooths the following.
            transform.position += ShakeOffset();

            transform.rotation = rotation;
        }

        private Vector3 ShakeOffset()
        {
            if (_shake <= 0f)
                return Vector3.zero;

            // Perlin rather than random: noise that is continuous frame to frame
            // reads as a camera being shaken, while white noise reads as a
            // rendering fault.
            float time = Time.unscaledTime * _shakeFrequency;

            return new Vector3(
                Mathf.PerlinNoise(time, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, time) - 0.5f,
                Mathf.PerlinNoise(time, time) - 0.5f) * (_shake * 2f);
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
