using System;
using UnityEngine;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// One step of a set's reward, and what wearing that many pieces is worth.
    ///
    /// All three rewards are optional and independent, which is the whole point
    /// of the shape: two pieces can be plain numbers, four can add a support
    /// that applies to everything the wearer casts, six can break a rule. None
    /// of the three is a new mechanism — they are the affix list, the support
    /// modifier and the keystone that gear already carries, read off a set
    /// instead of off one item.
    /// </summary>
    [Serializable]
    public sealed class SetBonusThreshold
    {
        [Tooltip("How many pieces of the set must be worn. 2, 4, 6.")]
        [SerializeField, Min(1)] private int _requiredPieceCount = 2;

        [Tooltip("Stats this step grants, in exactly the form an item affix " +
                 "takes: they are summed into the character sheet by the same " +
                 "fold, flat and increased kept apart.")]
        [SerializeField] private ItemAffix[] _bonuses = Array.Empty<ItemAffix>();

        [Tooltip("A support modifier this step applies to EVERY skill the " +
                 "wearer casts, rather than to one link group. Empty for a " +
                 "step that is only numbers.")]
        [SerializeField] private SkillModifier _bonusSkillModifier;

        [Tooltip("A rule this step breaks, exactly as a unique item would. " +
                 "None for almost every step — and a set keystone loses to a " +
                 "worn one, the same first-wins rule two rings follow.")]
        [SerializeField] private KeystoneEffect _bonusKeystone = KeystoneEffect.None;

        public int RequiredPieceCount => Mathf.Max(1, _requiredPieceCount);
        public ItemAffix[] Bonuses => _bonuses ?? Array.Empty<ItemAffix>();
        public SkillModifier BonusSkillModifier => _bonusSkillModifier;
        public KeystoneEffect BonusKeystone => _bonusKeystone;
    }

    /// <summary>
    /// A family of items and what wearing several of them at once is worth.
    ///
    /// Membership lives here rather than as a set id on every ItemDefinition,
    /// and that is the one real decision in this asset: a set is edited as a
    /// whole — "which pieces, and what do they give" is one thought — and the
    /// alternative is the same fact written down in six files that can disagree.
    /// An item belongs to at most one set; the second set that names it is a
    /// bake-time warning rather than a silently doubled bonus.
    ///
    /// Like every other content asset here, it never reaches a system. The
    /// baker copies what matters into a blob, because a job cannot read a
    /// managed asset and a host must work from data it owns.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ItemSetDefinition",
        menuName = "Together We Fall/Item Set Definition")]
    public sealed class ItemSetDefinition : ScriptableObject
    {
        [Tooltip("Stable id for saves and the network. Falls back to the asset " +
                 "name when empty.")]
        [SerializeField] private string _setId;

        [Tooltip("Shown to the player. Falls back to the id when empty.")]
        [SerializeField] private string _setName;

        [Tooltip("Every item in the set. This list IS the membership — an item " +
                 "carries no set field of its own.")]
        [SerializeField] private ItemDefinition[] _members = Array.Empty<ItemDefinition>();

        [Tooltip("What each piece count is worth. Only the highest step the " +
                 "wearer has reached is in force; the lower ones are replaced, " +
                 "not stacked.")]
        [SerializeField] private SetBonusThreshold[] _thresholds =
            Array.Empty<SetBonusThreshold>();

        public string SetId => string.IsNullOrWhiteSpace(_setId) ? name : _setId;

        public string SetName =>
            string.IsNullOrWhiteSpace(_setName) ? SetId : _setName;

        public ItemDefinition[] Members => _members ?? Array.Empty<ItemDefinition>();

        public SetBonusThreshold[] Thresholds =>
            _thresholds ?? Array.Empty<SetBonusThreshold>();
    }
}
