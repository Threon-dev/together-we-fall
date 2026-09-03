using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using TogetherWeFall.Spawning;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// Manual wave trigger for measurements: Space spawns one wave.
    ///
    /// This is the second (and for now the last) bridge from GameObject into
    /// ECS besides the player position. It deliberately decides nothing itself
    /// — it only increments an order counter, while how many to spawn and where
    /// is up to WaveSpawnSystem. So when spawning becomes server-side, this file
    /// simply disappears without taking any gameplay logic with it.
    /// </summary>
    public sealed class DebugSpawnTrigger : MonoBehaviour
    {
        private EntityManager _entityManager;
        private EntityQuery _spawnerQuery;
        private InputAction _spawnWaveAction;
        private bool _hasWorld;

        public void Initialize()
        {
            _spawnWaveAction = new InputAction("SpawnWave", InputActionType.Button);
            _spawnWaveAction.AddBinding("<Keyboard>/space");
            _spawnWaveAction.AddBinding("<Gamepad>/buttonNorth");
            _spawnWaveAction.Enable();

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(DebugSpawnTrigger)}] ECS world is unavailable — spawning is disabled.",
                    this);
                return;
            }

            _entityManager = world.EntityManager;
            _spawnerQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<WaveSpawnOrder>());
            _hasWorld = true;
        }

        private void Update()
        {
            if (!_hasWorld || !_spawnWaveAction.WasPressedThisFrame())
                return;

            if (_spawnerQuery.IsEmptyIgnoreFilter)
            {
                Debug.LogWarning(
                    $"[{nameof(DebugSpawnTrigger)}] No spawner in the scene. " +
                    "Check that WaveSpawnerAuthoring lives inside a SubScene.");
                return;
            }

            Entity spawner = _spawnerQuery.GetSingletonEntity();

            // Aimed at no room and sized by the spawner default: in the arena
            // that is the only kind of order there is, and in a dungeon it means
            // "wherever, however many you normally would".
            _entityManager.GetBuffer<WaveSpawnOrder>(spawner).Add(new WaveSpawnOrder
            {
                RoomId = SpawnPoint.AnyRoom,
                Count = 0
            });
        }

        private void OnDestroy()
        {
            _spawnWaveAction?.Dispose();
        }
    }
}
