using Unity.Mathematics;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Generation parameters as plain data.
    ///
    /// The generator takes this struct rather than the ScriptableObject for the
    /// same reason the enemy systems take a singleton rather than the asset: a
    /// host must be able to run generation from numbers it owns, without an
    /// asset reference from a client anywhere in the call.
    /// </summary>
    public struct DungeonGenerationSettings
    {
        public int2 GridSize;
        public float CellSize;

        public int MaxSplitDepth;
        public int MinLeafCells;

        public int MinRoomCells;
        public int MaxRoomCells;
        public int CorridorWidthCells;

        public int TreasureRoomCount;
        public float CombatRoomShare;

        /// <summary>
        /// Clamps the settings into a range the algorithm can actually satisfy.
        ///
        /// Done once here rather than checked inside the recursion, because the
        /// invariants are mutually dependent: a leaf must hold a room plus a
        /// one-cell margin on each side, or room placement quietly produces
        /// zero-sized rooms and the floor comes out disconnected — a failure
        /// that looks like a generation bug and is really a settings bug.
        /// </summary>
        public static DungeonGenerationSettings Sanitized(DungeonGenerationSettings settings)
        {
            settings.GridSize = math.max(settings.GridSize, new int2(16, 16));
            settings.CellSize = math.max(settings.CellSize, 0.5f);

            settings.MinRoomCells = math.max(settings.MinRoomCells, 3);
            settings.MaxRoomCells = math.max(settings.MaxRoomCells, settings.MinRoomCells);

            // A leaf holds a room plus one cell of solid rock on each side, so
            // that two rooms in neighbouring leaves never share a wall face.
            settings.MinLeafCells = math.max(settings.MinLeafCells, settings.MinRoomCells + 2);

            // And the whole grid must hold at least one leaf.
            settings.MinLeafCells = math.min(
                settings.MinLeafCells,
                math.max(5, math.cmin(settings.GridSize) / 2));

            settings.MinRoomCells = math.min(settings.MinRoomCells, settings.MinLeafCells - 2);
            settings.MaxRoomCells = math.max(settings.MaxRoomCells, settings.MinRoomCells);

            settings.MaxSplitDepth = math.clamp(settings.MaxSplitDepth, 0, 8);

            // A corridor wider than the narrowest room would punch through the
            // room's walls where it meets it.
            settings.CorridorWidthCells =
                math.clamp(settings.CorridorWidthCells, 1, settings.MinRoomCells);

            settings.TreasureRoomCount = math.max(settings.TreasureRoomCount, 0);
            settings.CombatRoomShare = math.saturate(settings.CombatRoomShare);

            return settings;
        }
    }
}
