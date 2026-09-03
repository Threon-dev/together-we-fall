using System.Collections.Generic;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Decides what each room is for.
    ///
    /// Kept apart from the generator because it answers a different question.
    /// The generator answers "what shape is the floor"; this answers "what is
    /// the floor for", and that second answer is the one that will keep changing
    /// as loot, bosses and floor progression arrive. Splitting them means those
    /// changes never touch the partitioning code that is already correct.
    ///
    /// Placement is driven by graph distance from the entrance rather than by
    /// straight-line distance: what makes a treasure room feel like a detour is
    /// the number of rooms walked through to reach it, not how far away it is
    /// on the map.
    /// </summary>
    public sealed class DungeonRoomTypeAssigner
    {
        private readonly List<int> _frontier = new List<int>();
        private readonly List<int> _candidates = new List<int>();

        public void Assign(
            DungeonRoom[] rooms,
            DungeonRoomLink[] links,
            in DungeonGenerationSettings settings,
            ref Random random,
            out int entranceIndex,
            out int bossIndex)
        {
            entranceIndex = -1;
            bossIndex = -1;

            if (rooms.Length == 0)
                return;

            List<int>[] adjacency = BuildAdjacency(rooms, links);

            entranceIndex = FindEntrance(rooms);
            MeasureDistances(rooms, adjacency, entranceIndex);

            bossIndex = rooms.Length > 1 ? FindBoss(rooms, entranceIndex) : -1;

            AssignTreasureRooms(rooms, settings, entranceIndex, bossIndex);
            AssignCombatRooms(rooms, settings, ref random, entranceIndex, bossIndex);

            rooms[entranceIndex].Type = DungeonRoomType.Entrance;

            if (bossIndex >= 0)
                rooms[bossIndex].Type = DungeonRoomType.Boss;
        }

        private static List<int>[] BuildAdjacency(DungeonRoom[] rooms, DungeonRoomLink[] links)
        {
            var adjacency = new List<int>[rooms.Length];
            for (int i = 0; i < rooms.Length; i++)
                adjacency[i] = new List<int>(4);

            for (int i = 0; i < links.Length; i++)
            {
                int a = links[i].RoomA;
                int b = links[i].RoomB;

                if (a == b || a < 0 || b < 0 || a >= rooms.Length || b >= rooms.Length)
                    continue;

                // The partition can hand the same pair to two different levels
                // of the tree. Counting it twice would turn a dead end into a
                // room of degree two and exclude it from treasure placement.
                if (adjacency[a].Contains(b))
                    continue;

                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }

            for (int i = 0; i < rooms.Length; i++)
                rooms[i].Degree = adjacency[i].Count;

            return adjacency;
        }

        /// <summary>
        /// The entrance is the room nearest the grid origin corner. An arbitrary
        /// rule, but a stable one: the run always starts at the same corner of
        /// the map, so a player learns which way "deeper" is.
        /// </summary>
        private static int FindEntrance(DungeonRoom[] rooms)
        {
            int best = 0;
            int bestScore = int.MaxValue;

            for (int i = 0; i < rooms.Length; i++)
            {
                int score = rooms[i].CenterCell.x + rooms[i].CenterCell.y;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = i;
            }

            return best;
        }

        private void MeasureDistances(DungeonRoom[] rooms, List<int>[] adjacency, int entranceIndex)
        {
            for (int i = 0; i < rooms.Length; i++)
                rooms[i].DistanceFromEntrance = -1;

            rooms[entranceIndex].DistanceFromEntrance = 0;

            _frontier.Clear();
            _frontier.Add(entranceIndex);

            for (int cursor = 0; cursor < _frontier.Count; cursor++)
            {
                int current = _frontier[cursor];
                List<int> neighbours = adjacency[current];

                for (int n = 0; n < neighbours.Count; n++)
                {
                    int next = neighbours[n];
                    if (rooms[next].DistanceFromEntrance >= 0)
                        continue;

                    rooms[next].DistanceFromEntrance = rooms[current].DistanceFromEntrance + 1;
                    _frontier.Add(next);
                }
            }

            // A room the spanning tree somehow failed to reach still needs a
            // usable number: leaving -1 would make it look like the deepest room
            // on the floor and pull the boss into it.
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i].DistanceFromEntrance < 0)
                    rooms[i].DistanceFromEntrance = 0;
            }
        }

        /// <summary>The deepest room; ties go to the largest, which makes a better arena.</summary>
        private static int FindBoss(DungeonRoom[] rooms, int entranceIndex)
        {
            int best = -1;

            for (int i = 0; i < rooms.Length; i++)
            {
                if (i == entranceIndex)
                    continue;

                if (best < 0 || IsDeeperOrLarger(rooms[i], rooms[best]))
                    best = i;
            }

            return best;
        }

        private static bool IsDeeperOrLarger(in DungeonRoom candidate, in DungeonRoom current)
        {
            if (candidate.DistanceFromEntrance != current.DistanceFromEntrance)
                return candidate.DistanceFromEntrance > current.DistanceFromEntrance;

            return candidate.CellCount > current.CellCount;
        }

        /// <summary>
        /// Treasure goes into dead ends first — a room with one corridor is a
        /// place the player has to choose to walk into and back out of, which is
        /// exactly the shape a risk-reward detour needs. Only if there are not
        /// enough dead ends does it fall back to the deepest ordinary rooms.
        /// </summary>
        private void AssignTreasureRooms(
            DungeonRoom[] rooms,
            in DungeonGenerationSettings settings,
            int entranceIndex,
            int bossIndex)
        {
            if (settings.TreasureRoomCount <= 0)
                return;

            CollectCandidates(rooms, entranceIndex, bossIndex, deadEndsOnly: true);

            if (_candidates.Count < settings.TreasureRoomCount)
                CollectCandidates(rooms, entranceIndex, bossIndex, deadEndsOnly: false);

            int count = System.Math.Min(settings.TreasureRoomCount, _candidates.Count);
            for (int i = 0; i < count; i++)
                rooms[_candidates[i]].Type = DungeonRoomType.Treasure;
        }

        private void CollectCandidates(
            DungeonRoom[] rooms, int entranceIndex, int bossIndex, bool deadEndsOnly)
        {
            _candidates.Clear();

            for (int i = 0; i < rooms.Length; i++)
            {
                if (i == entranceIndex || i == bossIndex)
                    continue;

                if (deadEndsOnly && rooms[i].Degree > 1)
                    continue;

                _candidates.Add(i);
            }

            SortByDepthDescending(rooms, _candidates);
        }

        /// <summary>
        /// Deepest first, with size and id as tie-breakers.
        ///
        /// The id tie-breaker is not cosmetic: it makes the ordering a total one,
        /// so an unstable sort still produces the same result on every machine.
        /// Without it two players could roll different treasure rooms from the
        /// same seed.
        /// </summary>
        private static void SortByDepthDescending(DungeonRoom[] rooms, List<int> indices)
        {
            indices.Sort((a, b) =>
            {
                if (rooms[a].DistanceFromEntrance != rooms[b].DistanceFromEntrance)
                    return rooms[b].DistanceFromEntrance - rooms[a].DistanceFromEntrance;

                if (rooms[a].CellCount != rooms[b].CellCount)
                    return rooms[b].CellCount - rooms[a].CellCount;

                return a - b;
            });
        }

        /// <summary>
        /// Everything still untyped becomes a fight or an empty transit room.
        /// The empty ones are the point of the share being below one: a floor
        /// where every room is an ambush has no rhythm.
        /// </summary>
        private void AssignCombatRooms(
            DungeonRoom[] rooms,
            in DungeonGenerationSettings settings,
            ref Random random,
            int entranceIndex,
            int bossIndex)
        {
            int combatCount = 0;
            int deepestPlain = -1;

            for (int i = 0; i < rooms.Length; i++)
            {
                if (i == entranceIndex || i == bossIndex)
                    continue;

                if (rooms[i].Type == DungeonRoomType.Treasure)
                    continue;

                if (deepestPlain < 0 ||
                    rooms[i].DistanceFromEntrance > rooms[deepestPlain].DistanceFromEntrance)
                {
                    deepestPlain = i;
                }

                if (random.NextFloat() >= settings.CombatRoomShare)
                {
                    rooms[i].Type = DungeonRoomType.Transit;
                    continue;
                }

                rooms[i].Type = DungeonRoomType.Combat;
                combatCount++;
            }

            // A floor with no fight at all is a legal roll of the dice and a
            // useless floor to play. Promote the deepest plain room rather than
            // re-rolling, so the outcome stays a pure function of the seed.
            if (combatCount == 0 && deepestPlain >= 0)
                rooms[deepestPlain].Type = DungeonRoomType.Combat;
        }
    }
}
