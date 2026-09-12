using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Audio;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Curtain;
using TogetherWeFall.DebugTools;
using TogetherWeFall.Dungeon;
using TogetherWeFall.Enemies;
using TogetherWeFall.Lobby;
using TogetherWeFall.Player;
using TogetherWeFall.UI;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Bootstrap
{
    /// <summary>
    /// The single initialisation point for the scene. Project conventions rule
    /// out statics and DI containers, so instead of "everyone finds themselves
    /// somehow" there is one explicit order: references are handed out from
    /// here, and who depends on whom is visible at a glance.
    ///
    /// The order matters twice over. The dungeon is built first, because the
    /// player has to be put down on a floor that exists — otherwise the first
    /// frames are spent falling through an empty world. Then the camera receives
    /// its target and settles into place before the input reader starts
    /// converting screen coordinates through it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private TopDownCameraRig _cameraRig;
        [SerializeField] private PlayerMotor _player;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private PlayerPositionPublisher _positionPublisher;
        [SerializeField] private DebugHud _hud;
        [SerializeField] private DebugSpawnTrigger _spawnTrigger;

        [Tooltip("Optional. Present in the dungeon scene and absent in the arena, " +
                 "whose floor is authored by hand.")]
        [SerializeField] private DungeonDirector _dungeon;

        [Tooltip("Optional. Absent in scenes with nothing to interact with or cast at.")]
        [SerializeField] private PlayerActionPublisher _actionPublisher;

        [Tooltip("Optional. Absent in scenes with no loot to carry.")]
        [SerializeField] private InventoryUI _inventoryUI;

        [Tooltip("Optional. Present in the lobby and absent everywhere else — " +
                 "it draws the NPC prompt, the shop, the forge, the portal and " +
                 "the damage meter.")]
        [SerializeField] private LobbyUI _lobbyUI;

        [Tooltip("Optional. The one thing about leaving a scene that needs " +
                 "Unity. Without it the portal fades the screen and nothing " +
                 "loads.")]
        [SerializeField] private SceneLoadBridge _sceneLoader;

        [Tooltip("Optional. Without it the pools and cooldowns still run and " +
                 "simply cannot be seen.")]
        [SerializeField] private PlayerHud _playerHud;

        [Tooltip("Optional. Without it the game plays identically and looks flat.")]
        [SerializeField] private VfxPresenter _vfxPresenter;

        [Tooltip("Optional. Without it the fades still run in the simulation " +
                 "and simply cannot be seen.")]
        [SerializeField] private CurtainPresenter _curtainPresenter;

        [Tooltip("Optional. Without it the game plays identically and silently.")]
        [SerializeField] private AudioPresenter _audioPresenter;

        private readonly DebugRunStatusProbe _statusProbe = new DebugRunStatusProbe();

        private EntityQuery _enemyQuery;
        private bool _hasEnemyQuery;

        private void Awake()
        {
            if (!ValidateReferences())
                return;

            BeginDungeonRun();

            // Asked once and handed to both the motor and the action publisher:
            // "is anything taking the player's input" is one question, and two
            // copies of it would eventually be two answers.
            System.Func<bool> uiCapturing = CreateUiCaptureProbe();

            _cameraRig.Initialize(_player.transform);
            _input.Initialize(_cameraRig);
            _player.Initialize(_input, uiCapturing);

            _positionPublisher.Initialize();
            _spawnTrigger.Initialize();

            if (_inventoryUI != null)
                _inventoryUI.Initialize(_input, _positionPublisher.PlayerId);

            // The same player id, from the same place. The HUD is told who to
            // watch rather than looking for "the character" — in coop there are
            // several and only one of them is this screen's.
            if (_playerHud != null)
                _playerHud.Initialize(_positionPublisher.PlayerId);

            if (_lobbyUI != null)
                _lobbyUI.Initialize(_input, _player.transform, _positionPublisher.PlayerId);

            if (_sceneLoader != null)
                _sceneLoader.Initialize();

            // The panels do not reach into the publisher; the publisher is
            // handed a question to ask. Wiring in one direction from one place
            // is the whole reason this class exists — and two panels are one
            // question, because "is anything taking the clicks" is what the
            // publisher actually wants to know.
            if (_actionPublisher != null)
                _actionPublisher.Initialize(_input, _positionPublisher.PlayerId, uiCapturing);

            // After the camera, because the presenter shakes it.
            if (_vfxPresenter != null)
                _vfxPresenter.Initialize(_cameraRig);

            if (_curtainPresenter != null)
                _curtainPresenter.Initialize();

            // The rig, not the player: the AudioListener sits on the camera, so
            // distance culling has to measure from where Unity is listening or
            // it will drop sounds that are still audible.
            if (_audioPresenter != null)
                _audioPresenter.Initialize(_cameraRig.transform);

            _hud.Initialize(CreateEnemyCountProvider(), CreateStatusProvider());
        }

        /// <summary>
        /// Whether any panel is currently taking the player's clicks, or null
        /// when this scene has no panels at all.
        ///
        /// One delegate rather than a list each caller walks: neither the
        /// publisher nor the motor has any business knowing that panels are what
        /// does it, let alone how many there are.
        ///
        /// It closes over the fields rather than reading them when asked, so it
        /// can be built before the panels are initialised — which it is, because
        /// the motor is wired first.
        /// </summary>
        private System.Func<bool> CreateUiCaptureProbe()
        {
            InventoryUI inventory = _inventoryUI;
            LobbyUI lobby = _lobbyUI;

            if (inventory == null && lobby == null)
                return null;

            return () =>
                (inventory != null && inventory.IsCapturingInput) ||
                (lobby != null && lobby.IsCapturingInput);
        }

        /// <summary>
        /// Generates the floor and puts the player at its entrance.
        ///
        /// The seed is resolved here and passed in, rather than the director
        /// helping itself to one: in coop this call becomes "use the seed the
        /// host sent", and it is worth having exactly one line to change.
        /// </summary>
        private void BeginDungeonRun()
        {
            if (_dungeon == null)
                return;

            _dungeon.Initialize();
            _player.WarpToFloor(_dungeon.BeginRun(_dungeon.ResolveSeed()));
        }

        /// <summary>
        /// The HUD must not know about ECS, so the query lives here and only a
        /// delegate travels there. The query is built once — calling
        /// CreateEntityQuery every frame would cost more than everything the
        /// HUD draws.
        /// </summary>
        private System.Func<int> CreateEnemyCountProvider()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return () => 0;

            _enemyQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EnemyTag>());
            _hasEnemyQuery = true;

            return () => _enemyQuery.CalculateEntityCount();
        }

        /// <summary>
        /// The dungeon/loot readout for the debug overlay, or null when there is
        /// no ECS world to read. The probe itself returns an empty string in a
        /// scene without a run, which the HUD skips.
        /// </summary>
        private System.Func<string> CreateStatusProvider()
        {
            if (!_statusProbe.Initialize())
                return null;

            return _statusProbe.Describe;
        }

        private void OnDestroy()
        {
            if (_hasEnemyQuery)
                _enemyQuery.Dispose();

            _statusProbe.Dispose();
        }

        private bool ValidateReferences()
        {
            // Catch a forgotten reference with one clear message instead of a
            // NullReference somewhere in Update a frame after startup. The
            // dungeon director, the action publisher, the inventory panel, the
            // VFX presenter, the curtain and the audio presenter are not on the
            // list: a scene without any of them still plays.
            if (_cameraRig == null || _player == null || _input == null ||
                _positionPublisher == null || _hud == null || _spawnTrigger == null)
            {
                Debug.LogError(
                    $"[{nameof(GameBootstrap)}] Inspector references are incomplete — " +
                    "the scene was not initialised.", this);
                return false;
            }

            return true;
        }
    }
}
