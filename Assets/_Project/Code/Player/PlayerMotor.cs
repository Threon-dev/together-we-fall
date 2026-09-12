using System;
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
    ///
    /// It is also why the aim has to be ignored while a panel is open: the mouse
    /// is picking things up in the bag, and a character that spins to follow it
    /// is following a pointer that no longer means "look there". Walking is
    /// untouched — that is still the keyboard, and it still means what it says.
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
        private Func<bool> _uiCapturing;
        private float _verticalVelocity;
        private bool _initialized;

        /// <summary>
        /// Told who reads the devices, and handed a question to ask about the
        /// panels rather than a panel to look at.
        ///
        /// The same delegate the action publisher gets, from the same place and
        /// for the same reason: the motor has no business knowing that panels
        /// exist, let alone how many. Null in a scene with none.
        /// </summary>
        public void Initialize(PlayerInputReader input, Func<bool> uiCapturing = null)
        {
            _controller = GetComponent<CharacterController>();
            _input = input;
            _uiCapturing = uiCapturing;
            _initialized = true;
        }

        /// <summary>
        /// Puts the player down on a floor position — the dungeon hands over the
        /// entrance at ground level, and the capsule has to stand on it rather
        /// than in it.
        ///
        /// The controller is switched off around the move on purpose: while it
        /// is enabled it owns the transform and quietly undoes a direct write,
        /// which looks exactly like a generator that produced the wrong
        /// entrance.
        /// </summary>
        public void WarpToFloor(Vector3 floorPosition)
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            _controller.enabled = false;

            transform.position = floorPosition +
                                 Vector3.up * (_controller.height * 0.5f + _controller.skinWidth);

            _controller.enabled = true;
            _verticalVelocity = 0f;
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

            // Not while the bag is open. The character keeps the facing they
            // had, which is also what the portrait in the panel is drawn from.
            if (_uiCapturing == null || !_uiCapturing())
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
