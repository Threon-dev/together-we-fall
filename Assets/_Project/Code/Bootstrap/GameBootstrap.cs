using Unity.Entities;
using UnityEngine;
using TogetherWeFall.CameraRig;
using TogetherWeFall.DebugTools;
using TogetherWeFall.Enemies;
using TogetherWeFall.Player;

namespace TogetherWeFall.Bootstrap
{
    /// <summary>
    /// The single initialisation point for the scene. Project conventions rule
    /// out statics and DI containers, so instead of "everyone finds themselves
    /// somehow" there is one explicit order: references are handed out from
    /// here, and who depends on whom is visible at a glance.
    ///
    /// The order matters: the camera must receive its target and settle into
    /// place before the input reader starts converting screen coordinates
    /// through it.
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

        private EntityQuery _enemyQuery;
        private bool _hasEnemyQuery;

        private void Awake()
        {
            if (!ValidateReferences())
                return;

            _cameraRig.Initialize(_player.transform);
            _input.Initialize(_cameraRig);
            _player.Initialize(_input);

            _positionPublisher.Initialize();
            _spawnTrigger.Initialize();

            _hud.Initialize(CreateEnemyCountProvider());
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

        private void OnDestroy()
        {
            if (_hasEnemyQuery)
                _enemyQuery.Dispose();
        }

        private bool ValidateReferences()
        {
            // Catch a forgotten reference with one clear message instead of a
            // NullReference somewhere in Update a frame after startup.
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
