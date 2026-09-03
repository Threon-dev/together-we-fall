using Unity.Mathematics;

namespace TogetherWeFall.Dungeon
{
    /// <summary>What occupies one cell of the dungeon grid.</summary>
    public enum DungeonCellKind : byte
    {
        Solid = 0,
        RoomFloor = 1,
        CorridorFloor = 2
    }

    /// <summary>
    /// What a room is for.
    ///
    /// The type is decided during generation and never changes, which is why it
    /// travels into ECS as plain data: systems downstream only ever read it.
    /// </summary>
    public enum DungeonRoomType : byte
    {
        Entrance = 0,
        Transit = 1,
        Combat = 2,
        Treasure = 3,
        Boss = 4
    }

    /// <summary>
    /// One room, in grid coordinates.
    ///
    /// A blittable struct on purpose: the very same values are copied into the
    /// ECS room buffer without a conversion step, so there is one description of
    /// a room in the project rather than a managed one and an entity one that
    /// can drift apart.
    /// </summary>
    public struct DungeonRoom
    {
        public int Id;

        /// <summary>Lower-left cell of the room, inclusive.</summary>
        public int2 Min;

        /// <summary>Room size in cells.</summary>
        public int2 Size;

        public DungeonRoomType Type;

        /// <summary>Corridor hops from the entrance room. Drives boss and treasure placement.</summary>
        public int DistanceFromEntrance;

        /// <summary>How many corridors touch this room. Degree 1 means a dead end.</summary>
        public int Degree;

        public int2 CenterCell => Min + Size / 2;
        public int CellCount => Size.x * Size.y;
    }

    /// <summary>One axis-aligned corridor rectangle in grid coordinates.</summary>
    public struct DungeonCorridor
    {
        public int2 Min;
        public int2 Size;
    }

    /// <summary>
    /// An edge of the room graph. Kept separately from the corridor rectangles
    /// because one L-shaped connection is two rectangles but only one edge, and
    /// distance-from-entrance must not count it twice.
    /// </summary>
    public struct DungeonRoomLink
    {
        public int RoomA;
        public int RoomB;
    }

    /// <summary>
    /// The generated floor: a cell grid, the rooms, and the corridors joining
    /// them.
    ///
    /// Managed arrays rather than native containers, deliberately. Generation
    /// runs once per floor on the main thread at load time and no job ever
    /// touches the result, so native memory would buy nothing and cost a
    /// lifetime to get wrong. The elements themselves stay blittable, so if this
    /// ever does move into a job, only the containers change.
    ///
    /// The layout is never replicated. Every player derives it from the seed,
    /// which is the whole reason generation is a pure function of that seed.
    /// </summary>
    public sealed class DungeonLayout
    {
        public DungeonLayout(
            uint seed,
            int2 gridSize,
            float cellSize,
            DungeonCellKind[] cells,
            int[] cellRoom,
            DungeonRoom[] rooms,
            DungeonCorridor[] corridors,
            DungeonRoomLink[] links,
            int entranceRoomIndex,
            int bossRoomIndex)
        {
            Seed = seed;
            GridSize = gridSize;
            CellSize = cellSize;
            Cells = cells;
            CellRoom = cellRoom;
            Rooms = rooms;
            Corridors = corridors;
            Links = links;
            EntranceRoomIndex = entranceRoomIndex;
            BossRoomIndex = bossRoomIndex;

            // The grid is centred on the world origin so the dungeon does not
            // drift off into large coordinates as it grows — float precision in
            // navmesh queries degrades with distance from the origin.
            Origin = new float3(
                -gridSize.x * cellSize * 0.5f,
                0f,
                -gridSize.y * cellSize * 0.5f);
        }

        public uint Seed { get; }
        public int2 GridSize { get; }
        public float CellSize { get; }

        /// <summary>World position of the grid's lower-left corner, at floor level.</summary>
        public float3 Origin { get; }

        public DungeonCellKind[] Cells { get; }

        /// <summary>Room id per cell, or -1 for corridor and solid cells.</summary>
        public int[] CellRoom { get; }

        public DungeonRoom[] Rooms { get; }
        public DungeonCorridor[] Corridors { get; }
        public DungeonRoomLink[] Links { get; }

        public int EntranceRoomIndex { get; }

        /// <summary>-1 when the floor is too small to hold a room past the entrance.</summary>
        public int BossRoomIndex { get; }

        public int CellIndex(int x, int y) => y * GridSize.x + x;

        public bool IsInside(int x, int y)
            => x >= 0 && y >= 0 && x < GridSize.x && y < GridSize.y;

        public DungeonCellKind CellAt(int x, int y)
            => IsInside(x, y) ? Cells[CellIndex(x, y)] : DungeonCellKind.Solid;

        public bool IsFloor(int x, int y) => CellAt(x, y) != DungeonCellKind.Solid;

        /// <summary>Centre of a cell, at floor level.</summary>
        public float3 CellCenter(int2 cell) => new float3(
            Origin.x + (cell.x + 0.5f) * CellSize,
            0f,
            Origin.z + (cell.y + 0.5f) * CellSize);

        /// <summary>Centre of a grid rectangle, at floor level.</summary>
        public float3 RectCenter(int2 min, int2 size) => new float3(
            Origin.x + (min.x + size.x * 0.5f) * CellSize,
            0f,
            Origin.z + (min.y + size.y * 0.5f) * CellSize);

        /// <summary>Half-extents of a grid rectangle on the ground plane.</summary>
        public float2 RectExtents(int2 size) => new float2(size.x, size.y) * CellSize * 0.5f;

        public float3 RoomCenter(int roomIndex) =>
            RectCenter(Rooms[roomIndex].Min, Rooms[roomIndex].Size);

        /// <summary>Where the player starts the floor, at floor level.</summary>
        public float3 EntranceWorldPosition =>
            Rooms.Length == 0 ? float3.zero : RoomCenter(EntranceRoomIndex);
    }
}
