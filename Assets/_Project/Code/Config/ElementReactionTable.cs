using System;
using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Every reaction rule, and what each element leaves behind on its own.
    ///
    /// The same shape as LootTable: one asset that names the pieces, baked into
    /// a blob that the systems actually read. It exists so that there is one
    /// place to look for the answer to "what combinations does this game have",
    /// rather than a folder of rule assets that may or may not all be wired in.
    ///
    /// The default statuses are here rather than on the elements because an
    /// element is an enum value and has nowhere to hold anything. An element
    /// with no default status is perfectly ordinary — physical marks nothing,
    /// and neither would a game that wanted cold to be inert.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ElementReactionTable",
        menuName = "Together We Fall/Element Reaction Table")]
    public sealed class ElementReactionTable : ScriptableObject
    {
        [Tooltip("Every status in the game, including the ones no element " +
                 "leaves and no reaction produces — the control and debuff ones " +
                 "a skill applies by name. A status missing from all three " +
                 "lists below simply does not exist at runtime.")]
        [SerializeField] private StatusEffectDefinition[] _statuses =
            Array.Empty<StatusEffectDefinition>();

        [Tooltip("What a plain hit of each element leaves on its target. One " +
                 "entry per element that marks; the rest mark nothing.")]
        [SerializeField] private StatusEffectDefinition[] _defaultStatuses =
            Array.Empty<StatusEffectDefinition>();

        [Tooltip("Every combination. Two rules for the same ordered pair is an " +
                 "authoring mistake and is reported at bake time.")]
        [SerializeField] private ElementReactionRule[] _rules = Array.Empty<ElementReactionRule>();

        public StatusEffectDefinition[] Statuses => _statuses;
        public StatusEffectDefinition[] DefaultStatuses => _defaultStatuses;
        public ElementReactionRule[] Rules => _rules;
    }
}
