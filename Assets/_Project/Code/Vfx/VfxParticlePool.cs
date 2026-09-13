using System.Collections.Generic;
using UnityEngine;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// Pooled particle prefabs — the authored ones, from whichever pack is in
    /// the project.
    ///
    /// A pool per prefab rather than one pool of instances, because a fire
    /// impact cannot be reused as a frost one: a particle system is its own
    /// configuration, so the only thing two instances of it share is the prefab
    /// they came from. The dictionary is therefore prefab to instances, and it
    /// is the whole data structure.
    ///
    /// Nothing is created after the ceiling is reached. Past it the oldest live
    /// instance is taken back, which is the same trade the line pool makes and
    /// for the same reason: a chain reaction in a crowd is exactly where an
    /// Instantiate becomes a hitch, and the flash that gets stolen is the one
    /// nobody was looking at any more.
    ///
    /// Two ways to use it, because effects come in two shapes:
    ///
    /// - Play: a one-shot at a point. It retires itself once the prefab's own
    ///   particles have had time to finish, so nothing has to remember it.
    /// - Rent and Release: an effect that has to FOLLOW something — a trail on a
    ///   projectile. The caller holds the instance, moves it, and releases it
    ///   when the thing it was following is gone. Release clears the body at
    ///   once and lets only what it left in the air fade, for as long as that
    ///   lives — which is what makes a trail run out rather than get cut, without
    ///   the head hanging where the projectile landed.
    ///
    /// Unscaled time throughout, like the line pool: freezing the effect that
    /// caused a hit-stop would be an odd way to sell the hit-stop.
    /// </summary>
    public sealed class VfxParticlePool
    {
        /// <summary>
        /// One live instance. Public so a follower can hold onto it, and
        /// deliberately opaque: the only thing outside this file may do with it
        /// is move its transform and hand it back.
        /// </summary>
        public sealed class Instance
        {
            public Transform Transform;

            internal GameObject Prefab;
            internal GameObject Root;
            internal ParticleSystem[] Systems;

            /// <summary>
            /// Trail renderers anywhere under the root, and the widths they
            /// were authored with.
            ///
            /// A trail is not a particle: a TrailRenderer ignores the transform
            /// scale completely and measures its width in world units, so the
            /// one thing that makes an effect bigger does nothing to it. The
            /// authored widths are kept because the scale has to be applied to
            /// the ORIGINAL every time — multiplying what is already there
            /// would compound every time the instance is reused.
            /// </summary>
            internal TrailRenderer[] Trails;

            internal float[] TrailWidths;

            /// <summary>
            /// Mesh renderers under the root — an arrow, rather than a particle
            /// effect. The only thing a tint reaches: particles carry their own
            /// colours, and overriding them would flatten the effect.
            /// </summary>
            internal MeshRenderer[] Meshes;

            /// <summary>
            /// Which systems are the body — simulated in local space, so they
            /// ride along with the root — as opposed to what the body leaves
            /// behind in the world. Read once, when the instance is made.
            /// </summary>
            internal bool[] Body;

            /// <summary>How long what the body leaves behind needs to die out, read off the prefab.</summary>
            internal float TrailSeconds;

            /// <summary>Seconds until it goes back. Negative means a follower owns it.</summary>
            internal float SecondsLeft;

            internal bool Emitting;
        }

        private readonly Dictionary<GameObject, List<Instance>> _byPrefab =
            new Dictionary<GameObject, List<Instance>>();

        private readonly List<Instance> _live = new List<Instance>();

        private Transform _parent;
        private int _perPrefab;
        private float _fadeSeconds;
        private float _maxSeconds;

        public void Initialize(
            Transform parent, int perPrefab, float fadeSeconds, float maxSeconds)
        {
            _parent = parent;
            _perPrefab = Mathf.Max(1, perPrefab);
            _fadeSeconds = Mathf.Max(0.05f, fadeSeconds);
            _maxSeconds = Mathf.Max(0.1f, maxSeconds);
        }

        /// <summary>
        /// Plays a one-shot at a point, facing a direction. Silent about
        /// failure: a null prefab is a set that does not name this effect, which
        /// is most sets.
        /// </summary>
        public void Play(
            GameObject prefab, Vector3 position, Quaternion rotation, float scale, bool mirrored = false)
        {
            Instance instance = Rent(prefab, position, rotation, scale, mirrored);

            if (instance == null)
                return;

            // Its own lifetime, read off the prefab rather than authored twice:
            // the pack decided how long its explosion lasts, and a number beside
            // it here would be a second opinion that drifts.
            instance.SecondsLeft = Mathf.Min(_maxSeconds, Duration(instance.Systems));
        }

        /// <summary>
        /// Takes an instance the caller will move itself, or null when the
        /// prefab is missing or the ceiling is reached and everything live is
        /// owned by a follower.
        /// </summary>
        /// <remarks>
        /// Mirrored is a negative X scale — left and right swapped, forward and
        /// up kept — which is how a slash sweeps the other way. It holds for
        /// particle systems scaling by Hierarchy, as every slash in the pack does.
        /// Set on every rent, so a mirrored instance returns to the pool
        /// unmirrored the next time.
        /// </remarks>
        public Instance Rent(
            GameObject prefab, Vector3 position, Quaternion rotation, float scale, bool mirrored = false)
        {
            if (prefab == null || _parent == null)
                return null;

            Instance instance = Free(prefab) ?? Create(prefab) ?? Steal(prefab);

            if (instance == null)
                return null;

            float size = Mathf.Max(0.01f, scale);

            instance.Transform.SetPositionAndRotation(position, rotation);
            instance.Transform.localScale = new Vector3(mirrored ? -size : size, size, size);
            instance.Root.SetActive(true);

            for (int i = 0; i < instance.Systems.Length; i++)
            {
                // Forced only when somebody actually asked for a different
                // size. A prefab authored with Local or Shape scaling ignores
                // the transform for particle sizes — which is a legitimate
                // authoring choice at scale one, and simply wrong the moment
                // the set says "twice as big".
                if (!Mathf.Approximately(size, 1f))
                {
                    ParticleSystem.MainModule main = instance.Systems[i].main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }

                instance.Systems[i].Clear(true);
                instance.Systems[i].Play(true);
            }

            for (int i = 0; i < instance.Trails.Length; i++)
            {
                // Width from the authored one, because scale does not reach a
                // trail on its own — and cleared, because a pooled trail
                // otherwise draws a streak from wherever it last died to
                // wherever it was just rented. That one is visible as a stripe
                // across the room.
                instance.Trails[i].widthMultiplier = instance.TrailWidths[i] * size;
                instance.Trails[i].emitting = true;
                instance.Trails[i].Clear();
            }

            // A pooled arrow that flew through fire last time is not on fire now.
            Tint(instance, null);

            instance.Emitting = true;

            // Owned by the caller until released. The tick leaves it alone.
            instance.SecondsLeft = -1f;

            if (!_live.Contains(instance))
                _live.Add(instance);

            return instance;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock _tintBlock;

        /// <summary>
        /// Colours a rented instance's meshes, or clears the colour with null.
        /// Mesh renderers only — see Instance.Meshes.
        /// </summary>
        public void Tint(Instance instance, Color? color)
        {
            if (instance == null || instance.Meshes.Length == 0)
                return;

            _tintBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < instance.Meshes.Length; i++)
            {
                if (color == null)
                {
                    instance.Meshes[i].SetPropertyBlock(null);
                    continue;
                }

                _tintBlock.Clear();
                _tintBlock.SetColor(BaseColorId, color.Value);
                instance.Meshes[i].SetPropertyBlock(_tintBlock);
            }
        }

        /// <summary>
        /// Hands a rented instance back: stops the emission and lets what is in
        /// the air fade before the instance is reused.
        /// </summary>
        public void Release(Instance instance)
        {
            if (instance == null || !instance.Emitting)
                return;

            StopEmitting(instance);

            // The body goes at once; only what it left behind is let fade. A
            // fireball whose head hangs where it landed for the whole fade reads
            // as a projectile that stopped rather than one that hit. Per system
            // and without children, because the prefab's root is itself a body
            // system, and clearing it with its children would take the trail too.
            //
            // Nothing is detached for this: a released instance is neither moved
            // nor handed out again until it retires, so the trail already stays
            // where it was drawn.
            for (int i = 0; i < instance.Systems.Length; i++)
            {
                if (instance.Body[i])
                    instance.Systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            // As long as the trail itself lives, so it runs out rather than being
            // cut. The configured fade is the floor, the one-shot ceiling the cap.
            instance.SecondsLeft = Mathf.Min(
                _maxSeconds, Mathf.Max(_fadeSeconds, instance.TrailSeconds));
        }

        public void Tick(float unscaledDeltaTime)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Instance instance = _live[i];

                // A follower owns this one. It goes back when that caller says
                // so and not a moment earlier, however long the thing it is
                // following lives.
                if (instance.SecondsLeft < 0f)
                    continue;

                instance.SecondsLeft -= unscaledDeltaTime;

                if (instance.SecondsLeft > 0f)
                    continue;

                Retire(instance);
                _live.RemoveAt(i);
            }
        }

        /// <summary>Every instance home and quiet. For a scene teardown.</summary>
        public void Clear()
        {
            for (int i = 0; i < _live.Count; i++)
                Retire(_live[i]);

            _live.Clear();
        }

        private Instance Free(GameObject prefab)
        {
            if (!_byPrefab.TryGetValue(prefab, out List<Instance> instances))
                return null;

            for (int i = 0; i < instances.Count; i++)
            {
                if (!instances[i].Emitting && instances[i].SecondsLeft <= 0f)
                    return instances[i];
            }

            return null;
        }

        private Instance Create(GameObject prefab)
        {
            if (!_byPrefab.TryGetValue(prefab, out List<Instance> instances))
            {
                instances = new List<Instance>(_perPrefab);
                _byPrefab.Add(prefab, instances);
            }

            if (instances.Count >= _perPrefab)
                return null;

            GameObject root = Object.Instantiate(prefab, _parent);
            root.name = $"{prefab.name} #{instances.Count}";
            root.SetActive(false);

            TrailRenderer[] trails = root.GetComponentsInChildren<TrailRenderer>(true);
            var widths = new float[trails.Length];

            for (int i = 0; i < trails.Length; i++)
                widths[i] = trails[i].widthMultiplier;

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            var body = new bool[systems.Length];
            float trailSeconds = 0f;

            for (int i = 0; i < trails.Length; i++)
                trailSeconds = Mathf.Max(trailSeconds, trails[i].time);

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                body[i] = main.simulationSpace == ParticleSystemSimulationSpace.Local;

                if (!body[i])
                    trailSeconds = Mathf.Max(trailSeconds, main.startLifetime.constantMax);
            }

            var instance = new Instance
            {
                Prefab = prefab,
                Root = root,
                Transform = root.transform,
                Systems = systems,
                Trails = trails,
                TrailWidths = widths,
                Meshes = root.GetComponentsInChildren<MeshRenderer>(true),
                Body = body,
                TrailSeconds = trailSeconds
            };

            instances.Add(instance);
            return instance;
        }

        /// <summary>
        /// The oldest one-shot of this prefab, taken back mid-flight.
        ///
        /// Followers are never stolen: a trail yanked off a projectile in the
        /// air is a visible bug, while a stolen impact flash is one frame of a
        /// crowd nobody could follow anyway.
        /// </summary>
        private Instance Steal(GameObject prefab)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Prefab != prefab || _live[i].SecondsLeft < 0f)
                    continue;

                Instance stolen = _live[i];
                _live.RemoveAt(i);
                return stolen;
            }

            return null;
        }

        private void Retire(Instance instance)
        {
            StopEmitting(instance);

            // Emptied here as well as on rent. A trail cleared only on the way
            // out still has a frame to draw in if the instance is stolen, and
            // one cleared only on the way in relies on every path going through
            // Rent — which Steal does, and a scene teardown does not.
            for (int i = 0; i < instance.Trails.Length; i++)
                instance.Trails[i].Clear();

            instance.Root.SetActive(false);
            instance.SecondsLeft = 0f;
        }

        private static void StopEmitting(Instance instance)
        {
            for (int i = 0; i < instance.Systems.Length; i++)
            {
                instance.Systems[i].Stop(
                    true, ParticleSystemStopBehavior.StopEmitting);
            }

            // Stopped, not cleared. What is already drawn goes on fading for
            // the release window, which is the whole point of releasing rather
            // than switching off — a trail that vanishes with its projectile
            // reads as a bug, and this is the half of it a TrailRenderer needs
            // told separately from the particles.
            for (int i = 0; i < instance.Trails.Length; i++)
                instance.Trails[i].emitting = false;

            instance.Emitting = false;
        }

        /// <summary>
        /// How long the prefab needs to finish: the longest of its systems'
        /// duration plus the life of the last particle each can emit.
        ///
        /// Read from the prefab rather than authored, so importing a pack whose
        /// explosions run for two seconds does not mean typing two seconds
        /// anywhere. Clamped by the caller, because a looping system answers
        /// with its loop and would otherwise never come back.
        /// </summary>
        private static float Duration(ParticleSystem[] systems)
        {
            float longest = 0.1f;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                float life = main.duration + main.startLifetime.constantMax;

                if (life > longest)
                    longest = life;
            }

            return longest;
        }
    }
}
