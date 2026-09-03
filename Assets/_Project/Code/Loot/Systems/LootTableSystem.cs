using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Interaction;
using TogetherWeFall.Equipment;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Loot.Systems
{
    /// <summary>
    /// Rolls what a chest drops and puts it on the floor.
    ///
    /// The roll happens exactly once, on the host, and the result is entities in
    /// the world. Nothing goes straight into an inventory — partly because there
    /// is not one yet, but mostly because a pile on the floor is the cheapest
    /// possible answer to "who gets it": both players watch the same items fall
    /// and one of them walks over. There is no allocation rule to write, no
    /// timer to arbitrate, and nothing to disagree about.
    ///
    /// Rolling is two-stage — rarity first, then an item of that rarity — which
    /// is what makes the weights on a table mean what they say. See LootTableBlob.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChestInteractionSystem))]
    public partial struct LootTableSystem : ISystem
    {
        private EntityQuery _freeItemQuery;

        public void OnCreate(ref SystemState state)
        {
            // The pool, seen from the other side: every item not currently
            // lying on the floor.
            _freeItemQuery = SystemAPI.QueryBuilder()
                .WithAll<ItemInstance>()
                .WithDisabled<InteractableTag>()
                .Build();

            state.RequireForUpdate<LootDatabase>();
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<LootPrefabs>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var drops = new NativeList<PendingDrop>(8, Allocator.Temp);

            RollOpenedChests(ref state, drops);

            if (drops.Length == 0)
                return;

            SpawnDrops(ref state, drops);
        }

        private void RollOpenedChests(ref SystemState state, NativeList<PendingDrop> drops)
        {
            LootDatabase database = SystemAPI.GetSingleton<LootDatabase>();
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            LootSettings settings = SystemAPI.GetSingleton<LootSettings>();
            RefRW<LootRandom> random = SystemAPI.GetSingletonRW<LootRandom>();

            // No WithPresent here, unlike the system that raises this flag: this
            // loop wants precisely the chests whose flag is already up.
            foreach ((RefRO<LootTableId> table, RefRO<LocalTransform> transform,
                      EnabledRefRW<LootRollRequest> request) in
                     SystemAPI.Query<RefRO<LootTableId>, RefRO<LocalTransform>,
                         EnabledRefRW<LootRollRequest>>()
                         .WithAll<ChestTag>())
            {
                // Lower the flag first. A table that rolls nothing must not be
                // asked again next frame, and forever after.
                request.ValueRW = false;

                RollTable(
                    ref database.Value.Value,
                    items,
                    table.ValueRO.Value,
                    transform.ValueRO.Position,
                    settings,
                    ref random.ValueRW.Value,
                    drops);
            }
        }

        private static void RollTable(
            ref LootDatabaseBlob database,
            ItemDatabase items,
            int tableIndex,
            float3 origin,
            in LootSettings settings,
            ref Random random,
            NativeList<PendingDrop> drops)
        {
            if (tableIndex < 0 || tableIndex >= database.Tables.Length)
            {
                UnityEngine.Debug.LogWarning(
                    $"[LootTableSystem] Chest refers to loot table {tableIndex}, which does not " +
                    "exist — nothing dropped.");
                return;
            }

            ref LootTableBlob table = ref database.Tables[tableIndex];
            if (table.Entries.Length == 0)
                return;

            int rolls = random.NextInt(table.MinRolls, table.MaxRolls + 1);

            for (int i = 0; i < rolls; i++)
            {
                int rarity = PickRarity(ref table, ref random);
                if (rarity < 0)
                    continue;

                int entry = PickEntry(ref table, rarity, ref random);
                if (entry < 0)
                    continue;

                int itemIndex = table.Entries[entry].ItemIndex;
                if (itemIndex < 0 || itemIndex >= items.Value.Value.Items.Length)
                    continue;

                // By reference, never by value: ItemBlob holds a BlobArray of
                // affixes, and copying the struct leaves that array pointing at
                // whatever happens to sit near the copy.
                ref ItemBlob item = ref items.Value.Value.Items[itemIndex];

                drops.Add(new PendingDrop
                {
                    Position = Scatter(origin, settings.DropScatterRadius, ref random),
                    ItemId = item.ItemId,
                    Name = item.Name,

                    // From the item, not from the bucket it was drawn out of.
                    // They agree by construction, and reading the item means
                    // they cannot stop agreeing.
                    Rarity = item.Rarity
                });
            }
        }

        /// <summary>
        /// Picks a rarity by weight. Rarities the table has no items for were
        /// zeroed at bake time, so this can never choose one and then find
        /// nothing to hand over.
        /// </summary>
        private static int PickRarity(ref LootTableBlob table, ref Random random)
        {
            float total = 0f;
            for (int r = 0; r < table.RarityWeights.Length; r++)
                total += table.RarityWeights[r];

            if (total <= 0f)
                return -1;

            float roll = random.NextFloat(0f, total);
            float cumulative = 0f;

            for (int r = 0; r < table.RarityWeights.Length; r++)
            {
                cumulative += table.RarityWeights[r];
                if (roll < cumulative)
                    return r;
            }

            // Rounding can put the roll a hair past the last boundary. Walk back
            // to the last rarity that actually has weight rather than returning
            // the last index blindly, which might be an empty one.
            for (int r = table.RarityWeights.Length - 1; r >= 0; r--)
            {
                if (table.RarityWeights[r] > 0f)
                    return r;
            }

            return -1;
        }

        private static int PickEntry(ref LootTableBlob table, int rarity, ref Random random)
        {
            int2 range = table.RarityRanges[rarity];
            if (range.y <= 0)
                return -1;

            float total = 0f;
            for (int i = 0; i < range.y; i++)
                total += table.Entries[range.x + i].Weight;

            // Every entry weighted zero is an authoring mistake, not a reason to
            // drop nothing. Treat them as equally likely.
            if (total <= 0f)
                return range.x + random.NextInt(0, range.y);

            float roll = random.NextFloat(0f, total);
            float cumulative = 0f;

            for (int i = 0; i < range.y; i++)
            {
                cumulative += table.Entries[range.x + i].Weight;
                if (roll < cumulative)
                    return range.x + i;
            }

            return range.x + range.y - 1;
        }

        private static float3 Scatter(float3 origin, float radius, ref Random random)
        {
            float angle = random.NextFloat(0f, 2f * math.PI);
            float distance = random.NextFloat(0.6f, math.max(0.7f, radius));

            return origin + new float3(math.cos(angle) * distance, 0f, math.sin(angle) * distance);
        }

        /// <summary>
        /// Puts the rolled items on the floor, taking them from the pool.
        ///
        /// Nothing is created: the items already exist, parked under the world,
        /// and dropping one is a handful of component writes. A chest whose
        /// drops do not fit gives fewer, which is what a ceiling means.
        /// </summary>
        private void SpawnDrops(ref SystemState state, NativeList<PendingDrop> drops)
        {
            using NativeArray<RarityColor> colors =
                SystemAPI.GetSingletonBuffer<RarityColor>(isReadOnly: true)
                    .ToNativeArray(Allocator.Temp);

            using var ready = new NativeList<ItemDrop>(drops.Length, Allocator.Temp);

            for (int i = 0; i < drops.Length; i++)
            {
                PendingDrop drop = drops[i];

                ready.Add(new ItemDrop
                {
                    Position = drop.Position,
                    ItemId = drop.ItemId,
                    Rarity = drop.Rarity,
                    Name = drop.Name,
                    Color = ColorFor(colors, drop.Rarity)
                });

                UnityEngine.Debug.Log(
                    $"[LootTableSystem] Dropped {drop.Name} ({drop.Rarity}).");
            }

            using NativeArray<Entity> free = _freeItemQuery.ToEntityArray(Allocator.Temp);
            LootItemPool.Activate(state.EntityManager, free, ready);
        }

        private static float4 ColorFor(in NativeArray<RarityColor> colors, ItemRarity rarity)
        {
            int index = (int)rarity;
            return index < colors.Length ? colors[index].Value : new float4(1f, 1f, 1f, 1f);
        }

        /// <summary>One rolled item, waiting for the structural change that creates it.</summary>
        private struct PendingDrop
        {
            public float3 Position;
            public int ItemId;
            public ItemRarity Rarity;
            public FixedString64Bytes Name;
        }
    }
}
