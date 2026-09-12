using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Lobby
{
    /// <summary>
    /// Money, and it is an item.
    ///
    /// A coin is an ordinary ItemDefinition with a CurrencyValue on it: it comes
    /// out of the same pool, occupies a cell, drops on the floor, is born AtRisk
    /// and is lost with everything else when its owner dies for good. An int on
    /// the character would have been half the code and none of that — and the
    /// half it saved is the half the grid inventory had already written.
    ///
    /// A struct of static methods rather than a system, exactly like GridFit and
    /// for the same reason: two owners need the same answers and neither calls
    /// the other. VendorTransactionSystem spends, CraftingSystem spends, and the
    /// UI reads the balance to grey out a button.
    ///
    /// ponytail: one denomination. Payment takes coins whose value fits in what
    /// is still owed and refuses if nothing does, so a purse of 5-coins cannot
    /// buy a 3-coin item — with every coin worth 1, which is what the content
    /// authors, that case does not arise. Making change is the upgrade path, and
    /// it needs a reason first.
    /// </summary>
    public struct Currency
    {
        /// <summary>
        /// What an item is worth before a vendor markup, by rarity.
        ///
        /// Derived from rarity rather than authored per item, because there is
        /// no balance in this prototype to author against and a price field on
        /// forty assets is forty numbers nobody chose. The day an item needs to
        /// be worth more than its rarity says, this becomes a field and this
        /// method becomes its fallback.
        /// </summary>
        public static int BasePrice(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return 2;
                case ItemRarity.Uncommon: return 5;
                case ItemRarity.Rare: return 12;
                case ItemRarity.Epic: return 25;
                case ItemRarity.Legendary: return 50;
                case ItemRarity.Mythic: return 100;
                default: return 2;
            }
        }

        /// <summary>How much this item is worth as money. Zero for everything else.</summary>
        public static int ValueOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return 0;

            // By reference: ItemBlob carries a BlobArray and must never be
            // copied.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.CurrencyValue;
        }

        /// <summary>
        /// The cheapest coin in the game, by id, or zero if there is no currency
        /// at all.
        ///
        /// A scan rather than a baked "this is the coin" reference, because the
        /// alternative is a second place that has to agree with the assets. The
        /// database is dozens of entries and a purchase happens when somebody
        /// clicks, so the scan costs nothing anybody can measure.
        /// </summary>
        public static int SmallestCoinId(ItemDatabase items)
        {
            if (!items.Value.IsCreated)
                return 0;

            ref ItemDatabaseBlob blob = ref items.Value.Value;

            int best = 0;
            int bestValue = int.MaxValue;

            for (int i = 0; i < blob.Items.Length; i++)
            {
                ref ItemBlob item = ref blob.Items[i];

                if (item.CurrencyValue <= 0 || item.CurrencyValue >= bestValue)
                    continue;

                best = item.ItemId;
                bestValue = item.CurrencyValue;
            }

            return best;
        }

        /// <summary>
        /// What is in a container, in coin.
        ///
        /// Counted from the cells, but only at each item recorded origin: an
        /// item writes itself into every square it covers, so counting cells
        /// would pay a 1x2 coin twice.
        /// </summary>
        public static int Balance(EntityManager entityManager, ItemDatabase items, Entity container)
        {
            if (container == Entity.Null || !entityManager.HasBuffer<InventoryCell>(container))
                return 0;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(container);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(container);

            int total = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                Entity item = cells[i].OccupyingItem;

                if (item == Entity.Null || !IsOrigin(entityManager, grid, item, i))
                    continue;

                total += ValueOf(
                    items, entityManager.GetComponentData<ItemInstance>(item).ItemId);
            }

            return total;
        }

        /// <summary>
        /// Takes an amount out of a container, or takes nothing and says no.
        ///
        /// Two passes on purpose: the first only looks, the second only writes.
        /// A payment that ran out of coins halfway would leave the buyer poorer
        /// and empty-handed, which is the one outcome a transaction must never
        /// produce — the same reason EquipmentSystem checks everything before
        /// its first write.
        /// </summary>
        public static bool TryPay(
            EntityManager entityManager, ItemDatabase items, Entity container, int amount)
        {
            if (amount <= 0)
                return true;

            if (container == Entity.Null || !entityManager.HasBuffer<InventoryCell>(container))
                return false;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(container);

            using var coins = new NativeList<Entity>(16, Allocator.Temp);

            // Gathered first, because releasing an item back to the pool writes
            // to its transform and flags — and the cell buffer below is cleared
            // as we go, which would upset a loop still reading it.
            {
                DynamicBuffer<InventoryCell> cells =
                    entityManager.GetBuffer<InventoryCell>(container);

                int remaining = amount;

                for (int i = 0; i < cells.Length && remaining > 0; i++)
                {
                    Entity item = cells[i].OccupyingItem;

                    if (item == Entity.Null || !IsOrigin(entityManager, grid, item, i))
                        continue;

                    int value = ValueOf(
                        items, entityManager.GetComponentData<ItemInstance>(item).ItemId);

                    // A coin worth more than what is still owed is skipped
                    // rather than spent: overpaying without change is theft, and
                    // change is the deferred half of this feature.
                    if (value <= 0 || value > remaining)
                        continue;

                    coins.Add(item);
                    remaining -= value;
                }

                if (remaining > 0)
                    return false;
            }

            for (int i = 0; i < coins.Length; i++)
            {
                GridFit.Clear(entityManager.GetBuffer<InventoryCell>(container), coins[i]);

                // Straight back to the pool. Spent money does not go anywhere —
                // there is no vendor purse to credit, and inventing one would be
                // an economy nobody asked for.
                LootItemPool.Release(entityManager, coins[i]);
            }

            return true;
        }

        /// <summary>
        /// Pays an amount into a container as coins, taking entities from the
        /// loot pool.
        ///
        /// Refuses as a whole when the pool or the container cannot take all of
        /// it, for the same reason payment does: half a refund is worse than
        /// none. The caller is expected to have checked CoinsFor against the
        /// free pool and the free cells first; this repeats the check because it
        /// is cheap and because being wrong here loses items.
        /// </summary>
        public static bool TryGrant(
            EntityManager entityManager,
            ItemDatabase items,
            in NativeArray<Entity> free,
            Entity container,
            int coinId,
            int amount,
            float4 colour,
            ref Random random)
        {
            if (amount <= 0)
                return true;

            int value = ValueOf(items, coinId);
            if (value <= 0 || coinId == 0)
                return false;

            // Integer division, deliberately not rounded up: a payout that is
            // not a whole number of coins is one the price list should not have
            // produced, and paying the extra would mint money.
            int count = amount / value;

            if (count <= 0 || count > free.Length)
                return false;

            if (container == Entity.Null || !entityManager.HasBuffer<InventoryCell>(container))
                return false;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(container);

            if (!GridFit.TryGetFootprint(items, coinId, false, out int width, out int height))
                return false;

            FixedString64Bytes name = NameOf(items, coinId);

            for (int i = 0; i < count; i++)
            {
                DynamicBuffer<InventoryCell> cells =
                    entityManager.GetBuffer<InventoryCell>(container);

                if (!GridFit.FindFirstFit(
                        cells, grid, width, height, Entity.Null, out int x, out int y))
                {
                    // Only reachable if the caller skipped its own room check.
                    // What is already paid out stays paid — undoing it would
                    // mean releasing entities this method has already stored,
                    // and the caller checks precisely so this cannot happen.
                    return false;
                }

                Entity coin = free[i];

                entityManager.SetComponentData(coin, new ItemInstance
                {
                    ItemId = coinId,
                    Rarity = ItemRarity.Common,
                    RiskState = ItemRiskState.AtRisk
                });

                entityManager.SetComponentData(coin, new ItemDisplayName { Value = name });
                entityManager.SetComponentData(
                    coin, new URPMaterialPropertyBaseColor { Value = colour });

                // The same call the pool makes when it hands an item out: this
                // entity was something else last time it was used, and a stale
                // socket buffer is the one part of that which does not clear
                // itself.
                GemSockets.Rebuild(entityManager, items, coin, ref random);

                GridFit.Occupy(cells, grid, x, y, width, height, coin);

                entityManager.SetComponentData(coin, new ItemGridPlacement
                {
                    ContainerEntity = container,
                    OriginX = x,
                    OriginY = y,
                    IsRotated = false
                });

                LootItemPool.Store(entityManager, coin);
            }

            return true;
        }

        /// <summary>
        /// How many coins of this kind a payout comes to. The number of free
        /// pool entities and free cells a caller has to have.
        /// </summary>
        public static int CoinsFor(ItemDatabase items, int coinId, int amount)
        {
            int value = ValueOf(items, coinId);
            return value <= 0 ? 0 : amount / value;
        }

        /// <summary>How many empty squares a container has left.</summary>
        public static int FreeCells(EntityManager entityManager, Entity container)
        {
            if (container == Entity.Null || !entityManager.HasBuffer<InventoryCell>(container))
                return 0;

            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(container);

            int free = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].IsFree)
                    free++;
            }

            return free;
        }

        public static FixedString64Bytes NameOf(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return default;

            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.Name;
        }

        private static bool IsOrigin(
            EntityManager entityManager,
            in InventoryGridComponent grid,
            Entity item,
            int cellIndex)
        {
            if (!entityManager.HasComponent<ItemGridPlacement>(item) ||
                !entityManager.HasComponent<ItemInstance>(item))
            {
                return false;
            }

            ItemGridPlacement placement =
                entityManager.GetComponentData<ItemGridPlacement>(item);

            return grid.IndexOf(placement.OriginX, placement.OriginY) == cellIndex;
        }

    }
}
