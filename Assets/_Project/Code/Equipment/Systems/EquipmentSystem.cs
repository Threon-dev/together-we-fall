using Unity.Entities;
using TogetherWeFall.Inventory;
using TogetherWeFall.Inventory.Systems;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Applies equip and unequip requests.
    ///
    /// The host half of the same split the interaction code uses. The UI says
    /// what it wants; this checks that the item is really in that player's bag,
    /// and puts it in the slot the ITEM says it belongs in rather than the one
    /// the request claimed. A request is a wish, not an instruction — which is
    /// the whole reason it is a separate type.
    ///
    /// Since the grid inventory, equipping is also a move: the item leaves the
    /// bag, and whatever it replaces has to come back into it. That move is done
    /// here rather than by posting a placement request, because it has to be
    /// indivisible — an unequip that cleared the slot and then found no room
    /// would leave the item nowhere at all. The rules still live in exactly one
    /// place; GridFit is called, not reimplemented.
    ///
    /// It never computes a stat. All it does is raise StatsDirty, and the
    /// recompute happens once, in the system that owns that job.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InventoryPlacementSystem))]
    public partial struct EquipmentSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            EntityManager entityManager = state.EntityManager;

            // WithPresent, because StatsDirty is down on every character whose
            // stats are up to date — which is exactly the set this loop exists
            // to raise it on.
            foreach ((DynamicBuffer<EquipRequest> requests,
                      DynamicBuffer<EquippedItem> slots,
                      RefRO<CarriedBag> bag,
                      EnabledRefRW<StatsDirty> dirty,
                      RefRO<PlayerCharacter> character) in
                     SystemAPI.Query<DynamicBuffer<EquipRequest>,
                         DynamicBuffer<EquippedItem>,
                         RefRO<CarriedBag>,
                         EnabledRefRW<StatsDirty>,
                         RefRO<PlayerCharacter>>()
                         .WithPresent<StatsDirty>())
            {
                if (requests.Length == 0)
                    continue;

                bool changed = false;

                for (int i = 0; i < requests.Length; i++)
                {
                    changed |= Apply(
                        entityManager, requests[i], items, slots,
                        bag.ValueRO.Container, character.ValueRO.PlayerId);
                }

                // Requests live for one frame. A rejected one is not retried:
                // the answer would be the same, and a queue that never drains is
                // a queue that grows.
                requests.Clear();

                if (changed)
                    dirty.ValueRW = true;
            }
        }

        private static bool Apply(
            EntityManager entityManager,
            in EquipRequest request,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            if (bag == Entity.Null || !entityManager.HasComponent<InventoryCell>(bag))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no bag — request refused.");
                return false;
            }

            return request.Equip
                ? Equip(entityManager, request.Item, items, slots, bag, playerId)
                : Unequip(entityManager, request.Slot, items, slots, bag, playerId);
        }

        /// <summary>
        /// Puts an item on, and puts whatever it replaces back in the bag.
        ///
        /// The order is what makes it safe: the incoming item's cells are freed
        /// first, so the outgoing one may legitimately use the space that just
        /// opened up — a two-by-three chest piece swapping for another
        /// two-by-three works in a bag with no other free room at all. If the
        /// outgoing item then does not fit, everything is put back and the answer
        /// is no.
        /// </summary>
        private static bool Equip(
            EntityManager entityManager,
            Entity item,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            if (!Owns(entityManager, item, bag))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} asked to equip an item that is not in " +
                    "their bag — refused.");
                return false;
            }

            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            int index = items.IndexOf(itemId);
            if (index < 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Item {itemId} is not in the item database — refused.");
                return false;
            }

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful.
            ref ItemBlob blob = ref items.Value.Value.Items[index];

            // The slot comes from the item, not from the request. A client that
            // could choose the slot could wear a two-handed sword as a hat.
            int slot = (int)blob.Slot;
            if (slot >= slots.Length)
                return false;

            EquippedItem current = slots[slot];
            if (current.Item == item)
                return false;

            ItemGridPlacement placement = entityManager.GetComponentData<ItemGridPlacement>(item);
            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            GridFit.Clear(cells, item);

            // Nothing worn there: the space the incoming item vacated simply
            // stays free.
            if (!current.HasItem)
            {
                Wear(entityManager, slots, slot, blob.Slot, item, itemId);
                return true;
            }

            if (!TryReturnToBag(entityManager, items, cells, grid, bag, current.Item))
            {
                // Put the incoming item back exactly where it was. A refused
                // request must leave the world untouched.
                Restore(items, cells, grid, item, placement, itemId);

                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no room to unequip what they are " +
                    "wearing — swap refused.");
                return false;
            }

            Wear(entityManager, slots, slot, blob.Slot, item, itemId);
            return true;
        }

        private static bool Unequip(
            EntityManager entityManager,
            EquipmentSlot slot,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            int index = (int)slot;

            if (index >= slots.Length || !slots[index].HasItem)
                return false;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            // Checked before the slot is cleared, never after. This is the whole
            // reason the move is not two systems: an item that has left the slot
            // and not arrived in the bag exists nowhere.
            if (!TryReturnToBag(entityManager, items, cells, grid, bag, slots[index].Item))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no room in their bag — unequip " +
                    "refused.");
                return false;
            }

            EquippedItem target = slots[index];
            target.Item = Entity.Null;
            target.ItemId = EquippedItem.Empty;
            slots[index] = target;

            return true;
        }

        /// <summary>
        /// Finds room for an item and writes it into the grid, unrotated first
        /// and turned on its side only if it has to be — and only if the item
        /// says it may.
        /// </summary>
        private static bool TryReturnToBag(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            Entity bag,
            Entity item)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            if (!GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                return false;

            bool rotated = false;

            if (!GridFit.FindFirstFit(
                    cells, grid, width, height, item, out int originX, out int originY))
            {
                if (!GridFit.CanRotate(items, itemId) ||
                    !GridFit.TryGetFootprint(items, itemId, true, out width, out height) ||
                    !GridFit.FindFirstFit(
                        cells, grid, width, height, item, out originX, out originY))
                {
                    return false;
                }

                rotated = true;
            }

            GridFit.Occupy(cells, grid, originX, originY, width, height, item);

            entityManager.SetComponentData(item, new ItemGridPlacement
            {
                ContainerEntity = bag,
                OriginX = originX,
                OriginY = originY,
                IsRotated = rotated
            });

            return true;
        }

        /// <summary>Puts an item back exactly where it was before a refused swap.</summary>
        private static void Restore(
            ItemDatabase items,
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            Entity item,
            in ItemGridPlacement placement,
            int itemId)
        {
            if (!GridFit.TryGetFootprint(
                    items, itemId, placement.IsRotated, out int width, out int height))
            {
                return;
            }

            GridFit.Occupy(
                cells, grid, placement.OriginX, placement.OriginY, width, height, item);
        }

        /// <summary>
        /// Moves an item into a slot. It stays owned — ItemStored is untouched —
        /// and simply stops being in any container.
        /// </summary>
        private static void Wear(
            EntityManager entityManager,
            DynamicBuffer<EquippedItem> slots,
            int slotIndex,
            EquipmentSlot slot,
            Entity item,
            int itemId)
        {
            entityManager.SetComponentData(item, new ItemGridPlacement
            {
                ContainerEntity = Entity.Null,
                OriginX = 0,
                OriginY = 0,
                IsRotated = false
            });

            EquippedItem target = slots[slotIndex];
            target.Slot = slot;
            target.Item = item;
            target.ItemId = itemId;
            slots[slotIndex] = target;
        }

        /// <summary>
        /// Whether this item is in this bag.
        ///
        /// Asked of the item rather than by searching the cells: the placement
        /// names its container directly, and an item that claims a container it
        /// is not written into would be a bug in GridFit, not something to paper
        /// over here.
        /// </summary>
        private static bool Owns(EntityManager entityManager, Entity item, Entity bag)
        {
            if (item == Entity.Null || !entityManager.Exists(item))
                return false;

            if (!entityManager.HasComponent<ItemInstance>(item) ||
                !entityManager.HasComponent<ItemGridPlacement>(item))
            {
                return false;
            }

            return entityManager.GetComponentData<ItemGridPlacement>(item).ContainerEntity == bag;
        }
    }
}
