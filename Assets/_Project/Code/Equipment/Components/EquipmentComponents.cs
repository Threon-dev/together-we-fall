using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Equipment
{
    /// <summary>
    /// Where an item goes. The index into the equipment buffer, so values may be
    /// appended but never reshuffled.
    ///
    /// Ten of them rather than the original four. The two that carry rules are
    /// the hands — a two-handed weapon in MainHand blocks OffHand — and the two
    /// rings, which are the only case where an item legitimately fits in more
    /// than one place and the player, not the host, decides which.
    /// </summary>
    public enum EquipmentSlot : byte
    {
        MainHand = 0,
        OffHand = 1,
        Helmet = 2,
        Chest = 3,
        Gloves = 4,
        Boots = 5,
        Ring1 = 6,
        Ring2 = 7,
        Amulet = 8,
        Belt = 9
    }

    /// <summary>
    /// One equipment slot on a character. Exactly one element per slot, in enum
    /// order, so the buffer is addressed rather than searched.
    ///
    /// A buffer instead of fields on a component because slots are the thing
    /// most likely to grow, and every one of those should be an enum value, not
    /// a schema change plus four systems that forgot to read the new field. It
    /// grew from four to ten without a single system noticing.
    /// </summary>
    [InternalBufferCapacity(10)]
    public struct EquippedItem : IBufferElementData
    {
        [GhostField]
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
        [GhostField]
        public Entity Item;

        /// <summary>What the equipped item is, or Empty.</summary>
        [GhostField]
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

    /// <summary>What an equipment request is asking for.</summary>
    public enum EquipRequestKind : byte
    {
        /// <summary>Put this item in this exact slot. The drag-onto-a-slot case.</summary>
        ToSlot = 0,

        /// <summary>
        /// Put this item wherever it belongs. The double-click case, where the
        /// player named an item and no slot at all.
        /// </summary>
        Auto = 1,

        /// <summary>Take whatever is in this slot off, back into the bag.</summary>
        Unequip = 2,

        /// <summary>
        /// Move what is in one slot to another. Ring1 to Ring2, and nothing
        /// else today — but it is the shape of the operation, not the pair,
        /// that makes it worth its own kind: it never touches the bag, so it
        /// works with no free space at all.
        /// </summary>
        SwapSlots = 3
    }

    /// <summary>
    /// A client asking to change what a character is wearing.
    ///
    /// The same request/result split as interaction, and for the same reason:
    /// the UI says what it wants and the host checks it. What changed with ten
    /// slots is that the host can no longer derive the target from the item
    /// alone — a ring fits in two places and only the player knows which — so
    /// the slot travels with the request and is CHECKED rather than ignored.
    /// The guarantee is the same one as before: an item goes only where its
    /// definition allows, so a two-handed sword still cannot be worn as a hat.
    ///
    /// The queue lives on the player entity rather than in one global list: a
    /// player can only ever equip their own items, so a shared queue would be a
    /// place for one player's UI to reach another player's gear.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct EquipRequest : IBufferElementData
    {
        public EquipRequestKind Kind;

        /// <summary>
        /// Which item to put on. Read for ToSlot and Auto.
        ///
        /// An entity rather than an id, because two copies of the same item are
        /// two different things to a grid: taking one off has to put that one
        /// back, in the space that one came out of.
        /// </summary>
        public Entity Item;

        /// <summary>
        /// The target slot for ToSlot, the source slot for Unequip, and the
        /// slot being dragged from for SwapSlots.
        /// </summary>
        public EquipmentSlot Slot;

        /// <summary>Where a SwapSlots is dragging to.</summary>
        public EquipmentSlot OtherSlot;

        /// <summary>
        /// Where in the bag an unequipped item should land.
        ///
        /// Honoured only when UseTarget is set and the square is actually free;
        /// otherwise the host finds the first place it fits. A player who drags
        /// a helmet onto a particular square meant that square, and a player who
        /// drops it vaguely on the bag did not.
        /// </summary>
        public bool UseTarget;
        public int TargetX;
        public int TargetY;
        public bool Rotated;
    }

    /// <summary>How an equipment request turned out.</summary>
    public enum EquipStatus : byte
    {
        Equipped = 0,
        Unequipped = 1,
        Swapped = 2,
        RejectedNotCarried = 3,
        RejectedUnknownItem = 4,
        RejectedWrongSlot = 5,
        RejectedOffHandBlocked = 6,
        RejectedBagFull = 7,
        RejectedEmptySlot = 8,
        RejectedNoBag = 9
    }

    /// <summary>
    /// What the host did about an equipment request.
    ///
    /// Written even when the answer is no, and for the same reason the placement
    /// results are: a refusal leaves the world untouched, so without this the
    /// player would watch an item spring back to where it was and be told
    /// nothing. "Your bag is full" is the difference between a bug and a rule.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct EquipResult : IBufferElementData
    {
        public Entity Item;
        public EquipmentSlot Slot;
        public EquipStatus Status;

        public bool Succeeded => Status <= EquipStatus.Swapped;
    }
}
