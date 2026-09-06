using Unity.Entities;
using TogetherWeFall.Inventory;
using TogetherWeFall.Inventory.Systems;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Applies every change to what a character is wearing.
    ///
    /// The host half of the same split the interaction code uses. The UI says
    /// what it wants; this checks that the item is really in that player's bag
    /// and that its definition allows the slot named. The slot travels with the
    /// request now rather than being read off the item — a ring fits in two
    /// places and only the player knows which — but it is CHECKED against the
    /// mask the baker derived, so a two-handed sword still cannot be worn as a
    /// hat.
    ///
    /// One system for equip, unequip and slot-to-slot, and every one of them is
    /// a single transaction. That is not tidiness. Putting a two-handed weapon
    /// on empties the off hand, which means two slots and the bag all change
    /// together; split across two systems there is a frame in which both hands
    /// hold something, and a refusal halfway through leaves an item owned by
    /// nobody. Everything that could fail is therefore checked before anything
    /// is written, and the one speculative step — clearing the incoming item out
    /// of the grid so it cannot collide with itself — is undone on every path
    /// that says no.
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
                      DynamicBuffer<EquipResult> results,
                      DynamicBuffer<EquippedItem> slots,
                      RefRO<CarriedBag> bag,
                      EnabledRefRW<StatsDirty> dirty,
                      RefRO<PlayerCharacter> character) in
                     SystemAPI.Query<DynamicBuffer<EquipRequest>,
                         DynamicBuffer<EquipResult>,
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
                    EquipResult result = Apply(
                        entityManager, requests[i], items, slots,
                        bag.ValueRO.Container, character.ValueRO.PlayerId);

                    results.Add(result);
                    changed |= result.Succeeded;
                }

                // Requests live for one frame. A rejected one is not retried:
                // the answer would be the same, and a queue that never drains is
                // a queue that grows.
                requests.Clear();

                if (changed)
                    dirty.ValueRW = true;
            }
        }

        private static EquipResult Apply(
            EntityManager entityManager,
            in EquipRequest request,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            // SwapSlots is the one kind that never touches the bag, so it is the
            // one kind that still works when there is no bag at all.
            if (request.Kind != EquipRequestKind.SwapSlots &&
                (bag == Entity.Null || !entityManager.HasComponent<InventoryCell>(bag)))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no bag — request refused.");
                return Reject(request.Item, request.Slot, EquipStatus.RejectedNoBag);
            }

            switch (request.Kind)
            {
                case EquipRequestKind.ToSlot:
                    return EquipToSlot(
                        entityManager, request.Item, request.Slot, items, slots, bag, playerId);

                case EquipRequestKind.Auto:
                    return EquipAuto(entityManager, request.Item, items, slots, bag, playerId);

                case EquipRequestKind.Unequip:
                    return Unequip(entityManager, request, items, slots, bag, playerId);

                default:
                    return SwapSlots(
                        entityManager, request.Slot, request.OtherSlot, items, slots, playerId);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Equip
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Picks the slot the player did not name, then equips into it.
        ///
        /// An empty allowed slot first, so double-clicking a second ring fills
        /// the other hand rather than replacing the first one. Only when every
        /// allowed slot is full does it take the lowest and swap.
        /// </summary>
        private static EquipResult EquipAuto(
            EntityManager entityManager,
            Entity item,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            if (!TryDescribe(entityManager, items, item, out ItemFacts facts))
                return Reject(item, EquipmentSlot.MainHand, EquipStatus.RejectedUnknownItem);

            int chosen = EquipmentSlots.Count;

            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (!EquipmentSlots.Accepts(facts.AllowedSlots, (EquipmentSlot)slot))
                    continue;

                if (chosen == EquipmentSlots.Count)
                    chosen = slot;

                if (!slots[slot].HasItem)
                {
                    chosen = slot;
                    break;
                }
            }

            if (chosen >= EquipmentSlots.Count)
                return Reject(item, EquipmentSlot.MainHand, EquipStatus.RejectedWrongSlot);

            return EquipToSlot(
                entityManager, item, (EquipmentSlot)chosen, items, slots, bag, playerId);
        }

        /// <summary>
        /// Puts an item in a named slot, and puts everything it displaces back
        /// in the bag.
        ///
        /// Displaced is up to two items: whatever was in the slot, and — for a
        /// two-handed weapon going into the main hand — whatever was in the off
        /// hand. Both have to fit, or neither moves. The incoming item's cells
        /// are freed first so that the displaced ones may legitimately use the
        /// space it just vacated, which is what lets a two-by-three chest piece
        /// swap for another in a bag with no other free room at all.
        /// </summary>
        private static EquipResult EquipToSlot(
            EntityManager entityManager,
            Entity item,
            EquipmentSlot targetSlot,
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
                return Reject(item, targetSlot, EquipStatus.RejectedNotCarried);
            }

            if (!TryDescribe(entityManager, items, item, out ItemFacts facts))
                return Reject(item, targetSlot, EquipStatus.RejectedUnknownItem);

            int slotIndex = (int)targetSlot;

            if (slotIndex >= slots.Length ||
                !EquipmentSlots.Accepts(facts.AllowedSlots, targetSlot))
            {
                return Reject(item, targetSlot, EquipStatus.RejectedWrongSlot);
            }

            // Nothing goes in the off hand while the main hand needs both. The
            // question is asked of the slots rather than of a stored flag, so it
            // cannot be stale.
            if (targetSlot == EquipmentSlot.OffHand &&
                EquipmentSlots.IsOffHandBlocked(slots, items))
            {
                return Reject(item, targetSlot, EquipStatus.RejectedOffHandBlocked);
            }

            if (slots[slotIndex].Item == item)
                return Reject(item, targetSlot, EquipStatus.RejectedWrongSlot);

            ItemGridPlacement placement = entityManager.GetComponentData<ItemGridPlacement>(item);
            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            // Speculative, and undone on every path below that says no.
            GridFit.Clear(cells, item);

            Entity displaced = slots[slotIndex].Item;

            // The off hand empties only for a two-hander arriving in the main
            // hand — and only if there is something in it to empty.
            int offHand = (int)EquipmentSlot.OffHand;
            Entity displacedOffHand = Entity.Null;

            if (facts.IsTwoHanded && targetSlot == EquipmentSlot.MainHand &&
                offHand < slots.Length && slots[offHand].HasItem)
            {
                displacedOffHand = slots[offHand].Item;
            }

            if (!TryReturnBoth(
                    entityManager, items, cells, grid, bag, displaced, displacedOffHand))
            {
                // Nothing moved. Put the incoming item back exactly where it
                // was: a refused request must leave the world untouched.
                Restore(items, cells, grid, item, placement, facts.ItemId);

                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no room in their bag for what " +
                    $"{targetSlot} would displace — refused.");
                return Reject(item, targetSlot, EquipStatus.RejectedBagFull);
            }

            if (displaced != Entity.Null)
                ClearSlot(slots, slotIndex);

            if (displacedOffHand != Entity.Null)
                ClearSlot(slots, offHand);

            Wear(entityManager, slots, slotIndex, targetSlot, item, facts.ItemId);

            return new EquipResult
            {
                Item = item,
                Slot = targetSlot,
                Status = EquipStatus.Equipped
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Unequip
        // ─────────────────────────────────────────────────────────────────

        private static EquipResult Unequip(
            EntityManager entityManager,
            in EquipRequest request,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            Entity bag,
            int playerId)
        {
            int slotIndex = (int)request.Slot;

            if (slotIndex >= slots.Length || !slots[slotIndex].HasItem)
                return Reject(Entity.Null, request.Slot, EquipStatus.RejectedEmptySlot);

            Entity item = slots[slotIndex].Item;

            InventoryGridComponent grid =
                entityManager.GetComponentData<InventoryGridComponent>(bag);
            DynamicBuffer<InventoryCell> cells = entityManager.GetBuffer<InventoryCell>(bag);

            // Checked before the slot is cleared, never after. An item that has
            // left the slot and not arrived in the bag exists nowhere.
            if (!TryReturnToBag(
                    entityManager, items, cells, grid, bag, item,
                    request.UseTarget, request.TargetX, request.TargetY, request.Rotated))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} has no room in their bag — unequip " +
                    "refused.");
                return Reject(item, request.Slot, EquipStatus.RejectedBagFull);
            }

            ClearSlot(slots, slotIndex);

            return new EquipResult
            {
                Item = item,
                Slot = request.Slot,
                Status = EquipStatus.Unequipped
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Slot to slot
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves what is worn in one slot to another, exchanging with whatever
        /// is already there.
        ///
        /// Its own path rather than an unequip followed by an equip, because it
        /// never touches the bag: moving a ring from one hand to the other works
        /// with no free space at all, and going through the bag would refuse it
        /// for being full. Both directions are checked, so a swap cannot leave
        /// an item somewhere its definition forbids.
        /// </summary>
        private static EquipResult SwapSlots(
            EntityManager entityManager,
            EquipmentSlot from,
            EquipmentSlot to,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            int playerId)
        {
            int fromIndex = (int)from;
            int toIndex = (int)to;

            if (fromIndex >= slots.Length || toIndex >= slots.Length || fromIndex == toIndex)
                return Reject(Entity.Null, from, EquipStatus.RejectedWrongSlot);

            if (!slots[fromIndex].HasItem)
                return Reject(Entity.Null, from, EquipStatus.RejectedEmptySlot);

            Entity moving = slots[fromIndex].Item;

            if (!TryDescribe(entityManager, items, moving, out ItemFacts movingFacts))
                return Reject(moving, to, EquipStatus.RejectedUnknownItem);

            if (!EquipmentSlots.Accepts(movingFacts.AllowedSlots, to))
                return Reject(moving, to, EquipStatus.RejectedWrongSlot);

            Entity displaced = slots[toIndex].Item;
            int displacedId = slots[toIndex].ItemId;

            if (displaced != Entity.Null)
            {
                if (!TryDescribe(entityManager, items, displaced, out ItemFacts displacedFacts))
                    return Reject(displaced, from, EquipStatus.RejectedUnknownItem);

                if (!EquipmentSlots.Accepts(displacedFacts.AllowedSlots, from))
                    return Reject(displaced, from, EquipStatus.RejectedWrongSlot);
            }

            // A two-hander cannot be moved out of the main hand into anywhere
            // else — its mask says MainHand only — so the off hand cannot become
            // valid or invalid as a result of this. Nothing to unblock.
            int movingId = slots[fromIndex].ItemId;

            WriteSlot(slots, toIndex, to, moving, movingId);
            WriteSlot(slots, fromIndex, from, displaced, displacedId);

            UnityEngine.Debug.Log(
                $"[EquipmentSystem] Player {playerId} moved {from} to {to}.");

            return new EquipResult
            {
                Item = moving,
                Slot = to,
                Status = EquipStatus.Swapped
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Bag
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts up to two displaced items back in the bag, all or nothing.
        ///
        /// The second one failing has to undo the first, or a refused two-handed
        /// equip would leave the off-hand item lying in the grid while still
        /// recorded as worn.
        /// </summary>
        private static bool TryReturnBoth(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            Entity bag,
            Entity first,
            Entity second)
        {
            if (first != Entity.Null &&
                !TryReturnToBag(entityManager, items, cells, grid, bag, first,
                    false, 0, 0, false))
            {
                return false;
            }

            if (second != Entity.Null &&
                !TryReturnToBag(entityManager, items, cells, grid, bag, second,
                    false, 0, 0, false))
            {
                // Undo the first: it is back out of the grid and still worn,
                // which is exactly the state everything was in on entry.
                if (first != Entity.Null)
                    GridFit.Clear(cells, first);

                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds room for an item and writes it into the grid.
        ///
        /// The square the player pointed at first, when they pointed at one and
        /// it is free. Otherwise unrotated first, and turned on its side only if
        /// it has to be and the item says it may.
        /// </summary>
        private static bool TryReturnToBag(
            EntityManager entityManager,
            ItemDatabase items,
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            Entity bag,
            Entity item,
            bool useTarget,
            int targetX,
            int targetY,
            bool rotated)
        {
            int itemId = entityManager.GetComponentData<ItemInstance>(item).ItemId;

            if (useTarget && (!rotated || GridFit.CanRotate(items, itemId)) &&
                GridFit.TryGetFootprint(items, itemId, rotated, out int tw, out int th) &&
                GridFit.Fits(cells, grid, targetX, targetY, tw, th, item))
            {
                Place(entityManager, cells, grid, bag, item, targetX, targetY, tw, th, rotated);
                return true;
            }

            if (!GridFit.TryGetFootprint(items, itemId, false, out int width, out int height))
                return false;

            bool turned = false;

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

                turned = true;
            }

            Place(entityManager, cells, grid, bag, item, originX, originY, width, height, turned);
            return true;
        }

        private static void Place(
            EntityManager entityManager,
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            Entity bag,
            Entity item,
            int originX,
            int originY,
            int width,
            int height,
            bool rotated)
        {
            GridFit.Occupy(cells, grid, originX, originY, width, height, item);

            entityManager.SetComponentData(item, new ItemGridPlacement
            {
                ContainerEntity = bag,
                OriginX = originX,
                OriginY = originY,
                IsRotated = rotated
            });
        }

        /// <summary>Puts an item back exactly where it was before a refused move.</summary>
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

        // ─────────────────────────────────────────────────────────────────
        // Slots
        // ─────────────────────────────────────────────────────────────────

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

            WriteSlot(slots, slotIndex, slot, item, itemId);
        }

        private static void ClearSlot(DynamicBuffer<EquippedItem> slots, int slotIndex)
        {
            WriteSlot(slots, slotIndex, slots[slotIndex].Slot, Entity.Null, EquippedItem.Empty);
        }

        private static void WriteSlot(
            DynamicBuffer<EquippedItem> slots,
            int slotIndex,
            EquipmentSlot slot,
            Entity item,
            int itemId)
        {
            EquippedItem target = slots[slotIndex];
            target.Slot = slot;
            target.Item = item;
            target.ItemId = itemId;
            slots[slotIndex] = target;
        }

        // ─────────────────────────────────────────────────────────────────
        // Lookups
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The three things about an item this system decides with, copied out
        /// of the blob as values.
        ///
        /// Copied rather than held as a ref because ItemBlob carries a
        /// BlobArray, and a struct holding one cannot travel. These are scalars,
        /// so they can.
        /// </summary>
        private struct ItemFacts
        {
            public int ItemId;
            public ushort AllowedSlots;
            public bool IsTwoHanded;
        }

        private static bool TryDescribe(
            EntityManager entityManager, ItemDatabase items, Entity item, out ItemFacts facts)
        {
            facts = default;

            if (item == Entity.Null || !entityManager.Exists(item) ||
                !entityManager.HasComponent<ItemInstance>(item))
            {
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

            ref ItemBlob blob = ref items.Value.Value.Items[index];

            facts = new ItemFacts
            {
                ItemId = itemId,
                AllowedSlots = blob.AllowedSlots,
                IsTwoHanded = blob.IsTwoHanded
            };

            return true;
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

        private static EquipResult Reject(Entity item, EquipmentSlot slot, EquipStatus status)
        {
            return new EquipResult
            {
                Item = item,
                Slot = slot,
                Status = status
            };
        }
    }
}
