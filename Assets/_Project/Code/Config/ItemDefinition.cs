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
        [SerializeField] private EquipmentSlot _slot = EquipmentSlot.MainHand;

        [Tooltip("Needs both hands. Equipping it clears the off hand back into " +
                 "the bag and keeps it empty for as long as this is worn.")]
        [SerializeField] private bool _isTwoHanded;

        [Header("Base stats")]
        [Tooltip("Added flat to the character before modifiers. What the item is " +
                 "worth with no affixes on it at all.")]
        [SerializeField] private ItemStatValue[] _baseStats = Array.Empty<ItemStatValue>();

        [Header("Affixes")]
        [SerializeField] private ItemAffix[] _affixes = Array.Empty<ItemAffix>();

        [Header("Skill gem")]
        [Tooltip("What this item is when socketed. None for ordinary gear.")]
        [SerializeField] private GemKind _gemKind = GemKind.None;

        [Tooltip("The skill an Active gem casts.")]
        [SerializeField] private SkillDefinition _gemSkill;

        [Tooltip("The modifier a Support gem applies to every active linked " +
                 "to it.")]
        [SerializeField] private SkillModifier _gemSupport;

        [Header("Sockets")]
        [Tooltip("Holes in this piece of gear. Fixed per item — rolling them " +
                 "per drop is a feature of its own on top of this one.")]
        [SerializeField, Range(0, 6)] private int _socketCount;

        [Tooltip("Which link group each socket belongs to, one entry per " +
                 "socket. Supports affect the actives sharing their group: " +
                 "[0,0,1,1,1] is a pair and a triple. Left short, every socket " +
                 "falls into group zero, which links them all.")]
        [SerializeField] private int[] _linkGroups = Array.Empty<int>();

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

        /// <summary>
        /// Only a main-hand weapon can need both hands. Anywhere else the flag
        /// has nothing to block, and letting it be true there would mean an
        /// amulet that quietly empties the off hand.
        /// </summary>
        public bool IsTwoHanded => _isTwoHanded && _slot == EquipmentSlot.MainHand;

        /// <summary>
        /// What kind of gem this is. Named GemType rather than GemKind so the
        /// enum stays reachable by name inside this class.
        /// </summary>
        public GemKind GemType => _gemKind;

        /// <summary>
        /// The skill an Active gem casts, as a stable id rather than an index.
        ///
        /// An id because the item database and the skill database are baked by
        /// two different authoring objects that share no ordering. The same
        /// reason item ids are FNV hashes of a name and not array positions —
        /// and the same guarantee: it survives a reorder, a re-bake and two
        /// separate processes.
        /// </summary>
        public int GemSkillId =>
            _gemKind == GemKind.Active && _gemSkill != null
                ? ComputeId(_gemSkill.DisplayName)
                : 0;

        /// <summary>The modifier a Support gem carries, or null.</summary>
        public SkillModifier GemSupportModifier =>
            _gemKind == GemKind.Support ? _gemSupport : null;

        public int SocketCount => Mathf.Clamp(_socketCount, 0, 6);

        /// <summary>
        /// Which link group a socket belongs to.
        ///
        /// Zero when the layout says nothing, which puts every socket in one
        /// group and links them all. That is the friendly default: an author who
        /// sets a socket count and forgets the groups gets a fully linked item
        /// rather than one where nothing supports anything.
        /// </summary>
        public int LinkGroupOf(int socketIndex)
        {
            if (_linkGroups == null || socketIndex < 0 || socketIndex >= _linkGroups.Length)
                return 0;

            return Mathf.Max(0, _linkGroups[socketIndex]);
        }

        /// <summary>
        /// Every slot this item may go in, as a bitmask.
        ///
        /// Derived from the one authored slot rather than authored as a list:
        /// the only item that fits in more than one place is a ring, and that
        /// rule belongs in one method instead of in every ring asset.
        /// </summary>
        public ushort AllowedSlots => EquipmentSlots.AllowedMask(_slot);

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
