using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Loot
{
    /// <summary>
    /// One item a table can produce.
    ///
    /// An index into the item database rather than an id and a name of its own.
    /// The two blobs are baked together, so an entry cannot point at an item the
    /// stats code has never heard of — and an item is described in exactly one
    /// place, which is the difference between a rename and a rename plus a hunt.
    /// </summary>
    public struct LootEntryBlob
    {
        public int ItemIndex;

        /// <summary>Relative chance against the other entries of the same rarity.</summary>
        public float Weight;
    }

    /// <summary>
    /// One loot table.
    ///
    /// Rolling happens in two stages — first a rarity, then an item of that
    /// rarity — because that is what makes rarity weights mean what players
    /// think they mean. Weighting items directly would let a table with thirty
    /// common items and one legendary drown the legendary regardless of its
    /// stated chance.
    ///
    /// Entries are stored sorted by rarity and RarityRanges indexes into them,
    /// so the second stage is a slice rather than a search.
    /// </summary>
    public struct LootTableBlob
    {
        public int MinRolls;
        public int MaxRolls;

        /// <summary>One weight per ItemRarity, in enum order.</summary>
        public BlobArray<float> RarityWeights;

        /// <summary>All entries, grouped by rarity.</summary>
        public BlobArray<LootEntryBlob> Entries;

        /// <summary>Start index and count into Entries, per rarity.</summary>
        public BlobArray<int2> RarityRanges;
    }

    /// <summary>
    /// Every table in the game, addressed by index.
    ///
    /// A blob rather than a managed asset reference for the usual reason: a job
    /// cannot touch a ScriptableObject, and a host must be able to roll loot
    /// from numbers it owns rather than from a client's assets.
    /// </summary>
    public struct LootDatabaseBlob
    {
        public BlobArray<LootTableBlob> Tables;
    }
}
