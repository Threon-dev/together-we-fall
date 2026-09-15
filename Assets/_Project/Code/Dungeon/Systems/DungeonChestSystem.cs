using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Loot;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Dungeon.Systems
{
    /// <summary>
    /// Puts chests in the treasure rooms, once per floor.
    ///
    /// The mirror of DungeonSpawnPointSystem, and for the same reason: the rooms
    /// do not exist until the seed is rolled, so what fills them cannot be
    /// authored in a SubScene. Treasure rooms have deliberately never spawned a
    /// wave — this is what they were being kept empty for.
    ///
    /// Placement is derived from the run seed, so every player in a coop game
    /// finds the chests in the same corners without a byte crossing the wire.
    /// Re-seeding the loot dice here does the same for what comes out of them:
    /// a strange drop becomes reproducible from one number.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DungeonChestSystem : ISystem
    {
        /// <summary>
        /// Salts, so that chest placement, loot rolls and the floor layout do not
        /// all walk the same random sequence from the same seed and end up
        /// correlated in ways nobody would ever think to look for.
        /// </summary>
        private const uint PlacementSeedSalt = 0x9E3779B9u;
        private const uint LootSeedSalt = 0x85EBCA6Bu;

        /// <summary>
        /// Lifts a chest onto the floor rather than into it. Matches the half
        /// height of the chest prefab built by DungeonSceneBuilder.
        /// </summary>
        private const float ChestGroundOffset = 0.5f;

        /// <summary>How far from the room centre chests are ringed, as a share of the half-extent.</summary>
        private const float PlacementRadiusFactor = 0.45f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonRunState>();
            state.RequireForUpdate<LootPrefabs>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity run = SystemAPI.GetSingletonEntity<DungeonRunState>();
            DungeonRunState runState = SystemAPI.GetComponent<DungeonRunState>(run);

            if (runState.Phase != DungeonRunPhase.Ready)
                return;

            if (state.EntityManager.HasComponent<DungeonChestsPlaced>(run))
                return;

            LootSettings settings = SystemAPI.GetSingleton<LootSettings>();
            LootPrefabs prefabs = SystemAPI.GetSingleton<LootPrefabs>();

            ReseedLootDice(ref state, runState.Seed);

            // A copy: instantiating chests below is a structural change and the
            // room buffer would not survive it.
            using NativeArray<DungeonRoomElement> rooms =
                SystemAPI.GetBuffer<DungeonRoomElement>(run).ToNativeArray(Allocator.Temp);

            using var placements = new NativeList<float3>(8, Allocator.Temp);
            var random = Random.CreateFromIndex(runState.Seed ^ PlacementSeedSalt);

            CollectPlacements(rooms, settings.ChestsPerTreasureRoom, ref random, placements);

            if (placements.Length > 0)
                PlaceChests(ref state, prefabs.Chest, settings.TreasureChestTableId, placements);

            state.EntityManager.AddComponent<DungeonChestsPlaced>(run);

            UnityEngine.Debug.Log(
                $"[DungeonChestSystem] {placements.Length} chests placed in the treasure rooms.");
        }

        /// <summary>
        /// Points the loot dice at this run.
        ///
        /// The host owns loot RNG, so this does not need to match anything on a
        /// client — but deriving it from the run seed means an odd drop can be
        /// reproduced by replaying the same floor, which is the difference
        /// between a bug report and a shrug.
        /// </summary>
        private void ReseedLootDice(ref SystemState state, uint runSeed)
        {
            Entity lootEntity = SystemAPI.GetSingletonEntity<LootDatabase>();

            state.EntityManager.SetComponentData(lootEntity, new LootRandom
            {
                Value = Random.CreateFromIndex(runSeed ^ LootSeedSalt)
            });
        }

        private static void CollectPlacements(
            in NativeArray<DungeonRoomElement> rooms,
            int chestsPerRoom,
            ref Random random,
            NativeList<float3> placements)
        {
            int count = math.max(1, chestsPerRoom);

            for (int i = 0; i < rooms.Length; i++)
            {
                DungeonRoomElement room = rooms[i];
                if (room.Type != DungeonRoomType.Treasure)
                    continue;

                float radius = math.cmin(room.Extents) * PlacementRadiusFactor;

                for (int c = 0; c < count; c++)
                {
                    // Evenly spaced around the room centre, with a jitter so two
                    // treasure rooms on the same floor do not look stamped from
                    // the same template.
                    float angle = (c + random.NextFloat(0f, 0.6f)) / count * 2f * math.PI;

                    placements.Add(room.Center + new float3(
                        math.cos(angle) * radius,
                        ChestGroundOffset,
                        math.sin(angle) * radius));
                }
            }
        }

        private void PlaceChests(
            ref SystemState state, Entity prefab, int tableId, NativeList<float3> placements)
        {
            using NativeArray<Entity> chests = state.EntityManager.Instantiate(
                prefab, placements.Length, Allocator.Temp);

            for (int i = 0; i < chests.Length; i++)
            {
                state.EntityManager.SetComponentData(
                    chests[i], LocalTransform.FromPosition(placements[i]));

                // Which table a chest rolls on is a property of where it stands,
                // not of the prefab — a boss reward will use the same prefab.
                state.EntityManager.SetComponentData(chests[i], new LootTableId { Value = tableId });
            }
        }
    }
}
