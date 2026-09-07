using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Config;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Audio
{
    /// <summary>
    /// Plays what the simulation announced.
    ///
    /// A bridge out of ECS: it reads the sound queue, clears it, and writes
    /// nothing back. No system can be made to wait on a noise, and a build
    /// without this component plays identically and silently — the same
    /// property the VFX presenter has, for the same reason.
    ///
    /// Three budgets stand between an event and a voice, and all three exist
    /// because of the same fight this game is built around. Three hundred
    /// enemies dying in a chain reaction is three hundred events; without a
    /// cap it is three hundred overlapping copies of one clip, which is not
    /// three hundred deaths, it is a wall of noise and a clipped mix. So a cue
    /// has a per-frame allowance, a minimum gap, and the whole mix has a budget
    /// on top — the same lever as MaxAreasPerFrame in the combat systems.
    ///
    /// The minimum gap has the same escape hatch the camera shake has, and it
    /// was learned the same way: a noticeably louder instance gets through
    /// anyway. Otherwise an explosion in the middle of a wave of small deaths
    /// is eaten by the deaths.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class AudioPresenter : MonoBehaviour
    {
        /// <summary>
        /// How much louder a sound must be to jump the queue on its cue.
        ///
        /// The same number and the same reasoning as the camera shake: a
        /// slightly louder repeat is the same event, and only a clearly bigger
        /// one is worth interrupting for.
        /// </summary>
        private const float OverrideFactor = 1.5f;

        [SerializeField] private AudioConfig _config;

        private Transform _listener;
        private EntityManager _entityManager;
        private EntityQuery _eventsQuery;
        private bool _hasWorld;

        private readonly AudioSourcePool _pool = new AudioSourcePool();

        private AudioCueSettings[] _cues;
        private int[] _playedThisFrame;
        private float[] _lastPlayedAt;
        private float[] _lastVolume;

        private float _now;
        private int _voicesThisFrame;
        private Random _random;

        /// <summary>
        /// The listener is where the AudioListener actually is — the camera rig,
        /// not the player. Distance culling has to agree with the attenuation
        /// Unity is applying, or sounds get dropped while still audible.
        /// </summary>
        public void Initialize(Transform listener)
        {
            _listener = listener;

            if (_config == null)
            {
                Debug.LogError(
                    $"[{nameof(AudioPresenter)}] No AudioConfig assigned — the game will play " +
                    "correctly and silently.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(AudioPresenter)}] ECS world is unavailable — nothing to play.",
                    this);
                return;
            }

            _entityManager = world.EntityManager;
            _eventsQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<AudioEventsSingleton>());

            _cues = _config.BuildIndex();
            _playedThisFrame = new int[_cues.Length];
            _lastPlayedAt = new float[_cues.Length];
            _lastVolume = new float[_cues.Length];

            for (int i = 0; i < _cues.Length; i++)
                _lastPlayedAt[i] = float.NegativeInfinity;

            _pool.Initialize(_config.VoicePoolSize, transform);

            // Local flavour, so it does not have to be reproducible or agreed
            // with anybody: which of four grunts plays is nobody else's
            // business. Seeded off the instance so two runs do not open with
            // the same clip every time.
            _random = Random.CreateFromIndex((uint)math.abs(GetInstanceID()) + 1u);

            _hasWorld = true;
        }

        /// <summary>
        /// LateUpdate, so the queue drained is the one this frame's simulation
        /// filled rather than last frame's.
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasWorld)
                return;

            float unscaled = Time.unscaledDeltaTime;

            _now += unscaled;
            _pool.Tick(unscaled);

            _voicesThisFrame = 0;
            System.Array.Clear(_playedThisFrame, 0, _playedThisFrame.Length);

            Drain();
        }

        private void Drain()
        {
            if (_eventsQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _eventsQuery.GetSingletonEntity();
            DynamicBuffer<AudioEvent> events = _entityManager.GetBuffer<AudioEvent>(registry);

            for (int i = 0; i < events.Length; i++)
                TryPlay(events[i]);

            // Events last exactly one frame. Anything not played now belongs to
            // a moment that has passed, and starting it late is worse than not
            // starting it.
            events.Clear();
        }

        private void TryPlay(in AudioEvent sound)
        {
            int cue = (int)sound.Cue;

            if (cue <= 0 || cue >= _cues.Length)
                return;

            AudioCueSettings settings = _cues[cue];

            // A cue nobody has recorded a clip for is a normal state for a
            // prototype, not an error to shout about every frame.
            if (settings == null || !settings.HasClips)
                return;

            if (_voicesThisFrame >= _config.MaxVoicesPerFrame)
                return;

            if (_playedThisFrame[cue] >= settings.MaxVoicesPerFrame)
                return;

            float volume = sound.Volume > 0f ? sound.Volume : 1f;
            volume *= settings.Volume * _config.MasterVolume;

            if (volume <= 0.001f)
                return;

            // Interval gating, with the louder-gets-through escape. Without the
            // escape a sustained wave of small sounds swallows the big one that
            // matters; without the gate the wave is a solid tone.
            float since = _now - _lastPlayedAt[cue];

            if (since < settings.MinIntervalSeconds && volume <= _lastVolume[cue] * OverrideFactor)
                return;

            if (settings.Spatial && !IsAudible(sound.Position))
                return;

            AudioSource source = _pool.Acquire();
            if (source == null)
                return;

            Configure(source, settings, sound, volume);
            source.Play();

            _voicesThisFrame++;
            _playedThisFrame[cue]++;
            _lastPlayedAt[cue] = _now;
            _lastVolume[cue] = volume;
        }

        /// <summary>
        /// Whether anything would be heard.
        ///
        /// Checked before a voice is taken, not after: a sound nobody can hear
        /// still costs a source that somebody audible then cannot have, which
        /// is how a distant fight silences the one the player is in.
        /// </summary>
        private bool IsAudible(float3 position)
        {
            if (_listener == null)
                return true;

            float distance = math.distance(position, (float3)_listener.position);
            return distance <= _config.MaxAudibleDistance;
        }

        private void Configure(
            AudioSource source, AudioCueSettings settings, in AudioEvent sound, float volume)
        {
            AudioClip[] clips = settings.Clips;
            AudioClip clip = clips[_random.NextInt(0, clips.Length)];

            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);

            source.pitch = sound.Pitch > 0f
                ? sound.Pitch
                : _random.NextFloat(settings.MinPitch, settings.MaxPitch);

            if (settings.Spatial)
            {
                source.spatialBlend = 1f;
                source.maxDistance = _config.MaxAudibleDistance;
                source.transform.position = sound.Position;
            }
            else
            {
                // Flat, and parked on the pool so a stale world position from a
                // previous spatial use cannot leak into the pan.
                source.spatialBlend = 0f;
                source.transform.localPosition = Vector3.zero;
            }
        }

        private void OnDestroy()
        {
            _pool.Release();

            if (_hasWorld)
                _eventsQuery.Dispose();
        }
    }
}
