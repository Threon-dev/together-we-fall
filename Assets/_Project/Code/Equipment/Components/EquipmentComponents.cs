using Unity.Entities;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Equipment
{
    /// <summary>
    /// Where an item goes. The index into the equipment buffer, so values may be
    /// appended but never reshuffled.
    /// </summary>
    public enum EquipmentSlot : byte
    {
        Weapon = 0,
        Helmet = 1,
        Armour = 2,
        Accessory = 3
    }

    /// <summary>
    /// One equipment slot on a character. Exactly one element per slot, in enum
    /// order, so the buffer is addressed rather than searched.
    ///
    /// A buffer instead of four fields on a component because slots are the
    /// thing most likely to grow — rings, boots, a second weapon — and every one
    /// of those should be an enum value, not a schema change plus four systems
    /// that forgot to read the new field.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct EquippedItem : IBufferElementData
    {
        public EquipmentSlot Slot;

        /// <summary>The equipped item, or Empty.</summary>
        public int ItemId;

        /// <summary>
        /// No item. ItemDefinition.ComputeId never returns this for a real name,
        /// so zero is unambiguous rather than merely unlikely.
        /// </summary>
        public const int Empty = 0;

        public bool HasItem => ItemId != Empty;
    }

    /// <summary>
    /// One item a player is carrying.
    ///
    /// This is the buffer that replaced the flat CollectedItem list from the
    /// loot step, exactly where that was expected to give way. It carries an id
    /// and the state that belongs to the instance — everything else about the
    /// item (name, slot, stats, affixes) is looked up in ItemDatabase, so an
    /// item is described in one place.
    ///
    /// Rolled affixes will need an instance id here: today two copies of the
    /// same item are genuinely interchangeable, because the affixes come from
    /// the definition rather than from a roll.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct InventoryItem : IBufferElementData
    {
        public int ItemId;
        public ItemRarity Rarity;
        public ItemRiskState RiskState;
    }

    /// <summary>
    /// A client asking to equip or unequip something.
    ///
    /// The same request/result split as interaction, and for the same reason:
    /// the UI says what it wants, the host checks that the item is actually in
    /// the inventory and that it fits the slot it claims. The request even
    /// carries a slot for equipping, and the host ignores it in favour of the
    /// slot the item definition says it belongs in.
    ///
    /// The queue lives on the player entity rather than in one global list: a
    /// player can only ever equip their own items, so a shared queue would be a
    /// place for one player's UI to reach another player's gear.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct EquipRequest : IBufferElementData
    {
        public int ItemId;

        /// <summary>Which slot to clear. Only read when Equip is false.</summary>
        public EquipmentSlot Slot;

        public bool Equip;
    }
}
