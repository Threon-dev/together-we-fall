using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Skills.Systems
{
    /// <summary>
    /// One item a new character is given, in the order it is granted.
    ///
    /// Baked from the starter kit asset, one entry per copy — a line asking for
    /// ten coins arrives here as ten of these. Beside the character sheet,
    /// because what a character starts with is authored content rather than
    /// something the code should name.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct StarterItem : IBufferElementData
    {
        public int ItemId;

        /// <summary>
        /// Whether this copy is worn rather than carried.
        ///
        /// A flag per entry, and it replaces the old rule that the FIRST entry
        /// was the gear and everything after it was luggage. That rule could not
        /// express "start wearing four pieces of this set", which is the only
        /// way to look at a set bonus without playing for an hour first — and a
        /// kit is a test bench before it is content.
        /// </summary>
        public bool Worn;
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
    /// is that the arrangement is the player's. Gear is worn when the kit asks
    /// for it, because a character with no gear has nowhere to put a gem at
    /// all.
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

            // The same stream the loot roller uses. A weapon rolls the skills it
            // comes with when it is handed out, and the kit hands out a weapon.
            state.RequireForUpdate<LootRandom>();
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
            using var wanted = new NativeList<StarterItem>(kit.Length, Allocator.Temp);

            for (int i = 0; i < kit.Length; i++)
                wanted.Add(kit[i]);

            for (int i = 0; i < characters.Length; i++)
                Grant(ref state, items, characters[i], wanted.AsArray());
        }

        private void Grant(
            ref SystemState state,
            ItemDatabase items,
            Entity character,
            in NativeArray<StarterItem> wanted)
        {
            EntityManager entityManager = state.EntityManager;

            using NativeArray<Entity> free = _freeItemQuery.ToEntityArray(Allocator.Temp);

            // Held across the loop below, which the comment there says is free
            // of structural changes — and that is exactly what makes holding a
            // singleton reference through it safe.
            RefRW<LootRandom> random = SystemAPI.GetSingletonRW<LootRandom>();

            if (free.Length < wanted.Length)
            {
                UnityEngine.Debug.LogWarning(
                    "[StarterKitSystem] The item pool is too small for the starter kit — " +
                    $"{free.Length} free, {wanted.Length} needed.");
                return;
            }

            Entity bag = entityManager.GetComponentData<CarriedBag>(character).Container;
            bool anyWorn = false;

            // Nothing structural happens in this loop, so the buffers taken
            // inside it stay valid for its whole run.
            for (int i = 0; i < wanted.Length; i++)
            {
                Entity item = free[i];

                if (items.IndexOf(wanted[i].ItemId) < 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[StarterKitSystem] Starter item {wanted[i].ItemId} is not in the " +
                        "database.");
                    continue;
                }

                entityManager.SetComponentData(item, new ItemInstance
                {
                    ItemId = wanted[i].ItemId,
                    Rarity = ItemRarity.Common,

                    // Everything carried into a dungeon is at risk, found here
                    // or brought along.
                    RiskState = ItemRiskState.AtRisk
                });

                // Owned from this moment, so the pool cannot hand it out twice.
                LootItemPool.Store(entityManager, item);
                GemSockets.Rebuild(entityManager, items, item, ref random.ValueRW.Value);

                // The starting coins go into the purse rather than the bag,
                // through the same call the pickup stage uses. The kit still
                // names them as items, because "how much money does a character
                // start with" is content and a coin is how the content says it.
                if (Currency.TryCollect(entityManager, items, character, item))
                    continue;

                // Worn if the kit asked for it and there is a slot to take it.
                // Otherwise it falls back to the bag rather than being dropped:
                // a kit line that cannot be worn is an authoring mistake, and
                // losing the item as well would hide it.
                if (wanted[i].Worn && TryWear(entityManager, character, item, items))
                {
                    anyWorn = true;
                    continue;
                }

                PlaceInBag(entityManager, items, bag, item);
            }

            if (anyWorn)
            {
                // The same arming the equip system does, called by hand because
                // this path deliberately skips it: the kit hands out gear that
                // is already owned, so there is no request to send. Without this
                // a new character wears a weapon whose attack no key casts.
                GemSockets.ArmDefaultAttack(
                    entityManager,
                    items,
                    SystemAPI.GetSingleton<SkillDatabase>(),
                    entityManager.GetBuffer<EquippedItem>(character),
                    entityManager.GetBuffer<SkillSlot>(character));
            }

            entityManager.AddComponent<StarterKitGranted>(character);

            UnityEngine.Debug.Log(
                $"[StarterKitSystem] Starter kit granted: {wanted.Length} item(s).");
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
        /// Wears one piece of the kit, without going through an equip request.
        ///
        /// A request would be the tidier route and would be checked against a
        /// bag the gear is not in — the kit hands things out already owned, so
        /// there is nothing to move and nothing to check.
        ///
        /// The slot is the first FREE one the item is allowed in, asked of the
        /// same EquipmentSlots the host asks: a second ring finds the other
        /// hand and a third finds nothing. Nothing is ever displaced, because
        /// the kit runs before anybody could have chosen what to displace.
        ///
        /// False when the item cannot be worn — a gem, a full slot, or a hand
        /// the other hand has taken — and the caller puts it in the bag
        /// instead, saying so once in the console. A kit line that cannot be
        /// worn is an authoring mistake, and swallowing the item as well would
        /// hide it.
        /// </summary>
        private static bool TryWear(
            EntityManager entityManager, Entity character, Entity item, ItemDatabase items)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return false;

            // By reference: ItemBlob carries a BlobArray, and copying the struct
            // leaves its affixes pointing at nothing. The values taken out of it
            // are flat, so they may travel.
            ref ItemBlob blob = ref items.Value.Value.Items[index];

            ushort allowed = blob.AllowedSlots;
            bool twoHanded = blob.IsTwoHanded;
            GemKind gem = blob.GemKind;
            FixedString64Bytes name = blob.Name;

            if (gem != GemKind.None)
            {
                UnityEngine.Debug.LogWarning(
                    $"[StarterKitSystem] The kit asks to wear '{name}', which is a gem — " +
                    "it goes in the bag. Socketing is the player's job.");
                return false;
            }

            DynamicBuffer<EquippedItem> slots =
                entityManager.GetBuffer<EquippedItem>(character);

            if (!EquipmentSlots.TryFirstFree(slots, allowed, out int slotIndex))
            {
                UnityEngine.Debug.LogWarning(
                    $"[StarterKitSystem] No free slot for '{name}' — it goes in the bag.");
                return false;
            }

            // Both hands, derived from what is worn exactly as the equip system
            // derives it, so the kit cannot reach a state a player could not:
            // the off hand is unusable while the main hand needs both, and a
            // two-handed weapon needs the off hand empty.
            var target = (EquipmentSlot)slotIndex;
            int offHand = (int)EquipmentSlot.OffHand;

            if (target == EquipmentSlot.OffHand &&
                EquipmentSlots.IsOffHandBlocked(slots, items))
            {
                UnityEngine.Debug.LogWarning(
                    $"[StarterKitSystem] '{name}' cannot go in the off hand while a " +
                    "two-handed weapon is worn — it goes in the bag.");
                return false;
            }

            if (twoHanded && target == EquipmentSlot.MainHand &&
                offHand < slots.Length && slots[offHand].HasItem)
            {
                UnityEngine.Debug.LogWarning(
                    $"[StarterKitSystem] '{name}' needs both hands and the off hand is " +
                    "taken — it goes in the bag. List it before the off-hand item.");
                return false;
            }

            EquippedItem slot = slots[slotIndex];
            slot.Item = item;
            slot.ItemId = itemId;
            slots[slotIndex] = slot;

            entityManager.SetComponentEnabled<StatsDirty>(character, true);
            return true;
        }

    }
}
