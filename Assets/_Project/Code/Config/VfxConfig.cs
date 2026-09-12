using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// How hard the game hits back when a crowd dies.
    ///
    /// Every number here is a feel number, and feel numbers are the ones most
    /// worth tuning with a slider while playing rather than by recompiling.
    /// They are also the ones most easily overdone: the defaults are chosen to
    /// be noticeable and not to make anyone motion sick, and the ranges are
    /// deliberately narrow.
    /// </summary>
    [CreateAssetMenu(
        fileName = "VfxConfig",
        menuName = "Together We Fall/VFX Config")]
    public sealed class VfxConfig : ScriptableObject
    {
        [Header("Chain lines")]
        [Tooltip("How long a chain link stays on screen. Roughly the jump delay " +
                 "of the skill, so one link is visible until the next appears.")]
        [SerializeField, Range(0.03f, 0.6f)] private float _chainLinkSeconds = 0.12f;

        [SerializeField, Range(0.02f, 0.5f)] private float _chainLinkWidth = 0.16f;

        [Tooltip("The opening stroke, from the caster to the first target. " +
                 "Heavier than the jumps it sets off, because it is the part the " +
                 "player aimed.")]
        [SerializeField, Range(0.02f, 0.8f)] private float _boltStrikeWidth = 0.3f;

        [SerializeField, Range(0.03f, 0.8f)] private float _boltStrikeSeconds = 0.18f;

        [Header("Explosion ring")]
        [Tooltip("How long the shockwave ring takes to reach the blast radius.")]
        [SerializeField, Range(0.05f, 0.8f)] private float _explosionSeconds = 0.22f;

        [SerializeField, Range(0.02f, 0.6f)] private float _explosionWidth = 0.22f;

        [Header("Damage numbers")]
        [Tooltip("How long a number stays up. Long enough to read, short enough " +
                 "that a crowd does not become a wall of text.")]
        [SerializeField, Range(0.2f, 2f)] private float _damageNumberSeconds = 0.75f;

        [Tooltip("How far it drifts upward over its life, in world units.")]
        [SerializeField, Range(0f, 4f)] private float _damageNumberRise = 1.6f;

        [SerializeField, Range(8, 48)] private int _damageNumberFontSize = 18;

        [Tooltip("Size multiplier for the blow that finished something off.")]
        [SerializeField, Range(1f, 3f)] private float _damageNumberKillScale = 1.5f;

        [Tooltip("Numbers kept alive and reused. Past this the oldest is taken " +
                 "back, because a crowd dying is exactly when allocating hurts.")]
        [SerializeField, Range(8, 256)] private int _damageNumberPoolSize = 64;

        [Header("Hit stop")]
        [Tooltip("How long the world nearly stops when a blast lands. Thirty to " +
                 "sixty milliseconds is the window where it reads as weight " +
                 "rather than as a stutter.")]
        [SerializeField, Range(0f, 0.15f)] private float _hitStopSeconds = 0.045f;

        [Tooltip("Time scale during the freeze. Not zero: at zero every system " +
                 "that divides by delta time has to grow a special case.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _hitStopScale = 0.05f;

        [Tooltip("How long the world takes to climb back to full speed after the " +
                 "freeze. Snapping back reads as a dropped frame; easing back " +
                 "reads as the world recovering from the hit.")]
        [SerializeField, Range(0f, 0.6f)] private float _hitStopRecoverySeconds = 0.18f;

        [Tooltip("The shape of that climb, from the frozen scale to normal. " +
                 "Deliberately a curve and not a number: a straight line is just " +
                 "a slower snap, and which curve feels right is decided by " +
                 "playing rather than by reasoning.")]
        [SerializeField]
        private AnimationCurve _hitStopRecoveryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Quiet period after a freeze ends before another may start. " +
                 "A fight produces impacts every frame; without this the world " +
                 "simply stays slow.")]
        [SerializeField, Range(0f, 2f)] private float _hitStopCooldownSeconds = 0.8f;

        [Header("Camera")]
        [SerializeField, Range(0f, 1f)] private float _explosionShake = 0.35f;

        [Tooltip("Minimum gap between camera knocks. Inside the gap only a " +
                 "distinctly bigger one gets through, so a chain reaction reads " +
                 "as a series of punches rather than as a rumble.")]
        [SerializeField, Range(0f, 1f)] private float _shakeIntervalSeconds = 0.35f;

        [Tooltip("Shake from a single death. Small — it is multiplied by however " +
                 "many died this frame.")]
        [SerializeField, Range(0f, 0.2f)] private float _deathShake = 0.02f;

        [Header("Mass kill")]
        [Tooltip("Deaths inside the window that count as a moment worth marking.")]
        [SerializeField, Range(2, 50)] private int _massKillThreshold = 6;

        [SerializeField, Range(0.1f, 2f)] private float _massKillWindowSeconds = 0.5f;

        [Tooltip("How far the camera pulls back and springs in. A punch, not a zoom.")]
        [SerializeField, Range(0f, 4f)] private float _massKillZoomPunch = 1.4f;

        [Tooltip("Quiet period after a mass kill fires, so a long fight does not " +
                 "punch the camera every frame.")]
        [SerializeField, Range(0.1f, 3f)] private float _massKillCooldownSeconds = 0.8f;

        [Header("Status markers")]
        [Tooltip("How many afflicted bodies carry a marker at once. A budget " +
                 "rather than a pool size: a hundred burning enemies are a " +
                 "hundred markers nobody can read, so the nearest ones get one " +
                 "and the rest are simply coloured.")]
        [SerializeField, Range(0, 64)] private int _statusIconBudget = 16;

        [Tooltip("How far from the camera rig a body still gets a marker. The " +
                 "same cut-off idea as the audio distance: something nobody can " +
                 "read should not be holding a label the near fight needs.")]
        [SerializeField, Range(5f, 80f)] private float _statusIconRange = 30f;

        [SerializeField, Range(6, 32)] private int _statusIconFontSize = 11;

        [Header("Pooling")]
        [Tooltip("Line renderers kept alive and reused. Effects that fire dozens " +
                 "of times a second must never allocate.")]
        [SerializeField, Range(8, 512)] private int _poolSize = 96;

        [Header("Authored effects")]
        [Tooltip("How many copies of ONE particle prefab may be alive at once. " +
                 "Past it the oldest one-shot is taken back rather than a new " +
                 "one made — a trail on a projectile is never stolen. Per " +
                 "prefab, so importing a pack of forty does not multiply this.")]
        [SerializeField, Range(2, 64)] private int _particlesPerEffect = 16;

        [Tooltip("How long a trail is left to fade after the thing carrying it " +
                 "is gone. It stops emitting immediately; this is only how long " +
                 "what is already in the air has to die out.")]
        [SerializeField, Range(0.05f, 3f)] private float _particleFadeSeconds = 0.6f;

        [Tooltip("Ceiling on a one-shot's own duration. A looping prefab " +
                 "answers with its loop and would otherwise never come back, so " +
                 "this is the number that makes a mistake in the pack survivable.")]
        [SerializeField, Range(0.2f, 12f)] private float _particleMaxSeconds = 4f;

        public int StatusIconBudget => _statusIconBudget;
        public float StatusIconRange => _statusIconRange;
        public int StatusIconFontSize => _statusIconFontSize;

        public float ChainLinkSeconds => _chainLinkSeconds;
        public float ChainLinkWidth => _chainLinkWidth;
        public float BoltStrikeWidth => _boltStrikeWidth;
        public float BoltStrikeSeconds => _boltStrikeSeconds;

        public float DamageNumberSeconds => _damageNumberSeconds;
        public float DamageNumberRise => _damageNumberRise;
        public int DamageNumberFontSize => _damageNumberFontSize;
        public float DamageNumberKillScale => _damageNumberKillScale;
        public int DamageNumberPoolSize => _damageNumberPoolSize;

        public float ExplosionSeconds => _explosionSeconds;
        public float ExplosionWidth => _explosionWidth;

        public float HitStopSeconds => _hitStopSeconds;
        public float HitStopScale => _hitStopScale;
        public float HitStopRecoverySeconds => _hitStopRecoverySeconds;
        public AnimationCurve HitStopRecoveryCurve => _hitStopRecoveryCurve;
        public float HitStopCooldownSeconds => _hitStopCooldownSeconds;

        public float ExplosionShake => _explosionShake;
        public float ShakeIntervalSeconds => _shakeIntervalSeconds;
        public float DeathShake => _deathShake;

        public int MassKillThreshold => _massKillThreshold;
        public float MassKillWindowSeconds => _massKillWindowSeconds;
        public float MassKillZoomPunch => _massKillZoomPunch;
        public float MassKillCooldownSeconds => _massKillCooldownSeconds;

        public int PoolSize => _poolSize;

        public int ParticlesPerEffect => _particlesPerEffect;
        public float ParticleFadeSeconds => _particleFadeSeconds;
        public float ParticleMaxSeconds => _particleMaxSeconds;
    }
}
