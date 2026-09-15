using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Spawning;
using TogetherWeFall.Spawning.Systems;

namespace TogetherWeFall.Dungeon.Systems
{
    /// <summary>
    /// Wakes a room up when players walk into it, and retires it when the fight
    /// is over.
    ///
    /// This is the whole reason room types exist as data: the rule below reads
    /// the type and orders a wave, and nothing in the spawner, the pathfinding
    /// or the enemy pipeline knows a dungeon is involved at all. Adding chests
    /// to treasure rooms or a real encounter to the boss room happens here and
    /// only here.
    ///
    /// A room clears when its grace period has passed and nothing hostile is
    /// standing in it. Enemies that chased the players out of the room count as
    /// gone, which is the honest reading: the encounter has moved, and re-arming
    /// the room the players just fought through would be the wrong answer.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RoomOccupancySystem))]
    [UpdateBefore(typeof(WaveSpawnSystem))]
    public partial struct RoomActivationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonRunState>();
            state.RequireForUpdate<DungeonEncounterSettings>();

            // Without a spawner there is nowhere to send an order, so the system
            // stays idle rather than activating rooms whose waves never arrive.
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<WaveSpawnerConfig, WaveSpawnOrder>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<DungeonRunState>().Phase != DungeonRunPhase.Ready)
                return;

            DungeonEncounterSettings settings = SystemAPI.GetSingleton<DungeonEncounterSettings>();
            DynamicBuffer<DungeonRoomElement> rooms =
                SystemAPI.GetSingletonBuffer<DungeonRoomElement>();

            Entity spawner = SystemAPI.GetSingletonEntity<WaveSpawnerConfig>();
            DynamicBuffer<WaveSpawnOrder> orders = SystemAPI.GetBuffer<WaveSpawnOrder>(spawner);

            float deltaTime = SystemAPI.Time.DeltaTime;

            for (int i = 0; i < rooms.Length; i++)
            {
                DungeonRoomElement room = rooms[i];

                switch (room.Phase)
                {
                    case DungeonRoomPhase.Dormant:
                        if (room.PlayersInside > 0)
                            Activate(ref room, settings, orders);
                        break;

                    case DungeonRoomPhase.Active:
                        room.PhaseTimer += deltaTime;

                        // The grace period is what stops a room from clearing in
                        // the frame between the order and the spawn, when it is
                        // simply still empty.
                        if (room.PhaseTimer >= settings.RoomClearGraceSeconds &&
                            room.EnemiesInside == 0)
                        {
                            room.Phase = DungeonRoomPhase.Cleared;
                            room.PhaseTimer = 0f;
                        }

                        break;
                }

                rooms[i] = room;
            }
        }

        private static void Activate(
            ref DungeonRoomElement room,
            in DungeonEncounterSettings settings,
            DynamicBuffer<WaveSpawnOrder> orders)
        {
            room.Phase = DungeonRoomPhase.Active;
            room.PhaseTimer = 0f;

            int count = EnemyCount(room.Type, settings);
            if (count <= 0)
                return;

            orders.Add(new WaveSpawnOrder { RoomId = room.RoomId, Count = count });

            UnityEngine.Debug.Log(
                $"[RoomActivationSystem] Room {room.RoomId} ({room.Type}) activated — " +
                $"{count} enemies ordered.");
        }

        /// <summary>
        /// How big the fight in a room is. Zero means the room holds no
        /// encounter: entrance, transit and — for now — treasure rooms, which
        /// will get chests rather than a wave.
        /// </summary>
        private static int EnemyCount(DungeonRoomType type, in DungeonEncounterSettings settings)
        {
            switch (type)
            {
                case DungeonRoomType.Combat:
                    return settings.EnemiesPerCombatRoom;

                case DungeonRoomType.Boss:
                    return (int)math.round(
                        settings.EnemiesPerCombatRoom * settings.BossRoomEnemyMultiplier);

                default:
                    return 0;
            }
        }
    }
}
