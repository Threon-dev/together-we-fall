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
                 "percentages; Added Chains, Fork, Multicast and Pierce are " +
                 "counts; Explode On Kill is the blast radius; Culling Strike " +
                 "is the life percentage below which the target simply dies; " +
                 "Mana On Kill is flat mana per body.")]
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

        [Header("Trigger (Cast on X)")]
        [Tooltip("Only Trigger On Condition uses these. The support casts the " +
                 "active gems linked beside it — it names no skill of its own — " +
                 "and while it is in the group those actives stop answering the " +
                 "hotkey.")]
        [SerializeField] private TriggerConditionType _triggerCondition =
            TriggerConditionType.OnKill;

        [Tooltip("Seconds between firings. Not optional: without it, 'cast on " +
                 "kill' during a wave is a cast every frame.")]
        [SerializeField, Min(0.05f)] private float _triggerCooldown = 3f;

        [Tooltip("How often the condition actually pays out. One is certain, " +
                 "and certain is the dull version.")]
        [SerializeField, Range(0.01f, 1f)] private float _procChance = 0.35f;

        [Header("Condition")]
        [Tooltip("What must be true for this support to count at all. None is " +
                 "every support authored before conditions existed, and is the " +
                 "default for a reason.")]
        [SerializeField] private ModifierConditionType _condition = ModifierConditionType.None;

        [Tooltip("Which element the condition is about.")]
        [SerializeField] private DamageType _requiredElement = DamageType.Fire;

        [Tooltip("Which status the condition is about, for Target Has Status " +
                 "Effect. Ignored by every other condition.")]
        [SerializeField] private StatusEffectType _requiredStatus = StatusEffectType.Stun;

        [Tooltip("The fraction the condition compares against: life remaining " +
                 "for Target Low Health, Target High Health and On Low Health. " +
                 "0.35 is a third.")]
        [SerializeField, Range(0f, 1f)] private float _threshold = 0.35f;

        [Tooltip("How many enemies have to be standing together for Target " +
                 "Crowded. Its own field rather than the value above, which is " +
                 "what the support DOES.")]
        [SerializeField, Range(1, 12)] private int _requiredCount = 3;

        [Header("Status Override")]
        [Tooltip("Only Status Override uses this: the status the supported " +
                 "skill applies INSTEAD of its own. It must also be on the " +
                 "reaction table, like any status a skill names.")]
        [SerializeField] private StatusEffectDefinition _appliedStatus;

        public SkillModifierKind Kind => _kind;
        public float Value => _value;
        public float SecondaryValue => _secondaryValue;
        public DamageType ConvertTo => _convertTo;
        public SkillDefinition TriggeredSkill => _triggeredSkill;

        public TriggerConditionType TriggerCondition => _triggerCondition;

        /// <summary>
        /// Clamped away from zero, because a trigger with no cooldown is not a
        /// fast trigger, it is a frame spent casting. The asset can say anything;
        /// what the blob carries is a number the pipeline survives.
        /// </summary>
        public float TriggerCooldown => Mathf.Max(0.05f, _triggerCooldown);

        public float ProcChance => Mathf.Clamp(_procChance, 0.01f, 1f);

        public ModifierConditionType Condition => _condition;
        public DamageType RequiredElement => _requiredElement;
        public StatusEffectType RequiredStatus => _requiredStatus;
        public float Threshold => Mathf.Clamp01(_threshold);

        /// <summary>Clamped to a byte, which is what the blob carries it in.</summary>
        public byte RequiredCount => (byte)Mathf.Clamp(_requiredCount, 1, 255);

        /// <summary>The asset, so the baker can depend on it.</summary>
        public StatusEffectDefinition AppliedStatus => _appliedStatus;

        /// <summary>
        /// What this support makes the skill apply, as the name two databases
        /// share. Same shape as SkillDefinition.AppliedStatusType, and for the
        /// same reason: authoring picks an asset, the runtime compares a value.
        /// </summary>
        public StatusEffectType AppliedStatusType =>
            _kind == SkillModifierKind.StatusOverride && _appliedStatus != null
                ? _appliedStatus.Type
                : StatusEffectType.None;
    }
}
