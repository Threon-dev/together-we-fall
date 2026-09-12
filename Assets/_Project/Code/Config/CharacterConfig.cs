using UnityEngine;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// What a character is worth wearing nothing.
    ///
    /// One sheet for everybody: in this game two characters differ by what they
    /// carry, not by what they were born as. When classes arrive this becomes a
    /// set of these assets and the baker picks one — the systems that read the
    /// baked singleton do not change.
    ///
    /// What a character STARTS with is no longer here: it moved to
    /// StarterKitConfig, beside the reason — a loadout is a dial somebody turns
    /// twenty times an hour to look at a mechanic, and the sheet is balance
    /// every scene shares.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CharacterConfig",
        menuName = "Together We Fall/Character Config")]
    public sealed class CharacterConfig : ScriptableObject
    {
        [Tooltip("Stats before any equipment. Anything not listed is zero — " +
                 "which for a resistance is the right default, and for damage " +
                 "means a character with an empty weapon slot does nothing.")]
        [SerializeField]
        private ItemStatValue[] _baseStats =
        {
            new ItemStatValue(StatKind.Damage, 10f),
            new ItemStatValue(StatKind.AttackSpeed, 1f),
            new ItemStatValue(StatKind.Armour, 0f),
            new ItemStatValue(StatKind.MaxHealth, 100f),
            new ItemStatValue(StatKind.MoveSpeed, 6f),

            // TUNE, and the pair that decides whether mana is a resource or a
            // formality: a hundred points against a six-mana skill is sixteen
            // casts, and eight a second puts one back. Nothing here is balanced
            // against anything else — see the note on the build library.
            new ItemStatValue(StatKind.MaxMana, 100f),
            new ItemStatValue(StatKind.ManaRegen, 8f)
        };

        [Header("Bag")]
        [Tooltip("How many cells wide the carried bag is. Twelve by five is the " +
                 "Path of Exile bag; it is here rather than in the simulation " +
                 "settings because how much a player may carry is balance, not " +
                 "a performance knob.")]
        [SerializeField, Range(4, 16)] private int _bagWidth = 12;

        [SerializeField, Range(3, 12)] private int _bagHeight = 5;

        public ItemStatValue[] BaseStats => _baseStats;

        public int BagWidth => _bagWidth;
        public int BagHeight => _bagHeight;
    }
}
