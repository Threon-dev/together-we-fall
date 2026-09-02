using UnityEngine;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// Player movement on a CharacterController. Deliberately simple: at this
    /// stage the player is a moving target for the enemies, not a system with
    /// depth of its own.
    ///
    /// Facing is driven by aim, not by velocity. That decoupling is what makes
    /// strafing and backing away read correctly in a top-down action game, and
    /// it is also what a future attack system will need — you shoot where you
    /// point, not where you walk.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 6f;

        [Tooltip("Degrees per second the character turns towards the aim " +
                 "direction. High values feel responsive; instant snapping " +
                 "looks mechanical once there is an animation on top.")]
        [SerializeField] private float _turnSpeed = 900f;

        [Header("Gravity")]
        [Tooltip("Constant downward push. Without it a CharacterController " +
                 "detaches from the ground on slopes and steps.")]
        [SerializeField] private float _gravity = -20f;

        private CharacterController _controller;
        private PlayerInputReader _input;
        private float _verticalVelocity;
        private bool _initialized;

        public void Initialize(PlayerInputReader input)
        {
            _controller = GetComponent<CharacterController>();
            _input = input;
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized)
                return;

            PlayerMoveIntent intent = _input.ReadIntent(transform.position);

            Vector3 horizontal = intent.HasMovement
                ? intent.MoveDirection * _moveSpeed
                : Vector3.zero;

            ApplyGravity();

            Vector3 motion = horizontal;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            FaceAim(intent);
        }

        private void ApplyGravity()
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += _gravity * Time.deltaTime;
        }

        private void FaceAim(in PlayerMoveIntent intent)
        {
            if (!intent.HasAim || intent.AimDirection.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = Quaternion.LookRotation(intent.AimDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, _turnSpeed * Time.deltaTime);
        }
    }
}
