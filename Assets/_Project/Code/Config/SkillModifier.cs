using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// A support socketed into a skill — the PoE support gem, as an asset.
    ///
    /// One asset per support rather than a list of numbers on the skill, so the
    /// same "Chain" can be dropped into three different skills and mean the same
    /// thing in all of them. That is the entire point of the model: supports are
    /// authored once and combine, rather than every skill spelling out its own
    /// variants.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SkillModifier",
        menuName = "Together We Fall/Skill Modifier")]
    public sealed class SkillModifier : ScriptableObject
    {
        [SerializeField] private SkillModifierKind _kind = SkillModifierKind.IncreasedDamage;

        [Tooltip("What the number means depends on the kind. Increases are " +
                 "percentages; Added Chains, Fork and Multicast are counts; " +
                 "Explode On Kill is the blast radius.")]
        [SerializeField] private float _value = 25f;

        [Tooltip("Only Explode On Kill uses this: the blast damage as a " +
                 "percentage of the skill damage.")]
        [SerializeField] private float _secondaryValue = 60f;

        [Tooltip("Only Elemental Conversion uses this.")]
        [SerializeField] private DamageType _convertTo = DamageType.Fire;

        [Tooltip("Only Trigger On Hit uses this: the skill cast wherever the " +
                 "supported skill lands. It must also be in the skill database " +
                 "list, or there is no index to refer to it by. Value is then " +
                 "read as the percentage of that skill's own damage.")]
        [SerializeField] private SkillDefinition _triggeredSkill;

        public SkillModifierKind Kind => _kind;
        public float Value => _value;
        public float SecondaryValue => _secondaryValue;
        public DamageType ConvertTo => _convertTo;
        public SkillDefinition TriggeredSkill => _triggeredSkill;
    }
}
