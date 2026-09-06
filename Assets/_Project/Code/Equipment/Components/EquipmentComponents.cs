using Unity.Entities;

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

        /// <summary>
        /// The exact item worn here, or Entity.Null.
        ///
        /// Both this and the id, because they answer different questions. The
        /// entity is which instance — the one that has to come back to the bag
        /// when it is taken off, and the one rolled affixes will eventually hang
        /// on. The id is what it is, which is all the stat maths needs, and
        /// keeping it here means PlayerStatsSystem never has to chase a
        /// reference to add up a number.
        /// </summary>
        public Entity Item;

        /// <summary>What the equipped item is, or Empty.</summary>
        public int ItemId;

        /// <summary>
        /// No item. ItemDefinition.ComputeId never returns this for a real name,
        /// so zero is unambiguous rather than merely unlikely.
        /// </summary>
        public const int Empty = 0;

        public bool HasItem => ItemId != Empty && Item != Entity.Null;
    }

    // What a player is carrying used to be an InventoryItem buffer here. The
    // grid inventory replaced it: a carried item is now the same entity that lay
    // on the floor, holding ItemInstance and ItemGridPlacement, and the bag is a
    // container entity whose cells point at those entities. The instance
    // identity that rolled affixes were always going to need arrived with it.

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
        /// <summary>
        /// Which item to put on. Only read when Equip is true.
        ///
        /// An entity rather than an id, because two copies of the same item are
        /// two different things to a grid: taking one off has to put that one
        /// back, in the space that one came out of.
        /// </summary>
        public Entity Item;

        /// <summary>Which slot to clear. Only read when Equip is false.</summary>
        public EquipmentSlot Slot;

        public bool Equip;
    }
}
