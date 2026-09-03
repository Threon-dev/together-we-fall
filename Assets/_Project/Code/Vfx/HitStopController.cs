using UnityEngine;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// Nearly stops the world for a few dozen milliseconds when something lands,
    /// then lets it accelerate back.
    ///
    /// The cheapest weight in games: forty milliseconds where nothing moves, and
    /// a blast reads as having hit something rather than having been drawn over
    /// it. What sells it is the second half — snapping straight back to full
    /// speed reads as a dropped frame, while easing back reads as the world
    /// recovering from the hit. The ramp is deliberately not linear: a constant
    /// climb is just a slower snap, and the ear for this is the same one that
    /// hears a note being released rather than cut.
    ///
    /// It is punctuation, and punctuation that never stops is not punctuation.
    /// So a request is REFUSED while one is already running and for a while
    /// afterwards. An earlier version extended the freeze instead, which looked
    /// reasonable until a chain reaction delivered an explosion every frame and
    /// the world simply stayed slow.
    ///
    /// It is also the single most dangerous line of feel code in a project,
    /// because it edits a global — a hit-stop that fails to end leaves the game
    /// running at five percent speed with no obvious cause. So the scale to
    /// restore is captured on the way in and never re-captured mid-ramp,
    /// requests extend rather than stack, and Release exists to be called from
    /// OnDisable. Nothing here assumes it will be ticked to completion.
    /// </summary>
    public sealed class HitStopController
    {
        private enum Phase : byte
        {
            Idle = 0,
            Frozen = 1,
            Recovering = 2
        }

        private Phase _phase;

        private float _frozenRemaining;
        private float _recoverRemaining;
        private float _recoverDuration;

        /// <summary>The scale the ramp climbs from.</summary>
        private float _frozenScale = 1f;

        /// <summary>The scale the ramp climbs back to. Captured once, on the way in.</summary>
        private float _restoreScale = 1f;

        private float _configuredRecoverySeconds;
        private float _configuredCooldownSeconds;
        private float _cooldownRemaining;
        private AnimationCurve _recoveryCurve;

        public bool IsActive => _phase != Phase.Idle;

        /// <summary>
        /// Sets how the world comes back. Configured once rather than passed per
        /// request: the shape of the recovery is a property of the game, not of
        /// the individual explosion.
        /// </summary>
        public void Configure(float recoverySeconds, AnimationCurve curve, float cooldownSeconds)
        {
            _configuredRecoverySeconds = Mathf.Max(0f, recoverySeconds);
            _configuredCooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            _recoveryCurve = curve;
        }

        /// <summary>
        /// Freezes for a while, if it is allowed to.
        ///
        /// Refused while a freeze is running or ramping back, and refused again
        /// until the cooldown has passed. That refusal is the whole point: a
        /// fight produces impacts every frame, and the answer to "how often
        /// should the world stop" is not "as often as something is hit".
        /// </summary>
        public void Request(float seconds, float scale)
        {
            if (seconds <= 0f || IsActive || _cooldownRemaining > 0f)
                return;

            // Only on the way in from a standing start. Re-capturing here would
            // record a half-recovered scale as "normal", and every chained
            // explosion would leave the world permanently slower.
            _restoreScale = Time.timeScale;

            _frozenScale = Mathf.Clamp(scale, 0.001f, 1f);
            _frozenRemaining = seconds;
            _phase = Phase.Frozen;

            Time.timeScale = _frozenScale;
        }

        /// <summary>
        /// Counts down in UNSCALED time. Scaled time is what was just slowed
        /// down, so a freeze measured in it would take twenty times as long to
        /// end as it was asked to.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= unscaledDeltaTime;

            switch (_phase)
            {
                case Phase.Frozen:
                    TickFreeze(unscaledDeltaTime);
                    break;

                case Phase.Recovering:
                    TickRecovery(unscaledDeltaTime);
                    break;
            }
        }

        private void TickFreeze(float unscaledDeltaTime)
        {
            _frozenRemaining -= unscaledDeltaTime;
            if (_frozenRemaining > 0f)
                return;

            _frozenRemaining = 0f;

            if (_configuredRecoverySeconds <= 0f)
            {
                Release();
                return;
            }

            _recoverDuration = _configuredRecoverySeconds;
            _recoverRemaining = _recoverDuration;
            _phase = Phase.Recovering;
        }

        private void TickRecovery(float unscaledDeltaTime)
        {
            _recoverRemaining -= unscaledDeltaTime;

            if (_recoverRemaining <= 0f)
            {
                Release();
                return;
            }

            // Zero at the start of the ramp, one at the end.
            float progress = 1f - Mathf.Clamp01(_recoverRemaining / Mathf.Max(0.001f, _recoverDuration));

            Time.timeScale = Mathf.LerpUnclamped(_frozenScale, _restoreScale, Ease(progress));
        }

        /// <summary>
        /// The shape of the climb back.
        ///
        /// Falls back to smoothstep, which is what the authored curve defaults
        /// to — a config whose curve was cleared behaves like one nobody touched,
        /// rather than snapping back and looking like a bug in the freeze.
        /// </summary>
        private float Ease(float progress)
        {
            if (_recoveryCurve != null && _recoveryCurve.length > 0)
                return Mathf.Clamp01(_recoveryCurve.Evaluate(progress));

            return progress * progress * (3f - 2f * progress);
        }

        /// <summary>Puts time back the way it was, at once. Safe to call at any moment.</summary>
        public void Release()
        {
            if (_phase == Phase.Idle)
                return;

            _phase = Phase.Idle;
            _frozenRemaining = 0f;
            _recoverRemaining = 0f;

            // Measured from the end, not the start: what matters is the gap
            // between one freeze finishing and the next being allowed.
            _cooldownRemaining = _configuredCooldownSeconds;

            Time.timeScale = _restoreScale;
        }
    }
}
