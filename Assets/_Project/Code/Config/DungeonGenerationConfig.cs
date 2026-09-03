using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Where the seed for a run comes from.
    ///
    /// This matters far beyond convenience: the dungeon is never transmitted,
    /// only the seed is. Every player regenerates the identical layout locally,
    /// so the choice of seed source is the single point that has to become
    /// "whatever the host sent" once coop arrives.
    /// </summary>
    public enum DungeonSeedMode
    {
        /// <summary>Same dungeon every launch — the mode to profile in.</summary>
        Fixed,

        /// <summary>A fresh dungeon per session, still one seed for everyone.</summary>
        RandomPerSession
    }

    /// <summary>
    /// Dungeon generation, geometry and encounter parameters.
    ///
    /// A ScriptableObject rather than constants for the same reason as the
    /// other configs: layout size and room mix are tuned by looking at the
    /// result, not by reasoning about numbers, and that loop must not require a
    /// recompile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonGenerationConfig",
        menuName = "Together We Fall/Dungeon Generation Config")]
    public sealed class DungeonGenerationConfig : ScriptableObject
    {
        [Header("Seed")]
        [SerializeField] private DungeonSeedMode _seedMode = DungeonSeedMode.Fixed;

        [Tooltip("Seed used in Fixed mode. Any non-zero value; zero is remapped " +
                 "because a zero state would make the generator degenerate.")]
        [SerializeField] private uint _fixedSeed = 20260902u;

        [Header("Grid")]
        [Tooltip("Dungeon size in cells. The world size is this times Cell Size.")]
        [SerializeField, Range(24, 160)] private int _gridWidth = 64;
        [SerializeField, Range(24, 160)] private int _gridHeight = 64;

        [Tooltip("World units per cell. Also the wall thickness and the corridor " +
                 "granularity, so values below ~1.5 make corridors too tight for " +
                 "a crowd to flow through.")]
        [SerializeField] private float _cellSize = 2f;

        [Header("BSP partitioning")]
        [Tooltip("How many times the grid may be halved. Each level doubles the " +
                 "possible room count: depth 4 yields up to 16 rooms.")]
        [SerializeField, Range(1, 6)] private int _maxSplitDepth = 4;

        [Tooltip("Smallest partition a split may produce, in cells. Raising it " +
                 "yields fewer, larger rooms regardless of split depth.")]
        [SerializeField, Range(6, 40)] private int _minLeafCells = 12;

        [Header("Rooms")]
        [SerializeField, Range(3, 40)] private int _minRoomCells = 6;
        [SerializeField, Range(3, 60)] private int _maxRoomCells = 16;

        [Tooltip("Corridor width in cells. Two or more keeps a wave of enemies " +
                 "from single-filing through a doorway.")]
        [SerializeField, Range(1, 6)] private int _corridorWidthCells = 3;

        [Header("Room mix")]
        [Tooltip("How many treasure rooms to place. They are put in dead ends " +
                 "first — the detour is what makes them a choice.")]
        [SerializeField, Range(0, 6)] private int _treasureRoomCount = 2;

        [Tooltip("Share of the remaining rooms that become combat rooms; the " +
                 "rest stay empty transit rooms so the pacing has gaps in it.")]
        [SerializeField, Range(0f, 1f)] private float _combatRoomShare = 0.65f;

        [Header("Geometry")]
        [SerializeField] private float _wallHeight = 4f;
        [SerializeField] private float _floorThickness = 0.4f;

        [Header("Encounters")]
        [Tooltip("Enemies spawned when a player first enters a combat room.")]
        [SerializeField, Range(1, 500)] private int _enemiesPerCombatRoom = 40;

        [Tooltip("Multiplier applied to that count in the boss room. There is no " +
                 "boss entity yet, so for now the boss room is simply the " +
                 "heaviest fight on the floor.")]
        [SerializeField, Range(1f, 10f)] private float _bossRoomEnemyMultiplier = 3f;

        [Tooltip("Grace period after a room activates before it may be counted " +
                 "as cleared. Without it a room is 'cleared' in the frame " +
                 "between the order and the spawn, when it is simply empty.")]
        [SerializeField] private float _roomClearGraceSeconds = 2f;

        public DungeonSeedMode SeedMode => _seedMode;
        public uint FixedSeed => _fixedSeed;

        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public float CellSize => _cellSize;

        public int MaxSplitDepth => _maxSplitDepth;
        public int MinLeafCells => _minLeafCells;

        public int MinRoomCells => _minRoomCells;
        public int MaxRoomCells => _maxRoomCells;
        public int CorridorWidthCells => _corridorWidthCells;

        public int TreasureRoomCount => _treasureRoomCount;
        public float CombatRoomShare => _combatRoomShare;

        public float WallHeight => _wallHeight;
        public float FloorThickness => _floorThickness;

        public int EnemiesPerCombatRoom => _enemiesPerCombatRoom;
        public float BossRoomEnemyMultiplier => _bossRoomEnemyMultiplier;
        public float RoomClearGraceSeconds => _roomClearGraceSeconds;
    }
}
