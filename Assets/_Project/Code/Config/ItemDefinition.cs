using System;
using UnityEngine;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;

namespace TogetherWeFall.Config
{
    /// <summary>One stat an item contributes before any modifier is applied.</summary>
    [Serializable]
    public sealed class ItemStatValue
    {
        [SerializeField] private StatKind _stat;
        [SerializeField] private float _value;

        public ItemStatValue()
        {
        }

        public ItemStatValue(StatKind stat, float value)
        {
            _stat = stat;
            _value = value;
        }

        public StatKind Stat => _stat;
        public float Value => _value;
    }

    /// <summary>
    /// One modifier an item grants, in the PoE sense: a stat, how it applies,
    /// and by how much.
    ///
    /// Authored on the definition rather than rolled per drop. Rolled affixes
    /// are the obvious next step and the seam for them is the item instance —
    /// today two copies of an item are identical, which is why the inventory can
    /// refer to items by definition id alone.
    /// </summary>
    [Serializable]
    public sealed class ItemAffix
    {
        [SerializeField] private StatKind _stat;
        [SerializeField] private ModifierKind _kind = ModifierKind.Increased;

        [Tooltip("Flat adds this much. Increased is a percentage: 15 means +15%, " +
                 "and it stacks additively with every other increase to the same " +
                 "stat before being applied once.")]
        [SerializeField] private float _value;

        public ItemAffix()
        {
        }

        public ItemAffix(StatKind stat, ModifierKind kind, float value)
        {
            _stat = stat;
            _kind = kind;
            _value = value;
        }

        public StatKind Stat => _stat;
        public ModifierKind Kind => _kind;
        public float Value => _value;
    }

    /// <summary>
    /// One kind of item. A ScriptableObject, consistently with LootTable and the
    /// SkillDefinition that will follow: item data is authored, not coded.
    ///
    /// The asset itself never reaches a system — the baker copies what matters
    /// into the item database blob. That is deliberate: a job cannot touch a
    /// managed asset, and a host must be able to work from data it owns.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ItemDefinition",
        menuName = "Together We Fall/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("Shown to the player. Falls back to the asset name when empty.")]
        [SerializeField] private string _displayName;

        [SerializeField] private ItemRarity _rarity = ItemRarity.Common;
        [SerializeField] private EquipmentSlot _slot = EquipmentSlot.Weapon;

        [Header("Base stats")]
        [Tooltip("Added flat to the character before modifiers. What the item is " +
                 "worth with no affixes on it at all.")]
        [SerializeField] private ItemStatValue[] _baseStats = Array.Empty<ItemStatValue>();

        [Header("Affixes")]
        [SerializeField] private ItemAffix[] _affixes = Array.Empty<ItemAffix>();

        [Header("Inventory footprint")]
        [Tooltip("How many cells wide the item is in the bag. A sword is 1x3, " +
                 "a breastplate 2x3, a ring 1x1.")]
        [SerializeField, Range(1, 4)] private int _gridWidth = 1;

        [SerializeField, Range(1, 4)] private int _gridHeight = 1;

        [Tooltip("Whether the player may turn this item on its side to make it " +
                 "fit. Off by default: rotation is a property of a specific " +
                 "item, like a two-handed weapon in PoE, not a default that " +
                 "every item quietly inherits.")]
        [SerializeField] private bool _canRotate;

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;

        public ItemRarity Rarity => _rarity;
        public EquipmentSlot Slot => _slot;

        public ItemStatValue[] BaseStats => _baseStats;
        public ItemAffix[] Affixes => _affixes;

        /// <summary>Clamped, because a zero-cell item would fit anywhere and nowhere.</summary>
        public int GridWidth => Mathf.Max(1, _gridWidth);
        public int GridHeight => Mathf.Max(1, _gridHeight);

        /// <summary>
        /// Square items are never rotatable whatever the asset says: turning a
        /// 1x1 or a 2x2 changes nothing, and letting the flag be true there
        /// would mean the UI offers a rotation that does not move anything.
        /// </summary>
        public bool CanRotate => _canRotate && GridWidth != GridHeight;

        /// <summary>The id systems, save data and the network refer to this item by.</summary>
        public int ItemId => ComputeId(DisplayName);

        /// <summary>
        /// FNV-1a over the name.
        ///
        /// Not string.GetHashCode: that is randomised per process in modern
        /// .NET, so two players — or the same player across two launches — would
        /// hash the same item to different ids, and every saved or replicated
        /// reference would rot. This one is fixed by the algorithm.
        ///
        /// Zero is never returned, so the equipment slots can use it to mean
        /// "empty" without that being a collision waiting to happen.
        /// </summary>
        public static int ComputeId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            unchecked
            {
                uint hash = 2166136261u;

                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                int id = (int)hash;
                return id != 0 ? id : 1;
            }
        }
    }
}
