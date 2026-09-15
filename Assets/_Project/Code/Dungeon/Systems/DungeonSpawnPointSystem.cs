using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Spawning;
using TogetherWeFall.Spawning.Systems;

namespace TogetherWeFall.Dungeon.Systems
{
    /// <summary>
    /// Creates the spawn points for a generated floor, once.
    ///
    /// The arena scene has spawn points placed by hand in a SubScene; a dungeon
    /// cannot, because its rooms do not exist until the seed is rolled. So the
    /// points are derived from room data instead — and derived by a system
    /// rather than by the director, which keeps the GameObject bridge free of
    /// anything that decides where enemies come from.
    ///
    /// Points are placed inside the room rather than at its centre so a wave
    /// arrives around the players instead of on top of them.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(WaveSpawnSystem))]
    public partial struct DungeonSpawnPointSystem : ISystem
    {
        /// <summary>
        /// Four corners of an inner rectangle. More points would only spread a
        /// wave thinner; the spawner already scatters within its jitter radius.
        /// </summary>
        private const int PointsPerRoom = 4;

        /// <summary>How far towards the walls the points sit, as a share of the half-extent.</summary>
        private const float Inset = 0.55f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonRunState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity run = SystemAPI.GetSingletonEntity<DungeonRunState>();

            // Not before the navmesh exists: a spawn point is only useful if an
            // enemy standing on it can find its way off it.
            if (SystemAPI.GetComponent<DungeonRunState>(run).Phase != DungeonRunPhase.Ready)
                return;

            if (state.EntityManager.HasComponent<DungeonSpawnPointsBuilt>(run))
                return;

            // A copy, because creating entities below is a structural change and
            // the buffer would not survive it.
            using NativeArray<DungeonRoomElement> rooms =
                SystemAPI.GetBuffer<DungeonRoomElement>(run).ToNativeArray(Allocator.Temp);

            EntityArchetype archetype = state.EntityManager.CreateArchetype(
                typeof(SpawnPoint), typeof(LocalTransform), typeof(LocalToWorld));

            int created = CreatePoints(ref state, archetype, rooms);

            state.EntityManager.AddComponent<DungeonSpawnPointsBuilt>(run);

            UnityEngine.Debug.Log(
                $"[DungeonSpawnPointSystem] {created} spawn points created for the floor.");
        }

        private int CreatePoints(
            ref SystemState state,
            EntityArchetype archetype,
            in NativeArray<DungeonRoomElement> rooms)
        {
            int created = 0;

            for (int i = 0; i < rooms.Length; i++)
            {
                DungeonRoomElement room = rooms[i];
                if (!HostsEncounter(room.Type))
                    continue;

                for (int p = 0; p < PointsPerRoom; p++)
                {
                    float2 offset = CornerDirection(p) * room.Extents * Inset;
                    float3 position = room.Center + new float3(offset.x, 0f, offset.y);

                    Entity point = state.EntityManager.CreateEntity(archetype);
                    state.EntityManager.SetComponentData(point, new SpawnPoint { RoomId = room.RoomId });
                    state.EntityManager.SetComponentData(point, LocalTransform.FromPosition(position));

                    // LocalToWorld is written here as well as LocalTransform:
                    // the transform group would fill it in later, but the
                    // spawner reads it and might read it first.
                    state.EntityManager.SetComponentData(
                        point, new LocalToWorld { Value = float4x4.Translate(position) });

                    created++;
                }
            }

            return created;
        }

        /// <summary>
        /// The four diagonal directions, derived from the index rather than kept
        /// in a table: a static array would be a managed field in a system
        /// struct, which is exactly the kind of thing that stops compiling the
        /// day this system is worth Bursting.
        /// </summary>
        private static float2 CornerDirection(int index) => new float2(
            (index & 1) == 0 ? -1f : 1f,
            (index & 2) == 0 ? -1f : 1f);

        /// <summary>
        /// Which room types fight. Treasure rooms deliberately do not — they are
        /// where chests will go, and a chest guarded by a wave is a decision to
        /// make later, on purpose, rather than by accident now.
        /// </summary>
        private static bool HostsEncounter(DungeonRoomType type)
            => type == DungeonRoomType.Combat || type == DungeonRoomType.Boss;
    }
}
