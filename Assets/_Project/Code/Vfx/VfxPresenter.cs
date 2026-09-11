using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Enemies;

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
    /// On VFX Graph: it is not in this project, and a .vfx asset is a serialised
    /// graph that cannot be written as text. What is here instead is pooled line
    /// renderers, which cover the two shapes this game needs — a chain link and
    /// a shockwave ring — and cost nothing per frame. Swapping in VFX Graph
    /// means rewriting the four Handle methods below and nothing else, because
    /// everything upstream only ever said what happened.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class VfxPresenter : MonoBehaviour
    {
        [SerializeField] private VfxConfig _config;

        [Tooltip("Material for the chain lines and blast rings.")]
        [SerializeField] private Material _lineMaterial;

        [Tooltip("Panel the damage numbers are drawn into. Without it everything " +
                 "else still works and the numbers are simply absent.")]
        [SerializeField] private UIDocument _document;

        private readonly VfxLinePool _pool = new VfxLinePool();
        private readonly DamageNumberPool _numbers = new DamageNumberPool();
        private readonly StatusIconPool _icons = new StatusIconPool();
        private readonly HitStopController _hitStop = new HitStopController();

        private TopDownCameraRig _camera;
        private EntityManager _entityManager;
        private EntityQuery _eventsQuery;
        private EntityQuery _afflictedQuery;
        private bool _hasWorld;

        private int _deathsInWindow;
        private float _windowRemaining;
        private float _massKillCooldown;

        private float _shakeCooldown;
        private float _lastShakeStrength;

        public void Initialize(TopDownCameraRig cameraRig)
        {
            _camera = cameraRig;

            if (_config == null || _lineMaterial == null)
            {
                Debug.LogError(
                    $"[{nameof(VfxPresenter)}] No VfxConfig or line material assigned — " +
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

            _pool.Initialize(_config.PoolSize, _lineMaterial, transform);
            _hitStop.Configure(
                _config.HitStopRecoverySeconds,
                _config.HitStopRecoveryCurve,
                _config.HitStopCooldownSeconds);

            if (_document == null)
            {
                Debug.LogWarning(
                    $"[{nameof(VfxPresenter)}] No UIDocument assigned — damage numbers will " +
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

            // Built on first use, not in Initialize: a UIDocument fills its root
            // in OnEnable, and the bootstrap that calls Initialize deliberately
            // runs before every other component in the scene.
            EnsureNumbers();

            if (_shakeCooldown > 0f)
                _shakeCooldown -= unscaled;

            DrainEvents();
            DrawStatusMarkers();
            AdvanceMassKillWindow(unscaled);

            _pool.Tick(unscaled);
            _numbers.Tick(unscaled);
            _hitStop.Tick(unscaled);
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
            }
        }

        private void HandleChainLink(in VfxEvent effect)
        {
            // Lifted to roughly chest height at both ends: a line drawn between
            // two entity origins runs along the floor and reads as a decal
            // rather than as lightning between two bodies.
            Vector3 from = ToWorld(effect.Position);
            Vector3 to = ToWorld(effect.EndPosition);

            // The link lives as long as the jump delay, so one link is on screen
            // until the next appears and the chain reads as travelling.
            float seconds = Mathf.Max(_config.ChainLinkSeconds, effect.Magnitude);

            _pool.AddLink(from, to, ToColor(effect.Color), seconds, _config.ChainLinkWidth);
        }

        /// <summary>
        /// The stroke from the caster to the first body. Same shape as a jump,
        /// drawn heavier and held a little longer — it is the part of the chain
        /// the player aimed, and it should read as the cause rather than as one
        /// more link.
        /// </summary>
        private void HandleBoltStrike(in VfxEvent effect)
        {
            _pool.AddLink(
                ToWorld(effect.Position),
                ToWorld(effect.EndPosition),
                ToColor(effect.Color),
                Mathf.Max(_config.BoltStrikeSeconds, effect.Magnitude),
                _config.BoltStrikeWidth);
        }

        /// <summary>
        /// A ring in the colour of whatever just combined, and nothing else.
        ///
        /// Deliberately no shake and no freeze. A reaction can fire on every hit
        /// that lands in a burning crowd, and punctuating each one would mean
        /// punctuating none of them — the camera never settling and time never
        /// coming back to one. The same reasoning that gives the shake and the
        /// hit-stop the right to refuse, applied one step earlier: some things
        /// simply do not ask.
        ///
        /// It borrows the explosion's timing and width because it is the same
        /// shape drawn smaller, and a pair of numbers of its own on the config
        /// would be two more things to keep in step for no visible gain.
        /// </summary>
        private void HandleElementBurst(in VfxEvent effect)
        {
            _pool.AddRing(
                ToWorld(effect.Position),
                effect.Magnitude,
                ToColor(effect.Color),
                _config.ExplosionSeconds,
                _config.ExplosionWidth);
        }

        private void HandleExplosion(in VfxEvent effect)
        {
            _pool.AddRing(
                ToWorld(effect.Position),
                effect.Magnitude,
                ToColor(effect.Color),
                _config.ExplosionSeconds,
                _config.ExplosionWidth);

            TryShake(_config.ExplosionShake);

            // The freeze goes with the blast rather than with the kills it
            // causes: the kills land a frame later, and by then the moment worth
            // punctuating has gone. Whether it is allowed at all — and how it
            // comes back out — the controller decides.
            _hitStop.Request(_config.HitStopSeconds, _config.HitStopScale);
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
            if (_numbers.IsReady || _document == null || _camera == null)
                return;

            VisualElement root = _document.rootVisualElement;
            if (root == null)
                return;

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

        private static Vector3 ToWorld(Unity.Mathematics.float3 position)
            => new Vector3(position.x, position.y + 0.9f, position.z);

        private static Color ToColor(Unity.Mathematics.float4 color)
            => new Color(color.x, color.y, color.z, color.w);

        private void OnDisable()
        {
            // Time scale is a global. Leaving play mode mid-freeze would
            // otherwise leave the editor running at five percent speed with
            // nothing on screen to explain why.
            _hitStop.Release();

            if (!_hasWorld)
                return;

            _pool.Clear();
            _numbers.Clear();
        }

        private void OnDestroy()
        {
            _hitStop.Release();

            if (_hasWorld)
                _eventsQuery.Dispose();
        }
    }
}
