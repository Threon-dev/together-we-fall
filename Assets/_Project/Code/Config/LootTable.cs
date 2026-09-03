using System;
using UnityEngine;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Config
{
    /// <summary>One item a table may produce, and how likely it is among its peers.</summary>
    [Serializable]
    public sealed class LootTableEntry
    {
        [SerializeField] private ItemDefinition _item;

        [Tooltip("Relative chance against the other entries OF THE SAME RARITY. " +
                 "Rarity itself is chosen first, by the weights above.")]
        [SerializeField, Min(0f)] private float _weight = 1f;

        public ItemDefinition Item => _item;
        public float Weight => _weight;
    }

    /// <summary>
    /// What a chest can drop.
    ///
    /// Rolling is two-stage — a rarity first, then an item of that rarity —
    /// because that is the only way rarity weights mean what they appear to
    /// mean. If items were weighted directly, adding thirty common items to a
    /// table would quietly bury the one legendary in it, and the number written
    /// next to "Legendary" would be a lie.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LootTable",
        menuName = "Together We Fall/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        [Header("How many items")]
        [SerializeField, Range(0, 20)] private int _minRolls = 2;
        [SerializeField, Range(0, 20)] private int _maxRolls = 4;

        [Header("Rarity weights")]
        [Tooltip("One weight per rarity, in enum order: Common, Uncommon, Rare, " +
                 "Epic, Legendary, Mythic. Rarities with no items in the table " +
                 "are skipped and their weight redistributed.")]
        [SerializeField]
        private float[] _rarityWeights = { 55f, 25f, 13f, 5f, 1.8f, 0.2f };

        [Header("Items")]
        [SerializeField] private LootTableEntry[] _entries = Array.Empty<LootTableEntry>();

        public int MinRolls => Mathf.Min(_minRolls, _maxRolls);
        public int MaxRolls => Mathf.Max(_minRolls, _maxRolls);

        public float[] RarityWeights => _rarityWeights;
        public LootTableEntry[] Entries => _entries;

        /// <summary>
        /// Weight of one rarity, or zero if the array is shorter than the enum.
        /// Read through this rather than indexing directly: an asset authored
        /// before a rarity was added would otherwise throw during baking.
        /// </summary>
        public float WeightFor(ItemRarity rarity)
        {
            int index = (int)rarity;
            return _rarityWeights != null && index < _rarityWeights.Length
                ? Mathf.Max(0f, _rarityWeights[index])
                : 0f;
        }
    }
}
