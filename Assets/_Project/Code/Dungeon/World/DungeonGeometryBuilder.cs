using UnityEngine;

namespace TogetherWeFall.Dungeon
{
    /// <summary>How much geometry a build produced. Logged, not used for logic.</summary>
    public struct DungeonGeometryStats
    {
        public int FloorPieces;
        public int WallPieces;
    }

    /// <summary>
    /// Turns a generated layout into collidable geometry.
    ///
    /// The pieces are box primitives rather than a generated mesh because the
    /// navmesh is baked from physics colliders: a box gives collision, a visible
    /// surface and a navmesh source in one object, with no mesh authoring in
    /// between.
    ///
    /// Cells are merged into horizontal runs before anything is created. Without
    /// that, a 64x64 floor is four thousand GameObjects; with it, a few hundred.
    /// The merge is per row only — merging rectangles as well would roughly
    /// halve the count again, and is the obvious next step if this ever shows up
    /// in a profile. It has not, because it runs once at load.
    /// </summary>
    public sealed class DungeonGeometryBuilder
    {
        private const int SolidKey = -1;
        private const int CorridorKey = 0;

        /// <summary>
        /// Builds the floor into <paramref name="root"/>, replacing whatever was
        /// there. The root is expected to sit at the world origin: the layout
        /// centres itself there, and the navmesh surface on that same object
        /// bakes in its local space.
        /// </summary>
        public DungeonGeometryStats Build(
            DungeonLayout layout,
            DungeonMaterialSet materials,
            float wallHeight,
            float floorThickness,
            Transform root)
        {
            Clear(root);

            return new DungeonGeometryStats
            {
                FloorPieces = BuildFloors(layout, materials, floorThickness, root),
                WallPieces = BuildWalls(layout, materials, wallHeight, root)
            };
        }

        /// <summary>
        /// Removes previous geometry immediately rather than deferring it.
        ///
        /// A regular Destroy only takes effect at the end of the frame, and the
        /// navmesh bake that follows this call collects colliders right now — it
        /// would happily bake the floor that is on its way out on top of the one
        /// being built.
        /// </summary>
        public void Clear(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        private int BuildFloors(
            DungeonLayout layout, DungeonMaterialSet materials, float thickness, Transform root)
        {
            int pieces = 0;

            for (int y = 0; y < layout.GridSize.y; y++)
            {
                int runKey = SolidKey;
                int runStart = 0;

                // One column past the end closes whatever run reaches the edge,
                // so the loop needs no duplicate of the emit code after it.
                for (int x = 0; x <= layout.GridSize.x; x++)
                {
                    int key = x < layout.GridSize.x ? MaterialKey(layout, x, y) : SolidKey;
                    if (key == runKey)
                        continue;

                    if (runKey != SolidKey)
                    {
                        EmitFloorRun(layout, materials, root, runStart, x - 1, y, runKey, thickness);
                        pieces++;
                    }

                    runKey = key;
                    runStart = x;
                }
            }

            return pieces;
        }

        private int BuildWalls(
            DungeonLayout layout, DungeonMaterialSet materials, float height, Transform root)
        {
            int pieces = 0;

            for (int y = 0; y < layout.GridSize.y; y++)
            {
                bool inRun = false;
                int runStart = 0;

                for (int x = 0; x <= layout.GridSize.x; x++)
                {
                    bool isWall = x < layout.GridSize.x && IsWall(layout, x, y);
                    if (isWall == inRun)
                        continue;

                    if (inRun)
                    {
                        EmitWallRun(layout, materials, root, runStart, x - 1, y, height);
                        pieces++;
                    }

                    inRun = isWall;
                    runStart = x;
                }
            }

            return pieces;
        }

        /// <summary>
        /// A wall is solid rock with floor beside it. The diagonals count too:
        /// checking only the four sides leaves a gap at every inside corner,
        /// and a gap in a wall is a hole the navmesh happily walks through.
        /// </summary>
        private static bool IsWall(DungeonLayout layout, int x, int y)
        {
            if (layout.IsFloor(x, y))
                return false;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    if (layout.IsFloor(x + dx, y + dy))
                        return true;
                }
            }

            return false;
        }

        private static int MaterialKey(DungeonLayout layout, int x, int y)
        {
            int index = layout.CellIndex(x, y);
            DungeonCellKind kind = layout.Cells[index];

            if (kind == DungeonCellKind.Solid)
                return SolidKey;

            int roomId = layout.CellRoom[index];
            if (kind == DungeonCellKind.CorridorFloor || roomId < 0)
                return CorridorKey;

            return 1 + (int)layout.Rooms[roomId].Type;
        }

        private static Material MaterialForKey(DungeonMaterialSet materials, int key)
            => key == CorridorKey
                ? materials.CorridorFloor
                : materials.FloorFor((DungeonRoomType)(key - 1));

        private static void EmitFloorRun(
            DungeonLayout layout,
            DungeonMaterialSet materials,
            Transform root,
            int startX,
            int endX,
            int y,
            int key,
            float thickness)
        {
            int length = endX - startX + 1;
            float cellSize = layout.CellSize;

            var center = new Vector3(
                layout.Origin.x + (startX + length * 0.5f) * cellSize,
                -thickness * 0.5f,
                layout.Origin.z + (y + 0.5f) * cellSize);

            CreateBox(
                root,
                $"Floor_{y}_{startX}",
                center,
                new Vector3(length * cellSize, thickness, cellSize),
                MaterialForKey(materials, key));
        }

        private static void EmitWallRun(
            DungeonLayout layout,
            DungeonMaterialSet materials,
            Transform root,
            int startX,
            int endX,
            int y,
            float height)
        {
            int length = endX - startX + 1;
            float cellSize = layout.CellSize;

            var center = new Vector3(
                layout.Origin.x + (startX + length * 0.5f) * cellSize,
                height * 0.5f,
                layout.Origin.z + (y + 0.5f) * cellSize);

            CreateBox(
                root,
                $"Wall_{y}_{startX}",
                center,
                new Vector3(length * cellSize, height, cellSize),
                materials.Wall);
        }

        private static void CreateBox(
            Transform root, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;

            box.transform.SetParent(root, worldPositionStays: false);
            box.transform.localPosition = center;
            box.transform.localScale = size;

            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
