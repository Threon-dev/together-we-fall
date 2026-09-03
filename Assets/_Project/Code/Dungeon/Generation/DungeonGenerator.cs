using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Builds a floor by binary space partitioning: split the grid until the
    /// pieces are room-sized, put one room in each piece, then join sibling
    /// pieces with corridors.
    ///
    /// BSP rather than a template library, deliberately. Templates would need a
    /// set of authored room prefabs and therefore a runtime asset-loading module
    /// before a single room could be walked through; BSP needs nothing but
    /// numbers. It also gives the property this project actually cares about for
    /// free: the tree guarantees a connected floor with no isolated pockets,
    /// which a random-walk maze does not.
    ///
    /// Every decision here comes from one Unity.Mathematics.Random seeded once.
    /// That is the contract with coop: two machines with the same seed and the
    /// same settings produce identical floors, so the network only ever carries
    /// the seed. Nothing in this file may consult time, frame count,
    /// UnityEngine.Random, or the iteration order of a hash container.
    /// </summary>
    public sealed class DungeonGenerator
    {
        private readonly DungeonRoomTypeAssigner _typeAssigner = new DungeonRoomTypeAssigner();

        private readonly List<BspNode> _nodes = new List<BspNode>();
        private readonly List<int> _pendingNodes = new List<int>();
        private readonly List<int> _pendingDepths = new List<int>();
        private readonly List<DungeonRoom> _rooms = new List<DungeonRoom>();
        private readonly List<DungeonCorridor> _corridors = new List<DungeonCorridor>();
        private readonly List<DungeonRoomLink> _links = new List<DungeonRoomLink>();
        private readonly List<int> _leftRooms = new List<int>();
        private readonly List<int> _rightRooms = new List<int>();
        private readonly List<int> _gatherStack = new List<int>();

        public DungeonLayout Generate(DungeonGenerationSettings settings, uint seed)
        {
            settings = DungeonGenerationSettings.Sanitized(settings);

            // CreateFromIndex hashes the index, so neighbouring seeds give
            // unrelated floors instead of near-identical ones. Seed 0 is
            // remapped because a zero state never advances.
            var random = Random.CreateFromIndex(seed == 0u ? 1u : seed);

            BuildPartitionTree(settings, ref random);
            PlaceRooms(settings, ref random);
            ConnectSubtrees(settings, ref random);

            DungeonRoom[] rooms = _rooms.ToArray();
            DungeonRoomLink[] links = _links.ToArray();

            _typeAssigner.Assign(
                rooms, links, settings, ref random,
                out int entranceIndex, out int bossIndex);

            CarveCells(settings, rooms, out DungeonCellKind[] cells, out int[] cellRoom);

            return new DungeonLayout(
                seed,
                settings.GridSize,
                settings.CellSize,
                cells,
                cellRoom,
                rooms,
                _corridors.ToArray(),
                links,
                entranceIndex,
                bossIndex);
        }

        // ─────────────────────────────────────────────────────────────────
        // Partitioning
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Splits the grid into leaves, iteratively.
        ///
        /// An explicit worklist rather than recursion: the traversal order is
        /// what feeds the random stream, and an explicit list makes that order
        /// something you can read off the code rather than infer from the call
        /// stack. Determinism is the feature here, not stack depth.
        /// </summary>
        private void BuildPartitionTree(in DungeonGenerationSettings settings, ref Random random)
        {
            _nodes.Clear();
            _pendingNodes.Clear();
            _pendingDepths.Clear();

            _nodes.Add(new BspNode
            {
                Min = int2.zero,
                Size = settings.GridSize,
                Left = -1,
                Right = -1,
                RoomIndex = -1
            });

            _pendingNodes.Add(0);
            _pendingDepths.Add(0);

            for (int cursor = 0; cursor < _pendingNodes.Count; cursor++)
            {
                int index = _pendingNodes[cursor];
                int depth = _pendingDepths[cursor];

                if (depth >= settings.MaxSplitDepth)
                    continue;

                BspNode node = _nodes[index];
                if (!TrySplit(node, settings, ref random, out BspNode left, out BspNode right))
                    continue;

                node.Left = _nodes.Count;
                _nodes.Add(left);

                node.Right = _nodes.Count;
                _nodes.Add(right);

                _nodes[index] = node;

                _pendingNodes.Add(node.Left);
                _pendingDepths.Add(depth + 1);
                _pendingNodes.Add(node.Right);
                _pendingDepths.Add(depth + 1);
            }
        }

        private static bool TrySplit(
            in BspNode node,
            in DungeonGenerationSettings settings,
            ref Random random,
            out BspNode left,
            out BspNode right)
        {
            left = default;
            right = default;

            int minLeaf = settings.MinLeafCells;
            bool canSplitX = node.Size.x >= minLeaf * 2;
            bool canSplitY = node.Size.y >= minLeaf * 2;

            if (!canSplitX && !canSplitY)
                return false;

            bool splitAlongX;
            if (canSplitX && canSplitY)
            {
                // Cut the long side first. Left to chance, the partition drifts
                // towards long thin slivers, and a corridor drawn between the
                // centres of two slivers runs the whole length of the floor.
                if (node.Size.x * 4 > node.Size.y * 5)
                    splitAlongX = true;
                else if (node.Size.y * 4 > node.Size.x * 5)
                    splitAlongX = false;
                else
                    splitAlongX = random.NextBool();
            }
            else
            {
                splitAlongX = canSplitX;
            }

            int length = splitAlongX ? node.Size.x : node.Size.y;
            int cut = random.NextInt(minLeaf, length - minLeaf + 1);

            left = node;
            right = node;
            left.Left = left.Right = right.Left = right.Right = -1;
            left.RoomIndex = right.RoomIndex = -1;

            if (splitAlongX)
            {
                left.Size = new int2(cut, node.Size.y);
                right.Min = new int2(node.Min.x + cut, node.Min.y);
                right.Size = new int2(node.Size.x - cut, node.Size.y);
            }
            else
            {
                left.Size = new int2(node.Size.x, cut);
                right.Min = new int2(node.Min.x, node.Min.y + cut);
                right.Size = new int2(node.Size.x, node.Size.y - cut);
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Rooms
        // ─────────────────────────────────────────────────────────────────

        private void PlaceRooms(in DungeonGenerationSettings settings, ref Random random)
        {
            _rooms.Clear();

            for (int i = 0; i < _nodes.Count; i++)
            {
                BspNode node = _nodes[i];
                if (!node.IsLeaf)
                    continue;

                int2 size = new int2(
                    PickRoomExtent(node.Size.x, settings, ref random),
                    PickRoomExtent(node.Size.y, settings, ref random));

                // The one-cell inset on every side is what keeps a wall between
                // rooms that sit in adjacent leaves.
                int2 min = new int2(
                    node.Min.x + 1 + random.NextInt(0, node.Size.x - 2 - size.x + 1),
                    node.Min.y + 1 + random.NextInt(0, node.Size.y - 2 - size.y + 1));

                node.RoomIndex = _rooms.Count;
                _nodes[i] = node;

                _rooms.Add(new DungeonRoom
                {
                    Id = _rooms.Count,
                    Min = min,
                    Size = size,
                    Type = DungeonRoomType.Transit,
                    DistanceFromEntrance = 0,
                    Degree = 0
                });
            }
        }

        private static int PickRoomExtent(
            int leafExtent, in DungeonGenerationSettings settings, ref Random random)
        {
            int maxExtent = math.min(settings.MaxRoomCells, leafExtent - 2);
            int minExtent = math.min(settings.MinRoomCells, maxExtent);
            return random.NextInt(minExtent, maxExtent + 1);
        }

        // ─────────────────────────────────────────────────────────────────
        // Corridors
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Joins the two halves of every internal node with one corridor.
        ///
        /// This is what makes the floor provably connected: each node hands its
        /// parent a single connected component, and the parent links the two it
        /// receives. The result is a spanning tree over the rooms — no isolated
        /// pockets, and every room reachable from the entrance.
        /// </summary>
        private void ConnectSubtrees(in DungeonGenerationSettings settings, ref Random random)
        {
            _corridors.Clear();
            _links.Clear();

            for (int i = 0; i < _nodes.Count; i++)
            {
                BspNode node = _nodes[i];
                if (node.IsLeaf)
                    continue;

                GatherRooms(node.Left, _leftRooms);
                GatherRooms(node.Right, _rightRooms);

                if (_leftRooms.Count == 0 || _rightRooms.Count == 0)
                    continue;

                FindClosestPair(_leftRooms, _rightRooms, out int roomA, out int roomB);
                CarveCorridor(roomA, roomB, settings, ref random);

                _links.Add(new DungeonRoomLink { RoomA = roomA, RoomB = roomB });
            }
        }

        private void GatherRooms(int nodeIndex, List<int> result)
        {
            result.Clear();
            _gatherStack.Clear();
            _gatherStack.Add(nodeIndex);

            for (int cursor = 0; cursor < _gatherStack.Count; cursor++)
            {
                BspNode node = _nodes[_gatherStack[cursor]];

                if (node.IsLeaf)
                {
                    if (node.RoomIndex >= 0)
                        result.Add(node.RoomIndex);

                    continue;
                }

                _gatherStack.Add(node.Left);
                _gatherStack.Add(node.Right);
            }
        }

        private void FindClosestPair(List<int> left, List<int> right, out int roomA, out int roomB)
        {
            roomA = left[0];
            roomB = right[0];

            int bestDistance = int.MaxValue;

            for (int a = 0; a < left.Count; a++)
            {
                int2 centerA = _rooms[left[a]].CenterCell;

                for (int b = 0; b < right.Count; b++)
                {
                    int2 delta = centerA - _rooms[right[b]].CenterCell;
                    int distance = delta.x * delta.x + delta.y * delta.y;

                    // Strictly closer, so an exact tie resolves to the earlier
                    // pair rather than to whichever the loop reached last.
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    roomA = left[a];
                    roomB = right[b];
                }
            }
        }

        /// <summary>
        /// Carves an L-shaped corridor between two room centres as two
        /// rectangles. Which leg comes first is random, which is what stops
        /// every junction on the floor from having the same shape.
        /// </summary>
        private void CarveCorridor(
            int roomA, int roomB, in DungeonGenerationSettings settings, ref Random random)
        {
            int2 from = _rooms[roomA].CenterCell;
            int2 to = _rooms[roomB].CenterCell;
            int width = settings.CorridorWidthCells;

            if (random.NextBool())
            {
                AddHorizontalRun(from.x, to.x, from.y, width, settings);
                AddVerticalRun(from.y, to.y, to.x, width, settings);
            }
            else
            {
                AddVerticalRun(from.y, to.y, from.x, width, settings);
                AddHorizontalRun(from.x, to.x, to.y, width, settings);
            }
        }

        private void AddHorizontalRun(
            int fromX, int toX, int row, int width, in DungeonGenerationSettings settings)
        {
            int half = (width - 1) / 2;

            AddCorridorRect(
                new int2(math.min(fromX, toX), row - half),
                new int2(math.abs(toX - fromX) + 1, width),
                settings);
        }

        private void AddVerticalRun(
            int fromY, int toY, int column, int width, in DungeonGenerationSettings settings)
        {
            int half = (width - 1) / 2;

            AddCorridorRect(
                new int2(column - half, math.min(fromY, toY)),
                new int2(width, math.abs(toY - fromY) + 1),
                settings);
        }

        /// <summary>
        /// Clamps a corridor into the grid, leaving the outermost ring solid so
        /// the floor is always enclosed by a wall.
        /// </summary>
        private void AddCorridorRect(
            int2 min, int2 size, in DungeonGenerationSettings settings)
        {
            int2 clampedMin = math.max(min, new int2(1, 1));
            int2 clampedMax = math.min(min + size, settings.GridSize - 1);
            int2 clampedSize = clampedMax - clampedMin;

            if (clampedSize.x <= 0 || clampedSize.y <= 0)
                return;

            _corridors.Add(new DungeonCorridor { Min = clampedMin, Size = clampedSize });
        }

        // ─────────────────────────────────────────────────────────────────
        // Rasterisation
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Turns rectangles into the cell grid that geometry and the navmesh are
        /// built from. Rooms are painted first and corridors never overwrite
        /// them, so a corridor crossing a room does not steal its cells and the
        /// room keeps its own footprint for occupancy tests.
        /// </summary>
        private void CarveCells(
            in DungeonGenerationSettings settings,
            DungeonRoom[] rooms,
            out DungeonCellKind[] cells,
            out int[] cellRoom)
        {
            int cellCount = settings.GridSize.x * settings.GridSize.y;
            cells = new DungeonCellKind[cellCount];
            cellRoom = new int[cellCount];

            for (int i = 0; i < cellCount; i++)
                cellRoom[i] = -1;

            for (int r = 0; r < rooms.Length; r++)
            {
                DungeonRoom room = rooms[r];

                for (int y = room.Min.y; y < room.Min.y + room.Size.y; y++)
                {
                    for (int x = room.Min.x; x < room.Min.x + room.Size.x; x++)
                    {
                        int index = y * settings.GridSize.x + x;
                        cells[index] = DungeonCellKind.RoomFloor;
                        cellRoom[index] = room.Id;
                    }
                }
            }

            for (int c = 0; c < _corridors.Count; c++)
            {
                DungeonCorridor corridor = _corridors[c];

                for (int y = corridor.Min.y; y < corridor.Min.y + corridor.Size.y; y++)
                {
                    for (int x = corridor.Min.x; x < corridor.Min.x + corridor.Size.x; x++)
                    {
                        int index = y * settings.GridSize.x + x;
                        if (cells[index] == DungeonCellKind.Solid)
                            cells[index] = DungeonCellKind.CorridorFloor;
                    }
                }
            }
        }

        /// <summary>One node of the partition tree. Leaves carry a room.</summary>
        private struct BspNode
        {
            public int2 Min;
            public int2 Size;
            public int Left;
            public int Right;
            public int RoomIndex;

            public bool IsLeaf => Left < 0;
        }
    }
}
