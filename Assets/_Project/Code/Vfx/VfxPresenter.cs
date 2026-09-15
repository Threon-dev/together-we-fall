using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using TogetherWeFall.UI;
using TogetherWeFall.Audio;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Enemies;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Vfx
{
    /// <summary>
    /// Draws what the simulation announced, and hits the camera when it is worth
    /// it.
    ///
    /// A bridge out of ECS rather than into it: it reads the event queue, clears
    /// it, and writes nothing back. That direction matters — no system can be
    /// made to wait on presentation, and a build with this component missing
    /// plays identically and silently.
    ///
    /// It is also where the two feel rules live that the simulation has no
    /// business knowing. What counts as a mass kill is a question about the
    /// player's attention, not about the game state, so the threshold and the
    /// window are read here from the events rather than tracked by any system.
    ///
    /// Everything it draws is a pooled prefab from the effect pack: a skill's own
    /// visual set, or — for what no skill authored, a chain jump, a reaction, a
    /// corpse going off — its element's row in the config. Swapping the pack
    /// means swapping those assets and nothing else, because everything upstream
    /// only ever said what happened.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class VfxPresenter : MonoBehaviour
    {
        /// <summary>
        /// Further than any projectile flies between two frames, even through a
        /// hitch at recording frame rates — 34 m/s at 10 fps is 3.4 m. A trail
        /// whose projectile moved more than this is following a new shot.
        /// </summary>
        private const float TrailJumpDistanceSq = 6f * 6f;

        /// <summary>
        /// How long after its last pulse a beam is still drawn: this, or two
        /// pulses. Pulses reach a client as snapshots, and one late snapshot
        /// should not cut the beam and restart its sound.
        /// </summary>
        private const float MinChannelGap = 0.25f;

        /// <summary>
        /// A channelled beam per caster: the cast count last seen, when it last
        /// moved, and the stretched instance while the channel lasts.
        /// </summary>
        private struct Channel
        {
            public uint Count;
            public float LastPulse;
            public VfxParticlePool.Instance Beam;
        }

        private readonly Dictionary<Entity, Channel> _channels = new Dictionary<Entity, Channel>();
        private readonly List<Entity> _goneChannels = new List<Entity>();
        private EntityQuery _castersQuery;

        [SerializeField] private VfxConfig _config;

        [Tooltip("Canvas the damage numbers are drawn on. Without it everything " +
                 "else still works and the numbers are simply absent.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Every authored visual set in the project, filled in by the " +
                 "scene build. A skill whose set is missing from this list " +
                 "draws only its element's generic effects.")]
        [SerializeField] private SkillVfxSet[] _skillVfx = System.Array.Empty<SkillVfxSet>();

        /// <summary>The config's element rows, indexed by DamageType.</summary>
        private ElementVfxSettings[] _elements = System.Array.Empty<ElementVfxSettings>();

        private readonly VfxParticlePool _particles = new VfxParticlePool();

        /// <summary>Visual sets by id, the way the simulation refers to them.</summary>
        private readonly Dictionary<int, SkillVfxSet> _sets = new Dictionary<int, SkillVfxSet>();

        /// <summary>
        /// Each set's sounds, resolved once: the fallback to what a prefab
        /// carries is a component lookup, and a cast is the fastest event there is.
        /// </summary>
        private readonly Dictionary<int, AudioClip> _castSounds = new Dictionary<int, AudioClip>();
        private readonly Dictionary<int, AudioClip> _hitSounds = new Dictionary<int, AudioClip>();

        /// <summary>Optional. Without it every effect still draws, silently.</summary>
        private AudioPresenter _audio;

        /// <summary>
        /// Which trail is following which projectile.
        ///
        /// Keyed by entity rather than tracked on the projectile, because the
        /// projectile is a pooled entity that knows nothing about presentation —
        /// and must not: a build with no presenter fires exactly the same shots.
        /// </summary>
        private readonly Dictionary<Entity, VfxParticlePool.Instance> _trails =
            new Dictionary<Entity, VfxParticlePool.Instance>();

        private readonly List<Entity> _goneTrails = new List<Entity>();
        private readonly DamageNumberPool _numbers = new DamageNumberPool();
        private readonly StatusIconPool _icons = new StatusIconPool();

        private TopDownCameraRig _camera;
        private EntityManager _entityManager;
        private EntityQuery _eventsQuery;
        private EntityQuery _afflictedQuery;
        private EntityQuery _flyingQuery;
        private bool _hasWorld;

        private int _deathsInWindow;
        private float _windowRemaining;
        private float _massKillCooldown;

        private float _shakeCooldown;
        private float _lastShakeStrength;

        public void Initialize(TopDownCameraRig cameraRig, AudioPresenter audio)
        {
            _camera = cameraRig;
            _audio = audio;

            if (_config == null)
            {
                Debug.LogError(
                    $"[{nameof(VfxPresenter)}] No VfxConfig assigned — " +
                    "the game will play correctly and look flat.", this);
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(VfxPresenter)}] ECS world is unavailable — nothing to draw.", this);
                return;
            }

            _entityManager = world.EntityManager;
            _eventsQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<VfxEventsSingleton>());

            // EnemyTag is enableable, so this query is already only the bodies
            // that are currently enemies: one asleep in the pool and one playing
            // out its death are both excluded, and neither the pool nor the
            // death fade has to remember to take a marker down.
            _afflictedQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EnemyTag>(),
                ComponentType.ReadOnly<StatusVisual>(),
                ComponentType.ReadOnly<LocalTransform>());

            // Only the ones in the air. ProjectileActive is enableable, so
            // this query is already the live set: one parked in the pool a
            // kilometre under the floor is excluded without anything having to
            // remember to take a marker down.
            _flyingQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SkillProjectile>(),
                ComponentType.ReadOnly<ProjectileActive>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<URPMaterialPropertyBaseColor>());

            // Every character, for the beams they channel.
            _castersQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CastCue>());

            for (int i = 0; i < _skillVfx.Length; i++)
            {
                if (_skillVfx[i] == null)
                    continue;

                // Last one wins and says so. Two sets hashing to one id means
                // two assets with the same name, which is the same collision the
                // item database refuses at bake time.
                if (_sets.ContainsKey(_skillVfx[i].VfxId))
                {
                    Debug.LogWarning(
                        $"[{nameof(VfxPresenter)}] Two visual sets share the id of " +
                        $"'{_skillVfx[i].name}'. Give them distinct asset names.", this);
                }

                _sets[_skillVfx[i].VfxId] = _skillVfx[i];
                _castSounds[_skillVfx[i].VfxId] = _skillVfx[i].ResolveCastSound();
                _hitSounds[_skillVfx[i].VfxId] = _skillVfx[i].ResolveHitSound();
            }

            _elements = _config.BuildElementIndex();
            _particles.Initialize(
                transform,
                _config.ParticlesPerEffect,
                _config.ParticleFadeSeconds,
                _config.ParticleMaxSeconds);

            if (_canvas == null)
            {
                Debug.LogWarning(
                    $"[{nameof(VfxPresenter)}] No Canvas assigned — damage numbers will " +
                    "not be drawn. Everything else is unaffected.", this);
            }
            _windowRemaining = _config.MassKillWindowSeconds;
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

            // Built on first use rather than in Initialize: the bootstrap that
            // calls Initialize deliberately runs before every other component in
            // the scene, and the camera it hands over is one of them.
            EnsureNumbers();

            if (_shakeCooldown > 0f)
                _shakeCooldown -= unscaled;

            DrainEvents();
            DrawTrails();
            DrawBeams();
            DrawStatusMarkers();
            AdvanceMassKillWindow(unscaled);

            _particles.Tick(unscaled);
            _numbers.Tick(unscaled);
        }

        private void DrainEvents()
        {
            if (_eventsQuery.IsEmptyIgnoreFilter)
                return;

            Entity registry = _eventsQuery.GetSingletonEntity();
            DynamicBuffer<VfxEvent> events = _entityManager.GetBuffer<VfxEvent>(registry);

            for (int i = 0; i < events.Length; i++)
                Handle(events[i]);

            // Events last exactly one frame. Anything not drawn now is a flash
            // that already belongs to a moment which has passed.
            events.Clear();
        }

        private void Handle(in VfxEvent effect)
        {
            switch (effect.Kind)
            {
                case VfxEventKind.ChainLink:
                    HandleChainLink(effect);
                    break;

                case VfxEventKind.BoltStrike:
                    HandleBoltStrike(effect);
                    break;

                case VfxEventKind.Explosion:
                    HandleExplosion(effect);
                    break;

                case VfxEventKind.Death:
                    HandleDeath(effect);
                    break;

                case VfxEventKind.DamageNumber:
                    HandleDamageNumber(effect);
                    break;

                case VfxEventKind.ElementBurst:
                    HandleElementBurst(effect);
                    break;

                case VfxEventKind.SkillCast:
                    HandleSkillCast(effect);
                    break;

                case VfxEventKind.SkillHit:
                    HandleSkillHit(effect);
                    break;
            }
        }

        private void HandleChainLink(in VfxEvent effect)
        {
            // The link lives as long as the jump delay, so one link is on screen
            // until the next appears and the chain reads as travelling.
            PlayBeam(effect, Mathf.Max(_config.ChainLinkSeconds, effect.Magnitude), _config.ChainLinkWidth);
        }

        /// <summary>
        /// The stroke from the caster to the first body. Same shape as a jump,
        /// drawn heavier and held a little longer — it is the part of the chain
        /// the player aimed, and it should read as the cause rather than as one
        /// more link.
        /// </summary>
        private void HandleBoltStrike(in VfxEvent effect)
            => PlayBeam(effect, Mathf.Max(_config.BoltStrikeSeconds, effect.Magnitude), _config.BoltStrikeWidth);

        /// <summary>
        /// The element's beam between the event's two ends, lifted to roughly
        /// chest height: drawn between two entity origins it would run along the
        /// floor and read as a decal rather than as lightning between two bodies.
        /// </summary>
        private void PlayBeam(in VfxEvent effect, float seconds, float width)
        {
            ElementVfxSettings element = ElementOf(effect);

            if (element == null || element.Beam == null)
                return;

            _particles.PlayBetween(
                element.Beam, ToWorld(effect.Position), ToWorld(effect.EndPosition), width, seconds);
        }

        /// <summary>
        /// The burst of whatever just combined, and nothing else.
        ///
        /// Deliberately no shake and no freeze. A reaction can fire on every hit
        /// that lands in a burning crowd, and punctuating each one would mean
        /// punctuating none of them — the camera never settling and time never
        /// coming back to one. The same reasoning that gives the shake and the
        /// hit-stop the right to refuse, applied one step earlier: some things
        /// simply do not ask.
        /// </summary>
        private void HandleElementBurst(in VfxEvent effect)
        {
            ElementVfxSettings element = ElementOf(effect);

            if (element != null && element.Reaction != null)
            {
                _particles.Play(
                    element.Reaction, ToWorld(effect.Position), Quaternion.identity,
                    BlastScale(effect.Magnitude));
            }
        }

        private void HandleExplosion(in VfxEvent effect)
        {
            // A skill's own blast belongs to its visual set, which has already
            // drawn it at this point. The element's blast is for what nobody
            // authored: a corpse going off.
            ElementVfxSettings element = effect.VfxId == 0 ? ElementOf(effect) : null;

            if (element != null && element.Blast != null)
            {
                _particles.Play(
                    element.Blast, ToWorld(effect.Position), Quaternion.identity,
                    BlastScale(effect.Magnitude));
            }

            // A shake and no freeze. There was a hit-stop here, and in a crowd
            // with exploding deaths it went off often enough to read as the
            // whole fight running in slow motion.
            TryShake(_config.ExplosionShake);
        }

        /// <summary>
        /// One event for every body that fell this frame, with the count in it.
        /// The arithmetic is the same as doing it per death; the queue is not.
        /// </summary>
        private void HandleDeath(in VfxEvent effect)
        {
            int deaths = Mathf.Max(1, Mathf.RoundToInt(effect.Magnitude));

            _deathsInWindow += deaths;

            // Each death nudges the camera. One is imperceptible; forty in a
            // frame is the crowd coming apart. The rig clamps the total, so a
            // wipe does not throw the camera across the room.
            TryShake(_config.DeathShake * deaths);
        }

        private void HandleDamageNumber(in VfxEvent effect)
        {
            if (!_numbers.IsReady)
                return;

            _numbers.Add(
                ToWorld(effect.Position),
                effect.Magnitude,
                ToColor(effect.Color),
                _config.DamageNumberSeconds,
                _config.DamageNumberRise,
                effect.Emphasis ? _config.DamageNumberKillScale : 1f);
        }

        private void EnsureNumbers()
        {
            if (_numbers.IsReady || _canvas == null || _camera == null)
                return;

            var root = (RectTransform)_canvas.transform;

            _numbers.Initialize(
                _config.DamageNumberPoolSize,
                _config.DamageNumberFontSize,
                root,
                _camera.Camera);

            _icons.Initialize(
                _config.StatusIconBudget,
                _config.StatusIconFontSize,
                root,
                _camera.Camera);
        }

        /// <summary>
        /// Puts a marker over every afflicted body the player can actually read
        /// one on.
        ///
        /// A level rather than a queue, which is why this reads the world
        /// directly instead of draining events. A status lasts seconds; an event
        /// per frame per status would be three hundred events on a frame where a
        /// crowd is burning, and the presenter would then have to work out which
        /// of them were still true.
        ///
        /// It still reads and never writes, so the rule the whole VFX seam rests
        /// on holds: a build without this component plays identically. The
        /// simulation reduced the buffer to StatusVisual and knows nothing about
        /// what became of it.
        ///
        /// Budgeted by distance first and then by count, in that order and
        /// deliberately: the same rule the audio presenter follows, because a
        /// marker nobody can read still takes the label the near fight needed.
        /// </summary>
        private void DrawStatusMarkers()
        {
            if (!_icons.IsReady || _camera == null || _camera.Camera == null)
                return;

            _icons.Begin();

            if (!_afflictedQuery.IsEmptyIgnoreFilter)
            {
                using NativeArray<StatusVisual> visuals =
                    _afflictedQuery.ToComponentDataArray<StatusVisual>(Allocator.Temp);
                using NativeArray<LocalTransform> transforms =
                    _afflictedQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

                Vector3 viewer = _camera.Camera.transform.position;
                float rangeSq = _config.StatusIconRange * _config.StatusIconRange;

                for (int i = 0; i < visuals.Length && i < transforms.Length; i++)
                {
                    if (visuals[i].Icons == 0)
                        continue;

                    Vector3 world = transforms[i].Position;

                    if ((world - viewer).sqrMagnitude > rangeSq)
                        continue;

                    Color tint = visuals[i].HasTint
                        ? new Color(
                            visuals[i].Tint.x, visuals[i].Tint.y, visuals[i].Tint.z, 1f)
                        : Color.white;

                    _icons.Add(world, visuals[i].Icons, tint);
                }
            }

            _icons.Finish();
        }

        /// <summary>
        /// Knocks the camera, unless it was knocked a moment ago.
        ///
        /// Shake decays in about a third of a second, and a fight delivers
        /// something worth shaking about every frame — so without a gap the
        /// decay never gets ahead and the picture simply vibrates. A distinctly
        /// bigger impact still gets through: an explosion in the middle of a
        /// wave of deaths is the one thing that should not be swallowed by the
        /// deaths.
        ///
        /// The rule lives here rather than in the rig on purpose. AddShake is a
        /// primitive — knock the camera by this much — and how often that is
        /// worth doing is a question about the player's attention, which is what
        /// this class is for.
        /// </summary>
        private void TryShake(float strength)
        {
            if (strength <= 0f)
                return;

            if (_shakeCooldown > 0f && strength <= _lastShakeStrength * 1.5f)
                return;

            _camera.AddShake(strength);
            _lastShakeStrength = strength;
            _shakeCooldown = _config.ShakeIntervalSeconds;
        }

        /// <summary>
        /// Marks the moment the player wipes out a crowd.
        ///
        /// A tumbling window rather than a sliding one: it costs a counter and a
        /// timer instead of a queue of timestamps, and the difference is
        /// invisible for something whose only job is to fire a camera punch
        /// occasionally. The cooldown is what stops a long fight from punching
        /// on every window boundary.
        /// </summary>
        private void AdvanceMassKillWindow(float unscaledDeltaTime)
        {
            if (_massKillCooldown > 0f)
                _massKillCooldown -= unscaledDeltaTime;

            if (_deathsInWindow >= _config.MassKillThreshold && _massKillCooldown <= 0f)
            {
                _camera.AddZoomPunch(_config.MassKillZoomPunch);
                _massKillCooldown = _config.MassKillCooldownSeconds;
                _deathsInWindow = 0;
            }

            _windowRemaining -= unscaledDeltaTime;
            if (_windowRemaining > 0f)
                return;

            _windowRemaining = _config.MassKillWindowSeconds;
            _deathsInWindow = 0;
        }

        // ─────────────────────────────────────────────────────────────────
        // Authored effects
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The flash where a skill went off, from the skill's own visual set.
        ///
        /// Nothing else happens here: no shake and no freeze. A cast is the one
        /// event in the queue that fires as fast as a player can press a key,
        /// and punctuating each one would leave the camera permanently unsettled
        /// — the same argument the element burst already makes.
        /// </summary>
        private void HandleSkillCast(in VfxEvent effect)
        {
            if (!TryGetSet(effect.VfxId, out SkillVfxSet set))
                return;

            Vector3 point = ToPoint(effect.Position, set.Lift);

            if (set.Cast != null)
            {
                _particles.Play(
                    set.Cast, point,
                    Facing(effect) * Quaternion.Euler(0f, set.Yaw, 0f), set.Scale,
                    mirrored: effect.SweepRight != set.SweepsRight);
            }

            // Heard even when nothing is drawn here: an arrow leaves the bow
            // with a sound and no flash.
            PlaySound(AudioCue.SkillCast, _castSounds, effect.VfxId, point);
        }

        /// <summary>
        /// The effect at the point of impact, from the same set. One per blow,
        /// which is what the queue already carries.
        /// </summary>
        private void HandleSkillHit(in VfxEvent effect)
        {
            if (!TryGetSet(effect.VfxId, out SkillVfxSet set))
                return;

            Vector3 point = ToPoint(effect.Position, set.Lift);

            if (set.Hit != null)
                _particles.Play(set.Hit, point, Quaternion.identity, set.Scale);

            PlaySound(AudioCue.ProjectileImpact, _hitSounds, effect.VfxId, point);
        }

        /// <summary>
        /// A set's sound, through the audio presenter's budget for the cue it
        /// stands in for. Forty bodies struck by one nova are forty impacts
        /// drawn and, by that budget, about one heard.
        /// </summary>
        private void PlaySound(AudioCue cue, Dictionary<int, AudioClip> sounds, int vfxId, Vector3 point)
        {
            if (_audio != null && sounds.TryGetValue(vfxId, out AudioClip clip))
                _audio.PlayAuthored(cue, clip, point);
        }

        /// <summary>
        /// Keeps one trail on every projectile in the air.
        ///
        /// A pull rather than a push, and that is the whole design: a projectile
        /// is a pooled entity that is fired by raising a flag and retired by
        /// lowering one, so there is no event to subscribe to and deliberately
        /// never will be. What there is instead is a query that answers "what is
        /// flying right now" — so this reads that, rents a trail for anything
        /// new, moves the ones it already has, and releases the rest.
        ///
        /// Released rather than stopped dead: the emission ends and what is
        /// already in the air fades, which is the difference between a trail
        /// that lands with its bolt and one that vanishes a frame early.
        /// </summary>
        private void DrawTrails()
        {
            if (_flyingQuery.IsEmptyIgnoreFilter && _trails.Count == 0)
                return;

            using NativeArray<Entity> flying = _flyingQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<SkillProjectile> projectiles =
                _flyingQuery.ToComponentDataArray<SkillProjectile>(Allocator.Temp);
            using NativeArray<LocalTransform> transforms =
                _flyingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using NativeArray<URPMaterialPropertyBaseColor> colours =
                _flyingQuery.ToComponentDataArray<URPMaterialPropertyBaseColor>(Allocator.Temp);

            // Marked before anything is added, so a projectile that is still in
            // the air keeps the trail it already has and everything else is
            // released below. A HashSet would do the same; the list is smaller
            // than the allocation that would save.
            _goneTrails.Clear();

            foreach (KeyValuePair<Entity, VfxParticlePool.Instance> pair in _trails)
                _goneTrails.Add(pair.Key);

            for (int i = 0; i < flying.Length; i++)
            {
                Entity projectile = flying[i];
                Vector3 position = transforms[i].Position;

                // Along its own flight, which is the one rotation a trail needs
                // and the one thing LocalTransform does not carry for a
                // projectile — the pool never rotates them, because a cube does
                // not care and a mesh does.
                Vector3 velocity = projectiles[i].Velocity;
                Quaternion rotation = velocity.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(velocity)
                    : Quaternion.identity;

                // What it picked up on the way, on the model itself: the
                // projectile has no body of its own any more to wear the colour
                // the overlap system gives it. Only once it carries something —
                // a plain shot keeps the colours it was modelled with.
                Color? carried = projectiles[i].CarriedElements != 0
                    ? ToColor(colours[i].Value)
                    : null;

                if (_trails.TryGetValue(projectile, out VfxParticlePool.Instance live))
                {
                    _goneTrails.Remove(projectile);

                    // A pooled projectile can be spent and fired again between
                    // two frames this presenter sees, so its flag never read as
                    // down. Dragging the old trail to the new muzzle would draw a
                    // streak across the room: a jump no flight makes is a new shot.
                    if ((position - live.Transform.position).sqrMagnitude <= TrailJumpDistanceSq)
                    {
                        live.Transform.SetPositionAndRotation(position, rotation);

                        if (carried != null)
                            _particles.Tint(live, carried);

                        continue;
                    }

                    _particles.Release(live);
                    _trails.Remove(projectile);
                }

                if (!TryGetSet(projectiles[i].VfxId, out SkillVfxSet set) ||
                    set.Projectile == null)
                {
                    continue;
                }

                VfxParticlePool.Instance rented =
                    _particles.Rent(set.Projectile, position, rotation, set.Scale);

                // Null when the ceiling for this prefab is reached. The shot
                // still flies and still hits; it simply flies bare, which under
                // the kind of barrage that reaches the ceiling is invisible.
                if (rented != null)
                {
                    _trails.Add(projectile, rented);

                    // A fork is born carrying what its parent carried.
                    if (carried != null)
                        _particles.Tint(rented, carried);
                }
            }

            for (int i = 0; i < _goneTrails.Count; i++)
            {
                _particles.Release(_trails[_goneTrails[i]]);
                _trails.Remove(_goneTrails[i]);
            }
        }

        /// <summary>
        /// Keeps a beam stretched from every caster who is channelling one.
        ///
        /// A level, like the trails, and for the same reason: a channel is a held
        /// button, not an event. The host casts a pulse while the button is held
        /// and the mana lasts, counts it on CastCue and writes where the beam ran;
        /// this draws while those counts keep coming and lets go when they stop.
        /// Nothing says "the channel ended" — letting go, running dry and being
        /// silenced all look the same from here, which is the point.
        /// </summary>
        private void DrawBeams()
        {
            if (_castersQuery.IsEmptyIgnoreFilter && _channels.Count == 0)
                return;

            using NativeArray<Entity> casters = _castersQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<CastCue> cues = _castersQuery.ToComponentDataArray<CastCue>(Allocator.Temp);

            float now = Time.unscaledTime;

            _goneChannels.Clear();

            foreach (KeyValuePair<Entity, Channel> pair in _channels)
                _goneChannels.Add(pair.Key);

            for (int i = 0; i < casters.Length; i++)
            {
                CastCue cue = cues[i];
                _goneChannels.Remove(casters[i]);

                // First sight only learns the count, as the arm does: a scene
                // that loads with casts already counted is not a channel opening.
                if (!_channels.TryGetValue(casters[i], out Channel channel))
                {
                    _channels.Add(casters[i], new Channel
                    {
                        Count = cue.Count,
                        LastPulse = float.NegativeInfinity
                    });

                    continue;
                }

                if (cue.Count != channel.Count)
                {
                    channel.Count = cue.Count;

                    if (cue.Effect == SkillEffectKind.Beam)
                        channel.LastPulse = now;
                }

                SkillVfxSet set = null;

                bool channelling = cue.Effect == SkillEffectKind.Beam &&
                                   now - channel.LastPulse <= Mathf.Max(MinChannelGap, cue.Interval * 2f) &&
                                   TryGetSet(cue.BeamVfxId, out set) &&
                                   set.Cast != null;

                if (channelling)
                {
                    Vector3 from = ToPoint(cue.BeamOrigin, set.Lift);
                    Vector3 to = ToPoint(cue.BeamEnd, set.Lift);

                    if (channel.Beam == null)
                    {
                        channel.Beam = _particles.Rent(set.Cast, from, Quaternion.identity, 1f);

                        // Heard once as the channel opens, not once a pulse.
                        PlaySound(AudioCue.SkillCast, _castSounds, cue.BeamVfxId, from);
                    }

                    _particles.Stretch(channel.Beam, from, to, set.Scale);
                }
                else if (channel.Beam != null)
                {
                    _particles.Release(channel.Beam);
                    channel.Beam = null;
                }

                _channels[casters[i]] = channel;
            }

            // A character gone from the world — a player who left mid-channel.
            for (int i = 0; i < _goneChannels.Count; i++)
            {
                _particles.Release(_channels[_goneChannels[i]].Beam);
                _channels.Remove(_goneChannels[i]);
            }
        }

        /// <summary>The element row an event names, or null when the config has none.</summary>
        private ElementVfxSettings ElementOf(in VfxEvent effect)
        {
            int index = (int)effect.Element;
            return index >= 0 && index < _elements.Length ? _elements[index] : null;
        }

        /// <summary>A prefab scale for a radius, never so small the effect vanishes.</summary>
        private float BlastScale(float radius)
            => Mathf.Max(0.3f, radius * _config.BlastScalePerMetre);

        /// <summary>The set an event names, or false when nothing names one.</summary>
        private bool TryGetSet(int vfxId, out SkillVfxSet set)
        {
            set = null;
            return vfxId != 0 && _sets.TryGetValue(vfxId, out set);
        }

        /// <summary>
        /// Which way a cast was pointing, carried in the event's far end — the
        /// field a chain link uses for its other end and nothing else uses at
        /// all.
        /// </summary>
        private static Quaternion Facing(in VfxEvent effect)
        {
            Vector3 forward = ToPoint(effect.EndPosition, 0f) - ToPoint(effect.Position, 0f);

            return forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward)
                : Quaternion.identity;
        }

        /// <summary>
        /// A generic effect's point, lifted to roughly chest height.
        ///
        /// The lift belongs to the element effects and to nothing else: a beam
        /// drawn between two entity origins runs along the floor and reads as a
        /// decal rather than as lightning between two bodies. A skill's set
        /// carries its own lift.
        /// </summary>
        private static Vector3 ToWorld(Unity.Mathematics.float3 position)
            => new Vector3(position.x, position.y + 0.9f, position.z);

        /// <summary>
        /// The point the simulation named, plus whatever lift the set asks for.
        ///
        /// What every authored effect uses, and the lift is the set's own knob
        /// rather than a constant here. An impact has to sit exactly where the
        /// blow landed, and where that is depends on what produced it: a
        /// projectile hit happens at the height the bolt was flying, while a
        /// nova's and a chain's happen at a body — and a body's origin is on the
        /// floor, which is the very reason the chain BEAMS lift themselves by
        /// nine tenths of a metre. A prefab whose pivot is not where its effect
        /// is gets fixed by the same number.
        /// </summary>
        private static Vector3 ToPoint(Unity.Mathematics.float3 position, float lift)
            => new Vector3(position.x, position.y + lift, position.z);

        private static Color ToColor(Unity.Mathematics.float4 color)
            => new Color(color.x, color.y, color.z, color.w);

        private void OnDisable()
        {
            // Trails are transforms in the scene, not events: left alone they
            // would hang in the air where the last projectile died.
            _trails.Clear();
            _channels.Clear();
            _particles.Clear();

            if (!_hasWorld)
                return;

            _numbers.Clear();
        }

        private void OnDestroy()
        {
            // Netcode disposes its worlds before the scene is torn down when play mode ends.
            if (_hasWorld && World.DefaultGameObjectInjectionWorld is { IsCreated: true })
            {
                _eventsQuery.Dispose();
                _flyingQuery.Dispose();
                _castersQuery.Dispose();
            }
        }
    }
}
