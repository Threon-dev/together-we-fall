using UnityEngine;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// One frame's intent — what the player asked for, not what has already
    /// happened. Keeping it a separate type from the movement itself is
    /// deliberate: once networking arrives, this is exactly what travels to the
    /// host, while the movement executes there. Both steps live in one process
    /// today, but the boundary between them is already drawn.
    ///
    /// Both vectors arrive already resolved into world space by the reader, so
    /// nothing downstream needs to know about cameras, screens or devices.
    /// </summary>
    public readonly struct PlayerMoveIntent
    {
        /// <summary>
        /// Where to walk, in world space on the ground plane. Zero when idle.
        /// </summary>
        public readonly Vector3 MoveDirection;

        /// <summary>
        /// Where to face, in world space on the ground plane. Aim is tracked
        /// separately from movement because in this genre they routinely point
        /// opposite ways — backing away while still facing the crowd is the
        /// whole reason the two are decoupled.
        /// </summary>
        public readonly Vector3 AimDirection;

        public readonly bool HasAim;

        public PlayerMoveIntent(Vector3 moveDirection, Vector3 aimDirection, bool hasAim)
        {
            MoveDirection = moveDirection;
            AimDirection = aimDirection;
            HasAim = hasAim;
        }

        public bool HasMovement => MoveDirection.sqrMagnitude > 0.0001f;

        public static PlayerMoveIntent None => new PlayerMoveIntent(Vector3.zero, Vector3.zero, false);
    }
}
