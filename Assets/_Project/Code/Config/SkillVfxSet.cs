using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// What one skill looks like: the flash where it is cast, the thing that
    /// flies, and what happens where it lands.
    ///
    /// Three prefabs rather than three fields on SkillDefinition, and one asset
    /// per skill rather than a table somewhere central: a visual set is the
    /// thing a designer swaps twenty times while looking for the right fireball,
    /// and swapping it should not mean editing the skill's damage in the same
    /// inspector. Any of the three may be empty — a melee swing has no
    /// projectile, and a skill with no set at all draws exactly what it drew
    /// before this existed.
    ///
    /// The prefabs are ordinary GameObjects with particle systems on them,
    /// because that is what the packs ship. They never reach a system: the
    /// simulation carries the id of the set and a presenter does the looking up,
    /// which is the same seam every other effect in this game crosses.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SkillVfxSet",
        menuName = "Together We Fall/Skill VFX Set")]
    public sealed class SkillVfxSet : ScriptableObject
    {
        [Tooltip("Played once where the skill goes off — a muzzle flash, a nova, " +
                 "the swing itself. For an area skill this is the effect: the " +
                 "blast has no projectile and needs nothing else.")]
        [SerializeField] private GameObject _cast;

        [Tooltip("Follows the projectile for as long as it flies. This is where " +
                 "a trail belongs: it is one instance moving, not an effect " +
                 "replayed per frame. Empty for anything that does not fly.")]
        [SerializeField] private GameObject _projectile;

        [Tooltip("Played at the point of impact, once per blow — so a fork hits " +
                 "twice and a chain plays once per jump.")]
        [SerializeField] private GameObject _hit;

        [Tooltip("Scale for all three, for a pack authored at a different size " +
                 "than this game's metre. One is the prefab as it is.")]
        [SerializeField, Range(0.1f, 8f)] private float _scale = 1f;

        [Tooltip("Raises the cast and hit effects off the point the simulation " +
                 "named. Zero is exactly there, which is right for a projectile " +
                 "impact: it happens at the height the bolt was flying. Bodies " +
                 "stand on the floor, though, so an effect that should land on " +
                 "a chest — a nova's hit, a chain's jump — wants about 0.9. " +
                 "It is also the knob for a prefab whose pivot is not where its " +
                 "effect is, which is most of what differs between packs.")]
        [SerializeField, Range(-1f, 3f)] private float _lift;

        [Tooltip("Degrees the cast effect turns about the vertical, on top of the " +
                 "way the cast was pointing. Zero for an effect authored along +Z; " +
                 "180 for one authored the other way, which otherwise plays " +
                 "behind the caster.")]
        [SerializeField, Range(-180f, 180f)] private float _yaw;

        [Tooltip("Which way the cast effect sweeps as authored, seen from behind " +
                 "the caster: on for left to right. Swings alternate, and one " +
                 "going the other way plays the effect mirrored — so this is the " +
                 "box to flip if every slash runs against the blade.")]
        [SerializeField] private bool _sweepsRight;

        [Tooltip("Heard where the skill goes off. Empty uses the sound the cast " +
                 "prefab carries, else the projectile's — which is where a pack " +
                 "keeps its launch sound. Played through the audio presenter's " +
                 "budget, never by the prefab itself.")]
        [SerializeField] private AudioClip _castSound;

        [Tooltip("Heard at every impact, under the same budget. Empty uses the " +
                 "sound the hit prefab carries.")]
        [SerializeField] private AudioClip _hitSound;

        public GameObject Cast => _cast;
        public GameObject Projectile => _projectile;
        public GameObject Hit => _hit;
        public float Scale => Mathf.Max(0.01f, _scale);
        public float Lift => _lift;
        public float Yaw => _yaw;
        public bool SweepsRight => _sweepsRight;

        /// <summary>
        /// The cast sound: the authored clip, else what the cast prefab carries,
        /// else what the projectile carries. Resolved by a lookup, so read once.
        /// </summary>
        public AudioClip ResolveCastSound()
        {
            if (_castSound != null)
                return _castSound;

            AudioClip carried = CarriedSound(_cast);
            return carried != null ? carried : CarriedSound(_projectile);
        }

        /// <summary>The impact sound: the authored clip, else what the hit prefab carries.</summary>
        public AudioClip ResolveHitSound()
            => _hitSound != null ? _hitSound : CarriedSound(_hit);

        /// <summary>
        /// The clip a prefab's own AudioSource would play. Explicit null checks
        /// rather than ?? — Unity's destroyed-object null does not survive it.
        /// </summary>
        private static AudioClip CarriedSound(GameObject prefab)
        {
            if (prefab == null)
                return null;

            AudioSource source = prefab.GetComponentInChildren<AudioSource>(true);
            return source != null && source.clip != null ? source.clip : null;
        }

        /// <summary>
        /// The id the simulation refers to this set by.
        ///
        /// FNV over the asset name, the same hash items and skills use and for
        /// the same reason: the skill database and the presenter's list are
        /// filled by two different things that share no ordering, so an index
        /// would rot the first time somebody reordered either one.
        /// </summary>
        public int VfxId => ItemDefinition.ComputeId(name);
    }
}
