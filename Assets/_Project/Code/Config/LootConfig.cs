using UnityEngine;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Chest, drop and interaction tuning.
    ///
    /// The rarity palette lives here rather than on the item prefab because
    /// rarity colour is a property of the game, not of one item: a legendary has
    /// to read as legendary whatever dropped it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LootConfig",
        menuName = "Together We Fall/Loot Config")]
    public sealed class LootConfig : ScriptableObject
    {
        [Header("Chests")]
        [SerializeField, Range(1, 6)] private int _chestsPerTreasureRoom = 2;

        [Tooltip("Index of the table treasure-room chests roll on.")]
        [SerializeField, Min(0)] private int _treasureChestTableId;

        [Tooltip("How long a chest takes to open. Not decoration: this is the " +
                 "window in which a second player can see they lost the race.")]
        [SerializeField, Range(0f, 5f)] private float _chestOpenSeconds = 1.2f;

        [SerializeField, Range(0.5f, 8f)] private float _chestInteractionRadius = 2.5f;

        [Header("Drops")]
        [Tooltip("How far items scatter from the chest they fell out of.")]
        [SerializeField, Range(0.5f, 8f)] private float _dropScatterRadius = 2.5f;

        [SerializeField, Range(0.5f, 6f)] private float _itemPickupRadius = 1.6f;

        [Tooltip("Dropped items created once and reused forever. Also the " +
                 "ceiling on how many can lie on the floor at a time.")]
        [SerializeField, Range(8, 512)] private int _itemPoolSize = 96;

        [Header("Rarity colours")]
        [Tooltip("One colour per rarity, in enum order: Common, Uncommon, Rare, " +
                 "Epic, Legendary, Mythic.")]
        [SerializeField]
        private Color[] _rarityColors =
        {
            new Color(0.78f, 0.78f, 0.78f),
            new Color(0.35f, 0.80f, 0.40f),
            new Color(0.30f, 0.55f, 0.95f),
            new Color(0.65f, 0.35f, 0.90f),
            new Color(0.95f, 0.62f, 0.18f),
            new Color(0.95f, 0.25f, 0.35f)
        };

        public int ChestsPerTreasureRoom => _chestsPerTreasureRoom;
        public int TreasureChestTableId => _treasureChestTableId;
        public float ChestOpenSeconds => _chestOpenSeconds;
        public float ChestInteractionRadius => _chestInteractionRadius;
        public float DropScatterRadius => _dropScatterRadius;
        public float ItemPickupRadius => _itemPickupRadius;
        public int ItemPoolSize => _itemPoolSize;

        /// <summary>
        /// Colour of a rarity, white if the palette is shorter than the enum —
        /// a wrong colour is a better failure than a baker that throws.
        /// </summary>
        public Color ColorFor(ItemRarity rarity)
        {
            int index = (int)rarity;
            return _rarityColors != null && index < _rarityColors.Length
                ? _rarityColors[index]
                : Color.white;
        }
    }
}
