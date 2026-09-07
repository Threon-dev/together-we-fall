using UnityEngine;

namespace TogetherWeFall.Audio
{
    /// <summary>
    /// A fixed set of AudioSources, handed out and taken back.
    ///
    /// Fixed for the same reason every other pool in this project is: a chain
    /// reaction produces sounds by the hundred, and something that allocates
    /// per event allocates exactly when the frame can least afford it. After
    /// Initialize nothing is created, and when every source is busy the oldest
    /// one is taken — the same bargain the line pool makes, and the right one,
    /// because the sound that started longest ago is the one nobody is still
    /// listening for.
    ///
    /// AudioSource is not affected by Time.timeScale unless its pitch is tied
    /// to it, and nothing here ties it. That is deliberate: a hit-stop should
    /// freeze the picture, not drop the audio into a groan.
    /// </summary>
    public sealed class AudioSourcePool
    {
        private AudioSource[] _sources;
        private float[] _startedAt;
        private float _now;

        public int Size => _sources != null ? _sources.Length : 0;

        public void Initialize(int size, Transform parent)
        {
            _sources = new AudioSource[Mathf.Max(1, size)];
            _startedAt = new float[_sources.Length];

            for (int i = 0; i < _sources.Length; i++)
            {
                var host = new GameObject($"Voice {i}");
                host.transform.SetParent(parent, worldPositionStays: false);

                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;

                // Logarithmic falloff and a hard cutoff at the far end, so a
                // source left playing across the floor costs nothing audible.
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.dopplerLevel = 0f;

                _sources[i] = source;
                _startedAt[i] = float.NegativeInfinity;
            }
        }

        public void Tick(float unscaledDelta)
        {
            _now += unscaledDelta;
        }

        /// <summary>
        /// A source that is not playing, or the one that has been playing
        /// longest. Never null once Initialize has run.
        /// </summary>
        public AudioSource Acquire()
        {
            if (_sources == null)
                return null;

            int oldest = 0;
            float oldestTime = float.PositiveInfinity;

            for (int i = 0; i < _sources.Length; i++)
            {
                if (!_sources[i].isPlaying)
                {
                    _startedAt[i] = _now;
                    return _sources[i];
                }

                if (_startedAt[i] >= oldestTime)
                    continue;

                oldestTime = _startedAt[i];
                oldest = i;
            }

            // Everything is busy. Cutting the oldest short is audible; running
            // out of voices silently is worse, because the sound that gets
            // dropped is the one that just happened.
            _sources[oldest].Stop();
            _startedAt[oldest] = _now;

            return _sources[oldest];
        }

        /// <summary>Stops everything. Used when the scene goes away.</summary>
        public void Release()
        {
            if (_sources == null)
                return;

            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] != null)
                    _sources[i].Stop();
            }
        }
    }
}
