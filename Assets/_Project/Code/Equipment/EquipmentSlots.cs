namespace TogetherWeFall.Equipment
{
    /// <summary>
    /// The rules about which slots an item may go in.
    ///
    /// A struct of static methods rather than a system, the same shape as
    /// GridFit and for the same reason: the baker needs these answers to build
    /// the item database, the host needs them to check a request, and the UI
    /// needs them to grey out a slot the dragged item can never enter. Three
    /// callers, one set of rules.
    ///
    /// The set of slots an item accepts is baked into the item as a bitmask,
    /// not stored on the asset as a list. Authoring one slot and deriving the
    /// rest means a new ring cannot be created that forgets to mention Ring2,
    /// and the day a third ring slot exists it is one line here rather than an
    /// edit to every ring in the game.
    /// </summary>
    public struct EquipmentSlots
    {
        /// <summary>How many slots a character has. The size of the buffer.</summary>
        public const int Count = (int)EquipmentSlot.Belt + 1;

        /// <summary>No slot at all. A mask, so zero genuinely means nothing fits.</summary>
        public const ushort None = 0;

        public static ushort Bit(EquipmentSlot slot) => (ushort)(1 << (int)slot);

        /// <summary>
        /// Every slot an item authored for this one may actually go in.
        ///
        /// Exactly the slot itself, except for rings: a ring authored as Ring1
        /// is equally at home in Ring2, and which of the two it ends up in is
        /// the player's choice rather than something derivable from the item.
        /// That case is the entire reason a request carries a slot at all.
        /// </summary>
        public static ushort AllowedMask(EquipmentSlot canonical)
        {
            if (IsRing(canonical))
                return (ushort)(Bit(EquipmentSlot.Ring1) | Bit(EquipmentSlot.Ring2));

            return Bit(canonical);
        }

        public static bool Accepts(ushort mask, EquipmentSlot slot) => (mask & Bit(slot)) != 0;

        public static bool IsRing(EquipmentSlot slot) =>
            slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2;

        /// <summary>
        /// The lowest slot in a mask, or Count if it is empty.
        ///
        /// Used only as the last resort when nothing else picks: an Auto equip
        /// prefers an empty allowed slot, and falls back to this when every one
        /// of them is occupied.
        /// </summary>
        public static int FirstAllowed(ushort mask)
        {
            for (int slot = 0; slot < Count; slot++)
            {
                if (Accepts(mask, (EquipmentSlot)slot))
                    return slot;
            }

            return Count;
        }

        /// <summary>
        /// The first EMPTY slot in a mask, or false when every one of them is
        /// taken.
        ///
        /// The half of an Auto equip that never swaps: the starter kit hands out
        /// items nobody owns yet and has nowhere to put a displaced one, so a
        /// full slot means the item goes in the bag instead. The equip system's
        /// own Auto keeps its fallback to the lowest allowed slot, because a
        /// player double-clicking a ring is asking to replace something.
        /// </summary>
        public static bool TryFirstFree(
            Unity.Entities.DynamicBuffer<EquippedItem> slots, ushort mask, out int slot)
        {
            for (slot = 0; slot < slots.Length; slot++)
            {
                if (Accepts(mask, (EquipmentSlot)slot) && !slots[slot].HasItem)
                    return true;
            }

            slot = Count;
            return false;
        }

        /// <summary>
        /// What the character is already wearing that this item would replace,
        /// by id, or zero for nothing.
        ///
        /// Here rather than in a panel because both panels ask it — the bag and
        /// the vendor's shelf are the same question asked in two rooms — and
        /// because it is the same allowed mask an equip request is checked
        /// against, so the item named is one this item could genuinely replace.
        /// It decides nothing: which slot an equip actually lands in stays the
        /// host's answer.
        ///
        /// ponytail: the FIRST occupied slot the item is allowed in, which for a
        /// ring means whichever hand comes first. Naming both would be two
        /// columns of numbers for the one item in the game that has two rivals,
        /// and the answer to "and the other ring?" is to look at the other ring.
        /// Return a pair the day a second item type accepts two slots.
        /// </summary>
        public static int WornRivalOf(
            Unity.Entities.DynamicBuffer<EquippedItem> slots, ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return 0;

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            ushort mask = item.AllowedSlots;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].HasItem && Accepts(mask, slots[i].Slot))
                    return slots[i].ItemId;
            }

            return 0;
        }

        /// <summary>
        /// Whether the off hand is unusable because the main hand is holding
        /// something that needs both.
        ///
        /// Derived from what is worn rather than stored as a flag on the
        /// character. A stored one would be a second copy of a fact the slots
        /// already contain, with all the usual consequences of the two
        /// disagreeing — the off hand staying locked forever after a two-hander
        /// is dropped, most likely, because nothing remembered to clear it.
        /// </summary>
        public static bool IsOffHandBlocked(
            Unity.Entities.DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            int mainHand = (int)EquipmentSlot.MainHand;

            if (mainHand >= slots.Length || !slots[mainHand].HasItem)
                return false;

            int index = items.IndexOf(slots[mainHand].ItemId);
            if (index < 0)
                return false;

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful.
            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.IsTwoHanded;
        }
    }
}
