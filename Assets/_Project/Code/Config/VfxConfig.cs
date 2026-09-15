using System;
using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// What one element looks like where no skill authored the look: the beam of
    /// a chain jump, the burst where two elements met, and a blast nobody owns.
    ///
    /// Per element rather than one of each, because these carried the element's
    /// colour when they were lines and rings — and a pack's prefab cannot be
    /// tinted without flattening it, so the colour became a choice of prefab.
    /// </summary>
    [Serializable]
    public sealed class ElementVfxSettings
    {
        [SerializeField] private DamageType _element = DamageType.Physical;

        [Tooltip("A line from one body to the next — a chain jump, a bolt's first " +
                 "stroke, a heal reaching an ally. Needs a LineRenderer: both ends " +
                 "are set on every use.")]
        [SerializeField] private GameObject _beam;

        [Tooltip("Where two elements met: a reaction, or a shot picking one up " +
                 "from a zone. Scaled by the reaction's radius.")]
        [SerializeField] private GameObject _reaction;

        [Tooltip("A blast no skill authored a look for — a corpse going off. A " +
                 "skill's own blast is drawn by its visual set instead. Scaled by " +
                 "the blast radius.")]
        [SerializeField] private GameObject _blast;

        public DamageType Element => _element;
        public GameObject Beam => _beam;
        public GameObject Reaction => _reaction;
        public GameObject Blast => _blast;
    }

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
        [Header("Chain beams")]
        [Tooltip("How long a chain link stays on screen. Roughly the jump delay " +
                 "of the skill, so one link is visible until the next appears.")]
        [SerializeField, Range(0.03f, 0.6f)] private float _chainLinkSeconds = 0.12f;

        [Tooltip("Multiplier on the beam prefab's own width.")]
        [SerializeField, Range(0.1f, 3f)] private float _chainLinkWidth = 0.5f;

        [Tooltip("The opening stroke, from the caster to the first target. " +
                 "Heavier than the jumps it sets off, because it is the part the " +
                 "player aimed. A multiplier on the beam's own width.")]
        [SerializeField, Range(0.1f, 3f)] private float _boltStrikeWidth = 0.8f;

        [SerializeField, Range(0.03f, 0.8f)] private float _boltStrikeSeconds = 0.18f;

        [Header("Element effects")]
        [Tooltip("One row per element. An element with no row, or a row with an " +
                 "empty slot, simply draws nothing there.")]
        [SerializeField] private ElementVfxSettings[] _elements = Array.Empty<ElementVfxSettings>();

        [Tooltip("Prefab scale per metre of radius, for reactions and blasts. " +
                 "The pack's explosions are about a metre across, so a blast of " +
                 "radius three wants a scale of about 2.4.")]
        [SerializeField, Range(0.1f, 3f)] private float _blastScalePerMetre = 0.8f;

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

        [Header("Pooled prefabs")]
        [Tooltip("How many copies of ONE prefab may be alive at once. Past it the " +
                 "oldest one-shot is taken back rather than a new one made — a " +
                 "trail on a projectile is never stolen. Per prefab, so importing " +
                 "a pack of forty does not multiply this.")]
        [SerializeField, Range(2, 64)] private int _particlesPerEffect = 16;

        [Tooltip("The least time a trail is left to fade after the thing carrying " +
                 "it is gone. The body vanishes at once; the trail gets this or " +
                 "its own lifetime, whichever is longer, capped by the ceiling below.")]
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

        public float BlastScalePerMetre => _blastScalePerMetre;

        public float DamageNumberSeconds => _damageNumberSeconds;
        public float DamageNumberRise => _damageNumberRise;
        public int DamageNumberFontSize => _damageNumberFontSize;
        public float DamageNumberKillScale => _damageNumberKillScale;
        public int DamageNumberPoolSize => _damageNumberPoolSize;

        public float ExplosionShake => _explosionShake;
        public float ShakeIntervalSeconds => _shakeIntervalSeconds;
        public float DeathShake => _deathShake;

        public int MassKillThreshold => _massKillThreshold;
        public float MassKillWindowSeconds => _massKillWindowSeconds;
        public float MassKillZoomPunch => _massKillZoomPunch;
        public float MassKillCooldownSeconds => _massKillCooldownSeconds;

        public int ParticlesPerEffect => _particlesPerEffect;
        public float ParticleFadeSeconds => _particleFadeSeconds;
        public float ParticleMaxSeconds => _particleMaxSeconds;

        /// <summary>
        /// The element rows as a lookup, indexed by the enum value. Built once at
        /// startup, like the audio cue table; a missing element comes back null
        /// and draws nothing.
        /// </summary>
        public ElementVfxSettings[] BuildElementIndex()
        {
            int highest = 0;

            foreach (DamageType value in Enum.GetValues(typeof(DamageType)))
                highest = Mathf.Max(highest, (int)value);

            var index = new ElementVfxSettings[highest + 1];

            if (_elements == null)
                return index;

            for (int i = 0; i < _elements.Length; i++)
            {
                if (_elements[i] == null)
                    continue;

                int slot = (int)_elements[i].Element;

                if (slot < 0 || slot >= index.Length)
                    continue;

                // First wins, and says so — the same rule as the audio cues.
                if (index[slot] != null)
                {
                    Debug.LogWarning(
                        $"[{nameof(VfxConfig)}] {_elements[i].Element} is listed twice — the second " +
                        "row is ignored.", this);
                    continue;
                }

                index[slot] = _elements[i];
            }

            return index;
        }
    }
}
