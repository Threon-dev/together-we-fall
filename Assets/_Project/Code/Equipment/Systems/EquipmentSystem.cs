using Unity.Entities;
using TogetherWeFall.Player;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Applies equip and unequip requests.
    ///
    /// The host half of the same split the interaction code uses. The UI says
    /// what it wants; this checks that the item is really in that player's
    /// inventory, and puts it in the slot the ITEM says it belongs in rather
    /// than the one the request claimed. A request is a wish, not an
    /// instruction — which is the whole reason it is a separate type.
    ///
    /// It never computes a stat. All it does is raise StatsDirty, and the
    /// recompute happens once, in the system that owns that job.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
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

            // WithPresent, because StatsDirty is down on every character whose
            // stats are up to date — which is exactly the set this loop exists
            // to raise it on.
            foreach ((DynamicBuffer<EquipRequest> requests,
                      DynamicBuffer<EquippedItem> slots,
                      DynamicBuffer<InventoryItem> inventory,
                      EnabledRefRW<StatsDirty> dirty,
                      RefRO<PlayerCharacter> character) in
                     SystemAPI.Query<DynamicBuffer<EquipRequest>,
                         DynamicBuffer<EquippedItem>,
                         DynamicBuffer<InventoryItem>,
                         EnabledRefRW<StatsDirty>,
                         RefRO<PlayerCharacter>>()
                         .WithPresent<StatsDirty>())
            {
                if (requests.Length == 0)
                    continue;

                bool changed = false;

                for (int i = 0; i < requests.Length; i++)
                    changed |= Apply(requests[i], items, slots, inventory, character.ValueRO.PlayerId);

                // Requests live for one frame. A rejected one is not retried:
                // the answer would be the same, and a queue that never drains is
                // a queue that grows.
                requests.Clear();

                if (changed)
                    dirty.ValueRW = true;
            }
        }

        private static bool Apply(
            in EquipRequest request,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            DynamicBuffer<InventoryItem> inventory,
            int playerId)
        {
            return request.Equip
                ? Equip(request.ItemId, items, slots, inventory, playerId)
                : Unequip(request.Slot, slots);
        }

        private static bool Equip(
            int itemId,
            ItemDatabase items,
            DynamicBuffer<EquippedItem> slots,
            DynamicBuffer<InventoryItem> inventory,
            int playerId)
        {
            if (!Owns(inventory, itemId))
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Player {playerId} asked to equip item {itemId}, which is " +
                    "not in their inventory — refused.");
                return false;
            }

            int index = items.IndexOf(itemId);
            if (index < 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[EquipmentSystem] Item {itemId} is not in the item database — refused.");
                return false;
            }

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful.
            ref ItemBlob item = ref items.Value.Value.Items[index];

            // The slot comes from the item, not from the request. A client that
            // could choose the slot could wear a two-handed sword as a hat.
            int slot = (int)item.Slot;
            if (slot >= slots.Length)
                return false;

            if (slots[slot].ItemId == itemId)
                return false;

            EquippedItem target = slots[slot];
            target.Slot = item.Slot;
            target.ItemId = itemId;
            slots[slot] = target;

            return true;
        }

        private static bool Unequip(EquipmentSlot slot, DynamicBuffer<EquippedItem> slots)
        {
            int index = (int)slot;

            if (index >= slots.Length || !slots[index].HasItem)
                return false;

            EquippedItem target = slots[index];
            target.ItemId = EquippedItem.Empty;
            slots[index] = target;

            return true;
        }

        private static bool Owns(DynamicBuffer<InventoryItem> inventory, int itemId)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].ItemId == itemId)
                    return true;
            }

            return false;
        }
    }
}
