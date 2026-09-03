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
            new ItemStatValue(StatKind.MoveSpeed, 6f)
        };

        public ItemStatValue[] BaseStats => _baseStats;
    }
}
