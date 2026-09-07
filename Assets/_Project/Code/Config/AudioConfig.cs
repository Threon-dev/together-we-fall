using System;
using UnityEngine;
using TogetherWeFall.Audio;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Everything one cue sounds like.
    ///
    /// Several clips rather than one because the alternative is a machine gun:
    /// the same sample forty times in two seconds stops reading as forty events
    /// and starts reading as a fault. The pitch range does the same job more
    /// cheaply for cues that only have one clip.
    /// </summary>
    [Serializable]
    public sealed class AudioCueSettings
    {
        [SerializeField] private AudioCue _cue = AudioCue.None;

        [Tooltip("One is picked at random. Leaving this empty makes the cue " +
                 "silent without making it an error — a sound nobody has " +
                 "recorded yet is a normal state for a prototype.")]
        [SerializeField] private AudioClip[] _clips = Array.Empty<AudioClip>();

        [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;

        [Tooltip("A pitch is rolled between these unless the event names one.")]
        [SerializeField] private Vector2 _pitchRange = new Vector2(0.95f, 1.05f);

        [Tooltip("Off for anything without a place in the world — menu clicks, " +
                 "and anything the local player did rather than saw.")]
        [SerializeField] private bool _spatial = true;

        [Tooltip("How many of this cue may start in one frame. Three hundred " +
                 "enemies dying at once is three hundred events and about two " +
                 "sounds worth hearing.")]
        [SerializeField, Range(1, 16)] private int _maxVoicesPerFrame = 3;

        [Tooltip("Shortest gap between two of this cue. Zero lets every frame " +
                 "start its full allowance, which for a sustained chain " +
                 "reaction is a solid tone rather than a fight.")]
        [SerializeField, Range(0f, 1f)] private float _minIntervalSeconds = 0.06f;

        public AudioCue Cue => _cue;
        public AudioClip[] Clips => _clips;
        public float Volume => _volume;
        public bool Spatial => _spatial;
        public int MaxVoicesPerFrame => _maxVoicesPerFrame;
        public float MinIntervalSeconds => _minIntervalSeconds;

        /// <summary>Ordered, so an inspector with the two swapped still works.</summary>
        public float MinPitch => Mathf.Min(_pitchRange.x, _pitchRange.y);
        public float MaxPitch => Mathf.Max(_pitchRange.x, _pitchRange.y);

        public bool HasClips => _clips != null && _clips.Length > 0;
    }

    /// <summary>
    /// How the game sounds, and how much of it may sound at once.
    ///
    /// A ScriptableObject like every other config here, and referenced directly
    /// rather than through Addressables for the same reason VfxConfig is: there
    /// is no runtime loading to do yet. A prototype's worth of clips loads with
    /// the scene and stays. The moment sound needs loading and unloading per
    /// floor, that is the first real need for the loading module the
    /// conventions describe, and this is where it plugs in.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AudioConfig",
        menuName = "Together We Fall/Audio Config")]
    public sealed class AudioConfig : ScriptableObject
    {
        [SerializeField] private AudioCueSettings[] _cues = Array.Empty<AudioCueSettings>();

        [Header("Voices")]
        [Tooltip("AudioSources created once and reused forever. Also the ceiling " +
                 "on how many sounds can overlap, in the same way the enemy pool " +
                 "is the living cap.")]
        [SerializeField, Range(4, 64)] private int _voicePoolSize = 24;

        [Tooltip("Across all cues. The same per-frame lever as MaxAreasPerFrame " +
                 "in the combat systems, for the same reason: a mass cast should " +
                 "spread over frames rather than drop one.")]
        [SerializeField, Range(1, 32)] private int _maxVoicesPerFrame = 8;

        [Header("Space")]
        [Tooltip("Past this, an event is dropped before a voice is taken for it. " +
                 "A sound nobody can hear still costs a source that somebody " +
                 "audible then cannot have.")]
        [SerializeField, Range(5f, 200f)] private float _maxAudibleDistance = 45f;

        [SerializeField, Range(0f, 1f)] private float _masterVolume = 1f;

        public int VoicePoolSize => _voicePoolSize;
        public int MaxVoicesPerFrame => _maxVoicesPerFrame;
        public float MaxAudibleDistance => _maxAudibleDistance;
        public float MasterVolume => _masterVolume;

        public AudioCueSettings[] Cues => _cues;

        /// <summary>
        /// The cue table as a lookup, indexed by the enum value.
        ///
        /// Built once at startup rather than searched per event: a chain
        /// reaction produces hundreds of these a second, and a linear scan of
        /// the authored list per sound is the kind of cost that only shows up
        /// when the game is already busy.
        ///
        /// A cue with no entry comes back null and is skipped, which is what
        /// makes a half-authored config silent rather than broken.
        /// </summary>
        public AudioCueSettings[] BuildIndex()
        {
            int highest = 0;

            foreach (AudioCue value in Enum.GetValues(typeof(AudioCue)))
                highest = Mathf.Max(highest, (int)value);

            var index = new AudioCueSettings[highest + 1];

            if (_cues == null)
                return index;

            for (int i = 0; i < _cues.Length; i++)
            {
                if (_cues[i] == null)
                    continue;

                int slot = (int)_cues[i].Cue;

                if (slot < 0 || slot >= index.Length)
                    continue;

                // First wins, so a duplicated cue is a warning in the log rather
                // than whichever entry happened to be last.
                if (index[slot] != null)
                {
                    Debug.LogWarning(
                        $"[{nameof(AudioConfig)}] {_cues[i].Cue} is listed twice — the second " +
                        "entry is ignored.", this);
                    continue;
                }

                index[slot] = _cues[i];
            }

            return index;
        }
    }
}
