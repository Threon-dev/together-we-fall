using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Network;

namespace TogetherWeFall.Lobby.Systems
{
    /// <summary>
    /// Gives each vendor a shelf and puts its stock on it, once.
    ///
    /// The shelf is an ordinary container: InventoryGridComponent plus a cell
    /// buffer, which is two thirds of what the player bag is made of. The third
    /// it deliberately lacks is ContainerOwner, and that single omission is what
    /// makes the shop safe — InventoryPlacementSystem refuses every request
    /// naming a container with no owner, so no client can drag itself a free
    /// sword out of the window. Only VendorTransactionSystem moves anything on
    /// or off this grid, and it charges.
    ///
    /// The stock itself comes out of the same pooled entities a floor drop comes
    /// from, through the same LootItemPool.Activate. That is not thrift, it is
    /// the reason a bought item is a real item: it was already one on the shelf,
    /// and buying it only changes which container it is in.
    ///
    /// The cost is worth saying out loud: a vendor with ten items holds ten of
    /// the pool slots for the whole session, and the pool is the ceiling on
    /// every item that exists at once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VendorStockSystem : ISystem
    {
        private EntityQuery _unstockedQuery;
        private EntityQuery _freeItemQuery;

        public void OnCreate(ref SystemState state)
        {
            _unstockedQuery = SystemAPI.QueryBuilder()
                .WithAll<VendorComponent, VendorStockEntry>()
                .Build();

            // The pool seen from the other side, exactly as LootTableSystem
            // sees it: neither on the floor nor in anybody keeping.
            _freeItemQuery = SystemAPI.QueryBuilder()
                .WithAll<ItemInstance>()
                .WithDisabled<InteractableTag>()
                .WithDisabled<ItemStored>()
                .Build();

            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<LootPrefabs>();
            state.RequireForUpdate<VendorComponent>();
            state.RequireForUpdate<NetworkPrefabs>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using NativeArray<Entity> vendors = _unstockedQuery.ToEntityArray(Allocator.Temp);

            using var pending = new NativeList<Entity>(2, Allocator.Temp);

            for (int i = 0; i < vendors.Length; i++)
            {
                VendorComponent vendor =
                    state.EntityManager.GetComponentData<VendorComponent>(vendors[i]);

                if (vendor.StockContainer == Entity.Null)
                    pending.Add(vendors[i]);
            }

            // Deliberately not state.Enabled = false once everything is stocked.
            // A SubScene loads asynchronously, so "no vendor still wants stock"
            // and "no vendor has loaded yet" look identical on an early frame,
            // and switching off on the second would leave a shop permanently
            // empty with nothing in the log. The query only runs at all in a
            // scene that has a vendor, and matches one or two entities.
            if (pending.Length == 0)
                return;

            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            for (int i = 0; i < pending.Length; i++)
                Stock(ref state, items, pending[i]);
        }

        private void Stock(ref SystemState state, ItemDatabase items, Entity vendor)
        {
            EntityManager entityManager = state.EntityManager;

            VendorComponent component = entityManager.GetComponentData<VendorComponent>(vendor);

            // Copied out before the container is created: creating an entity is
            // a structural change and the buffer would not survive it.
            using var wanted = new NativeList<int>(16, Allocator.Temp);
            {
                DynamicBuffer<VendorStockEntry> stock =
                    entityManager.GetBuffer<VendorStockEntry>(vendor);

                for (int i = 0; i < stock.Length; i++)
                    wanted.Add(stock[i].ItemId);
            }

            var grid = new InventoryGridComponent
            {
                Width = math.max(1, component.StockWidth),
                Height = math.max(1, component.StockHeight)
            };

            // No ContainerOwner, on purpose. See the class comment. A ghost, so every
            // client's shop window draws the shelf the host actually holds.
            Entity container = entityManager.Instantiate(SystemAPI.GetSingleton<NetworkPrefabs>().VendorShelf);

            entityManager.SetName(container, "VendorStock");
            entityManager.SetComponentData(container, grid);
            GridFit.Reset(entityManager.GetBuffer<InventoryCell>(container), grid);

            component.StockContainer = container;
            entityManager.SetComponentData(vendor, component);

            if (wanted.Length == 0)
                return;

            FillShelf(ref state, items, container, grid, wanted);
        }

        private void FillShelf(
            ref SystemState state,
            ItemDatabase items,
            Entity container,
            in InventoryGridComponent grid,
            NativeList<int> wanted)
        {
            EntityManager entityManager = state.EntityManager;

            using NativeArray<Entity> free = _freeItemQuery.ToEntityArray(Allocator.Temp);
            if (free.Length == 0)
                return;

            DynamicBuffer<RarityColor> colors = SystemAPI.GetSingletonBuffer<RarityColor>();

            using var drops = new NativeList<ItemDrop>(wanted.Length, Allocator.Temp);

            for (int i = 0; i < wanted.Length && i < free.Length; i++)
            {
                int index = items.IndexOf(wanted[i]);
                if (index < 0)
                    continue;

                // By reference: ItemBlob carries a BlobArray of affixes.
                ref ItemBlob blob = ref items.Value.Value.Items[index];

                drops.Add(new ItemDrop
                {
                    // Parked immediately below, so where it would have landed
                    // never matters.
                    Position = float3.zero,
                    ItemId = blob.ItemId,
                    Rarity = blob.Rarity,
                    Name = blob.Name,
                    Color = ColorFor(colors, blob.Rarity)
                });
            }

            if (drops.Length == 0)
                return;

            RefRW<LootRandom> random = SystemAPI.GetSingletonRW<LootRandom>();

            // The same call a chest makes. Everything an item has to become when
            // it is handed out — its id, its colour, its sockets, the skills a
            // weapon rolls — happens here and nowhere else, so a bought sword is
            // indistinguishable from a found one because it was made the same
            // way.
            int count = LootItemPool.Activate(
                entityManager, items, free, drops, ref random.ValueRW.Value);

            using var unplaceable = new NativeList<Entity>(1, Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                if (!Shelve(entityManager, items, container, grid, free[i]))
                    unplaceable.Add(free[i]);
            }

            if (unplaceable.Length == 0)
                return;

            UnityEngine.Debug.LogWarning(
                $"[VendorStockSystem] {unplaceable.Length} stock item(s) did not fit on a " +
                $"{grid.Width}x{grid.Height} shelf and went back to the pool. Widen the shelf " +
                "or shorten the stock list.");

            LootItemPool.Release(entityManager, unplaceable.AsArray());
        }

        /// <summary>
        /// Puts one already-activated item on the shelf.
        ///
        /// Store is called last and only on success, so an item that did not fit
        /// is still a floor drop the caller can hand straight back to the pool
        /// rather than a parked entity nobody owns.
        /// </summary>
        private static bool Shelve(
            EntityManager entityManager,
            ItemDatabase items,
            Entity container,
            in InventoryGridComponent grid,
            Entity item)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            if (!GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                return false;

            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(container);

            if (!GridFit.FindFirstFit(
                    cells, grid, width, height, Entity.Null, out int x, out int y))
            {
                return false;
            }

            GridFit.Occupy(cells, grid, x, y, width, height, item);

            entityManager.SetComponentData(item, new ItemGridPlacement
            {
                ContainerEntity = container,
                OriginX = x,
                OriginY = y,
                IsRotated = false
            });

            LootItemPool.Store(entityManager, item);
            return true;
        }

        private static float4 ColorFor(in DynamicBuffer<RarityColor> colors, ItemRarity rarity)
        {
            int index = (int)rarity;
            return index < colors.Length ? colors[index].Value : new float4(1f, 1f, 1f, 1f);
        }
    }
}
