using System.Collections;
using Unity.AI.Navigation;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Runs one floor from seed to playable: generate the layout, build the
    /// geometry, hand it to ECS, bake the navmesh.
    ///
    /// The order is not arbitrary. Everything except the bake is synchronous and
    /// finishes inside Awake, so the floor exists under the player before the
    /// first Update — otherwise the player spends the first frames falling
    /// through a world that has not been built yet. Only the bake is deferred,
    /// and until it finishes the run stays in the Generating phase, which is
    /// what keeps enemies from spawning onto a navmesh that does not exist.
    ///
    /// The seam for coop is one method. ResolveSeed is the only place in the
    /// project that invents a seed; a client will skip it and pass the seed the
    /// host sent straight to BeginRun, and every other line here stays as it is.
    /// </summary>
    public sealed class DungeonDirector : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private DungeonGenerationConfig _config;

        [Header("Scene references")]
        [Tooltip("Surface that bakes the generated geometry. Must collect its " +
                 "children only, using physics colliders.")]
        [SerializeField] private NavMeshSurface _navMeshSurface;

        [Tooltip("Parent for the generated boxes. Expected to sit at the world " +
                 "origin, where the layout centres itself.")]
        [SerializeField] private Transform _geometryRoot;

        [SerializeField] private DungeonMaterialSet _materials = new DungeonMaterialSet();

        private readonly DungeonGenerator _generator = new DungeonGenerator();
        private readonly DungeonGeometryBuilder _geometryBuilder = new DungeonGeometryBuilder();
        private readonly DungeonNavMeshBaker _navMeshBaker = new DungeonNavMeshBaker();
        private readonly DungeonLayoutPublisher _publisher = new DungeonLayoutPublisher();

        private DungeonLayout _layout;
        private bool _initialized;

        /// <summary>The current floor, or null before the first BeginRun.</summary>
        public DungeonLayout Layout => _layout;

        public void Initialize()
        {
            _initialized = ValidateReferences() && _publisher.Initialize();
        }

        /// <summary>
        /// Produces the seed for this run.
        ///
        /// The single point where a dungeon becomes "this dungeon". In coop only
        /// the host will call it; everyone else receives the number and calls
        /// BeginRun with it, which is why the resolution lives here and not
        /// inside the generator.
        /// </summary>
        public uint ResolveSeed()
        {
            if (_config == null || _config.SeedMode == DungeonSeedMode.Fixed)
                return _config != null ? _config.FixedSeed : 1u;

            // Non-deterministic on purpose, and deliberately the only such call
            // in the dungeon code. Everything downstream is a pure function of
            // whatever comes out of here.
            return (uint)System.DateTime.UtcNow.Ticks ^ (uint)Time.frameCount;
        }

        /// <summary>
        /// Builds the floor and returns the world position, at floor level,
        /// where the players start. Returns the world origin if the director was
        /// not initialised, so a misconfigured scene still runs rather than
        /// throwing during Awake.
        /// </summary>
        public Vector3 BeginRun(uint seed, int floorIndex = 0)
        {
            if (!_initialized)
                return Vector3.zero;

            _layout = _generator.Generate(ReadGenerationSettings(), seed);

            DungeonGeometryStats stats = _geometryBuilder.Build(
                _layout, _materials, _config.WallHeight, _config.FloorThickness, _geometryRoot);

            _publisher.Publish(_layout, ReadEncounterSettings(), floorIndex);

            LogFloor(stats);

            StartCoroutine(BakeNavMeshRoutine());

            return _layout.EntranceWorldPosition;
        }

        private IEnumerator BakeNavMeshRoutine()
        {
            float startedAt = Time.realtimeSinceStartup;

            yield return _navMeshBaker.BakeAsync(_navMeshSurface, succeeded =>
            {
                if (!succeeded)
                {
                    // Stay in Generating: no navmesh means no waves, which is a
                    // quiet dungeon rather than a crowd of enemies sliding
                    // through walls with nothing to constrain them.
                    Debug.LogError(
                        $"[{nameof(DungeonDirector)}] Navmesh bake failed — the floor stays " +
                        "in the generating phase and no encounter will start.");
                    return;
                }

                _publisher.SetPhase(DungeonRunPhase.Ready, isRunActive: true);

                Debug.Log(
                    $"[{nameof(DungeonDirector)}] Navmesh baked in " +
                    $"{(Time.realtimeSinceStartup - startedAt) * 1000f:F0} ms. Floor is ready.");
            });
        }

        private DungeonGenerationSettings ReadGenerationSettings() => new DungeonGenerationSettings
        {
            GridSize = new int2(_config.GridWidth, _config.GridHeight),
            CellSize = _config.CellSize,
            MaxSplitDepth = _config.MaxSplitDepth,
            MinLeafCells = _config.MinLeafCells,
            MinRoomCells = _config.MinRoomCells,
            MaxRoomCells = _config.MaxRoomCells,
            CorridorWidthCells = _config.CorridorWidthCells,
            TreasureRoomCount = _config.TreasureRoomCount,
            CombatRoomShare = _config.CombatRoomShare
        };

        private DungeonEncounterSettings ReadEncounterSettings() => new DungeonEncounterSettings
        {
            EnemiesPerCombatRoom = _config.EnemiesPerCombatRoom,
            BossRoomEnemyMultiplier = _config.BossRoomEnemyMultiplier,
            RoomClearGraceSeconds = _config.RoomClearGraceSeconds
        };

        private void LogFloor(in DungeonGeometryStats stats)
        {
            int combat = 0;
            int treasure = 0;

            for (int i = 0; i < _layout.Rooms.Length; i++)
            {
                if (_layout.Rooms[i].Type == DungeonRoomType.Combat)
                    combat++;
                else if (_layout.Rooms[i].Type == DungeonRoomType.Treasure)
                    treasure++;
            }

            Debug.Log(
                $"[{nameof(DungeonDirector)}] Floor {_layout.Seed} generated: " +
                $"{_layout.Rooms.Length} rooms ({combat} combat, {treasure} treasure), " +
                $"{_layout.Corridors.Length} corridor segments, " +
                $"{stats.FloorPieces} floor and {stats.WallPieces} wall pieces.");
        }

        private bool ValidateReferences()
        {
            if (_config == null || _navMeshSurface == null || _geometryRoot == null)
            {
                Debug.LogError(
                    $"[{nameof(DungeonDirector)}] Config, NavMeshSurface or geometry root is " +
                    "not assigned — no dungeon will be generated.", this);
                return false;
            }

            if (!_materials.IsComplete)
            {
                Debug.LogError(
                    $"[{nameof(DungeonDirector)}] The material set is incomplete — rooms would " +
                    "be indistinguishable from each other.", this);
                return false;
            }

            return true;
        }
    }
}
