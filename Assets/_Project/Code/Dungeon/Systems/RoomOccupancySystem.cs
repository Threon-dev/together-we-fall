using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using TogetherWeFall.Enemies;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Dungeon.Systems
{
    /// <summary>
    /// Counts who is standing in which room.
    ///
    /// Split from the system that acts on those counts for the same reason
    /// RepathRequestSystem is split from PathfindingSystem: this one is a
    /// measurement over every enemy on the floor and belongs in a job, while the
    /// decisions it feeds are a dozen comparisons on the main thread. Keeping
    /// them apart means the rule for activating a room can change without
    /// touching anything that runs per enemy.
    ///
    /// A single job rather than a parallel one: the work is rooms times
    /// occupants — a handful of rooms against a few hundred enemies — which is
    /// thousands of comparisons, not millions. Parallelising it would need
    /// per-thread counters to avoid racing on the room buffer, and would cost
    /// more in scheduling than it saves.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct RoomOccupancySystem : ISystem
    {
        private EntityQuery _enemyQuery;

        public void OnCreate(ref SystemState state)
        {
            _enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<EnemyTag, LocalTransform>()
                .Build();

            state.RequireForUpdate<DungeonRunState>();
            state.RequireForUpdate<PlayerPositionsSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<DungeonRunState>().Phase != DungeonRunPhase.Ready)
                return;

            DynamicBuffer<DungeonRoomElement> rooms =
                SystemAPI.GetSingletonBuffer<DungeonRoomElement>();

            if (rooms.Length == 0)
                return;

            DynamicBuffer<PlayerPositionElement> players =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>(isReadOnly: true);

            NativeList<LocalTransform> enemies = _enemyQuery.ToComponentDataListAsync<LocalTransform>(
                state.WorldUpdateAllocator, state.Dependency, out JobHandle gatherHandle);

            state.Dependency = new CountOccupantsJob
            {
                Rooms = rooms,
                Players = players.AsNativeArray(),
                Enemies = enemies
            }.Schedule(gatherHandle);
        }

        [BurstCompile]
        private struct CountOccupantsJob : IJob
        {
            public DynamicBuffer<DungeonRoomElement> Rooms;
            [ReadOnly] public NativeArray<PlayerPositionElement> Players;
            [ReadOnly] public NativeList<LocalTransform> Enemies;

            public void Execute()
            {
                for (int r = 0; r < Rooms.Length; r++)
                {
                    DungeonRoomElement room = Rooms[r];

                    room.PlayersInside = 0;
                    room.EnemiesInside = 0;

                    for (int p = 0; p < Players.Length; p++)
                    {
                        // A downed or disconnected player must not hold a room
                        // open: the room would never clear and the run would
                        // stall on someone who is not there.
                        if (!Players[p].IsTargetable)
                            continue;

                        if (room.Contains(Players[p].Position))
                            room.PlayersInside++;
                    }

                    for (int e = 0; e < Enemies.Length; e++)
                    {
                        if (room.Contains(Enemies[e].Position))
                            room.EnemiesInside++;
                    }

                    Rooms[r] = room;
                }
            }
        }
    }
}
