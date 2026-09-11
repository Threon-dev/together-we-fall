using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;

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
        /// What this item is when it goes in a hole rather than on a body.
        ///
        /// None for everything that is not a gem, which is almost everything.
        /// The two fields below are only meaningful for the matching kind, and
        /// the baker leaves them at zero otherwise rather than at something that
        /// looks usable.
        /// </summary>
        public GemKind GemKind;

        /// <summary>The skill an Active gem casts, by stable id. Zero for the rest.</summary>
        public int GemSkillId;

        /// <summary>What a Support gem does. Flat, so it may be copied freely.</summary>
        public SkillModifierBlob GemSupport;

        /// <summary>
        /// The skill this gear comes with, by stable id. Zero for everything
        /// that is not a weapon.
        ///
        /// It answers "what does wearing this let me do", which used to have no
        /// answer at all: a sword with no gem in it was a sword you could not
        /// swing. Welded into socket 0 when the item is handed out, so the rest
        /// of the pipeline still finds it exactly where it finds every other
        /// skill — in a hole.
        /// </summary>
        public int InnateSkillId;

        /// <summary>How many holes this gear has.</summary>
        public int SocketCount;

        /// <summary>
        /// The link group of each socket, in order.
        ///
        /// A FixedList rather than a second BlobArray: six is the ceiling, the
        /// values are bytes, and a blob array would be another pointer to get
        /// wrong in a struct that already must never be copied.
        /// </summary>
        public FixedList32Bytes<byte> LinkGroups;

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

        /// <summary>
        /// The combat rule this item inverts while worn, or None.
        ///
        /// Beside the affixes rather than among them because it is not a number:
        /// affixes are summed and a keystone is chosen, and putting a chosen
        /// thing in a summed list is how "two rings, both keystones" turns into
        /// a question with no answer.
        /// </summary>
        public KeystoneEffect Keystone;

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
