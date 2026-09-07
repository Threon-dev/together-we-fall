using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// One item a new character is given, in the order it is granted.
    ///
    /// The first entry is the gear; the rest are gems. Baked beside the
    /// character sheet, because what a character starts with is authored
    /// balance rather than something the code should name.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct StarterItem : IBufferElementData
    {
        public int ItemId;
    }

    /// <summary>"This character has already been given its kit."</summary>
    public struct StarterKitGranted : IComponentData
    {
    }

    /// <summary>
    /// Hands a new character its starting gear and gems, and points the hotkeys
    /// at them.
    ///
    /// It exists because skills now live in gems, and a character with no gems
    /// has no skills. Handing out a bar pre-filled with skills would have been
    /// the cheaper answer and the wrong one: it would mean two places a skill
    /// can come from, and "why do I have this skill" would have two answers.
    /// This way there is exactly one — something is in a socket — and the kit
    /// simply puts the first few things there.
    ///
    /// The gems go in the bag, not into the holes. Socketing them here would
    /// be deciding somebody's build for them, and the whole point of the model
    /// is that the arrangement is the player's. The weapon is worn because a
    /// character with no gear has nowhere to put a gem at all.
    ///
    /// Items come from the loot pool like every other item in the game, so the
    /// pool stays the ceiling on how many exist at once.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TogetherWeFall.Equipment.Systems.SocketSystem))]
    public partial struct StarterKitSystem : ISystem
    {
        private EntityQuery _freeItemQuery;
        private EntityQuery _pendingQuery;

        public void OnCreate(ref SystemState state)
        {
            // The pool seen from the other side, exactly as the loot roller
            // sees it: not on the floor and owned by nobody.
            _freeItemQuery = SystemAPI.QueryBuilder()
                .WithAll<ItemInstance>()
                .WithDisabled<InteractableTag>()
                .WithDisabled<ItemStored>()
                .Build();

            _pendingQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, CarriedBag>()
                .WithNone<StarterKitGranted>()
                .Build();

            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<SkillDatabase>();
            state.RequireForUpdate<StarterItem>();
            state.RequireForUpdate(_pendingQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<StarterItem> kit =
                SystemAPI.GetSingletonBuffer<StarterItem>(isReadOnly: true);

            if (kit.Length == 0)
                return;

            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            // Copied out before anything is granted: adding StarterKitGranted is
            // a structural change, and a query result taken before it would not
            // survive.
            using NativeArray<Entity> characters = _pendingQuery.ToEntityArray(Allocator.Temp);

            // A list filled by Add rather than an array written through its
            // indexer: a using-declared variable is readonly, and writing to one
            // that way does not compile.
            using var itemIds = new NativeList<int>(kit.Length, Allocator.Temp);

            for (int i = 0; i < kit.Length; i++)
                itemIds.Add(kit[i].ItemId);

            for (int i = 0; i < characters.Length; i++)
                Grant(ref state, items, characters[i], itemIds.AsArray());
        }

        private void Grant(
            ref SystemState state,
            ItemDatabase items,
            Entity character,
            in NativeArray<int> itemIds)
        {
            EntityManager entityManager = state.EntityManager;

            using NativeArray<Entity> free = _freeItemQuery.ToEntityArray(Allocator.Temp);

            if (free.Length < itemIds.Length)
            {
                UnityEngine.Debug.LogWarning(
                    "[StarterKitSystem] The item pool is too small for the starter kit — " +
                    $"{free.Length} free, {itemIds.Length} needed.");
                return;
            }

            Entity bag = entityManager.GetComponentData<CarriedBag>(character).Container;
            Entity gear = Entity.Null;

            // Nothing structural happens in this loop, so the buffers taken
            // inside it stay valid for its whole run.
            for (int i = 0; i < itemIds.Length; i++)
            {
                Entity item = free[i];

                if (items.IndexOf(itemIds[i]) < 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[StarterKitSystem] Starter item {itemIds[i]} is not in the database.");
                    continue;
                }

                entityManager.SetComponentData(item, new ItemInstance
                {
                    ItemId = itemIds[i],
                    Rarity = ItemRarity.Common,

                    // Everything carried into a dungeon is at risk, found here
                    // or brought along.
                    RiskState = ItemRiskState.AtRisk
                });

                // Owned from this moment, so the pool cannot hand it out twice.
                LootItemPool.Store(entityManager, item);
                GemSockets.Rebuild(entityManager, items, item);

                if (i == 0)
                {
                    gear = item;
                    continue;
                }

                PlaceInBag(entityManager, items, bag, item);
            }

            if (gear != Entity.Null)
                Equip(entityManager, character, gear, items);

            entityManager.AddComponent<StarterKitGranted>(character);

            UnityEngine.Debug.Log(
                $"[StarterKitSystem] Starter kit granted with {itemIds.Length - 1} gems.");
        }

        /// <summary>
        /// Lays a gem out in the bag, wherever it fits.
        ///
        /// Through GridFit rather than a placement request, because the kit runs
        /// before anybody has had a chance to send one and the items are already
        /// owned — there is nothing to check and nobody to answer.
        /// </summary>
        private static void PlaceInBag(
            EntityManager entityManager, ItemDatabase items, Entity bag, Entity item)
        {
            if (bag == Entity.Null || !entityManager.HasComponent<InventoryCell>(bag))
                return;

            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            if (!GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                return;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            if (!GridFit.FindFirstFit(cells, grid, width, height, item, out int x, out int y))
            {
                UnityEngine.Debug.LogWarning(
                    "[StarterKitSystem] No room in the bag for the whole starter kit.");
                return;
            }

            GridFit.Occupy(cells, grid, x, y, width, height, item);

            entityManager.SetComponentData(item, new ItemGridPlacement
            {
                ContainerEntity = bag,
                OriginX = x,
                OriginY = y,
                IsRotated = false
            });
        }

        /// <summary>
        /// Wears the starter gear, without going through an equip request.
        ///
        /// A request would be the tidier route and would be checked against a
        /// bag the gear is not in — the kit hands things out already owned, so
        /// there is nothing to move and nothing to check.
        /// </summary>
        private static void Equip(
            EntityManager entityManager, Entity character, Entity gear, ItemDatabase items)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(gear).ItemId;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return;

            ref ItemBlob blob = ref items.Value.Value.Items[index];
            int slotIndex = (int)blob.Slot;

            DynamicBuffer<EquippedItem> slots =
                entityManager.GetBuffer<EquippedItem>(character);

            if (slotIndex < 0 || slotIndex >= slots.Length)
                return;

            EquippedItem slot = slots[slotIndex];
            slot.Item = gear;
            slot.ItemId = itemId;
            slots[slotIndex] = slot;

            entityManager.SetComponentEnabled<StatsDirty>(character, true);
        }

    }
}
