using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Equipment
{
    /// <summary>One modifier an item grants.</summary>
    public struct AffixBlob
    {
        public StatKind Stat;
        public ModifierKind Kind;
        public float Value;
    }

    /// <summary>
    /// One item definition, as the simulation sees it.
    ///
    /// NEVER copy this by value. It holds a BlobArray, whose internal pointer is
    /// an offset relative to its own address — copy the struct and the affix
    /// array points at whatever happens to sit that far from the copy. Always
    /// take it as `ref ItemBlob`.
    /// </summary>
    public struct ItemBlob
    {
        public int ItemId;
        public ItemRarity Rarity;

        /// <summary>The slot the item was authored for. What it is, not where it may go.</summary>
        public EquipmentSlot Slot;

        /// <summary>
        /// Every slot it may actually go in, as a bitmask.
        ///
        /// Separate from Slot because they answer different questions and only
        /// coincide for nine kinds of item out of ten. A ring is authored as
        /// Ring1 and accepted by both ring slots, and the host checks the mask
        /// rather than comparing to Slot — which is what lets a client name a
        /// slot without being able to name any slot it likes.
        /// </summary>
        public ushort AllowedSlots;

        /// <summary>Needs both hands: blocks the off hand while worn.</summary>
        public bool IsTwoHanded;

        /// <summary>
        /// How many cells the item covers in a container, unrotated. Rotating
        /// swaps the two, and nothing else about the item changes — which is
        /// exactly why non-rectangular shapes are deferred rather than nearly
        /// free.
        /// </summary>
        public int GridWidth;
        public int GridHeight;

        /// <summary>Whether the player may turn it on its side.</summary>
        public bool CanRotate;

        /// <summary>Flat values the item contributes before any modifier.</summary>
        public StatBlock BaseStats;

        public BlobArray<AffixBlob> Affixes;

        public FixedString64Bytes Name;
    }

    /// <summary>
    /// Every item in the game, sorted by id.
    ///
    /// Sorted so a lookup is a binary search rather than a scan. That matters
    /// less for the eight items in the prototype than for the rule it sets: an
    /// item is looked up by id from one place, and nothing else caches a copy of
    /// what an item is.
    /// </summary>
    public struct ItemDatabaseBlob
    {
        public BlobArray<ItemBlob> Items;
    }

    /// <summary>
    /// Handle to the item database.
    ///
    /// The lookup lives on this component rather than on the blob struct on
    /// purpose: a method on the blob would take `this` by value, and copying a
    /// struct that contains a BlobArray quietly breaks it. This one only ever
    /// copies the reference.
    /// </summary>
    public struct ItemDatabase : IComponentData
    {
        public BlobAssetReference<ItemDatabaseBlob> Value;

        /// <summary>Index of an item, or -1 if the database has never heard of it.</summary>
        public int IndexOf(int itemId)
        {
            if (!Value.IsCreated)
                return -1;

            ref ItemDatabaseBlob blob = ref Value.Value;

            int low = 0;
            int high = blob.Items.Length - 1;

            while (low <= high)
            {
                int middle = (low + high) / 2;
                int candidate = blob.Items[middle].ItemId;

                if (candidate == itemId)
                    return middle;

                if (candidate < itemId)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return -1;
        }
    }
}
