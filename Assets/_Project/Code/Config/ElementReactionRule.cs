using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// One combination: what is already there, what just arrived, and what that
    /// means.
    ///
    /// An asset per combination rather than a switch in a system. That is the
    /// point of the whole shape — adding "cold onto a burning target makes
    /// steam" is a new asset dropped into the table, and no system learns
    /// anything. Nothing in the code knows that fire and lightning are
    /// interesting together; the systems only know how to ask.
    ///
    /// The pair is ORDERED. Fire onto a shocked target and lightning onto a
    /// burning one are different sentences, and an author who means both writes
    /// both — a symmetric rule would quietly author a combination nobody asked
    /// for and make the reverse impossible to tune separately.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ElementReactionRule",
        menuName = "Together We Fall/Element Reaction Rule")]
    public sealed class ElementReactionRule : ScriptableObject
    {
        [Tooltip("The element already on the target — as a status, or carried " +
                 "in by the effect that is landing. Both are the same question.")]
        [SerializeField] private DamageType _existingElement = DamageType.Lightning;

        [Tooltip("The element of the blow landing now.")]
        [SerializeField] private DamageType _incomingElement = DamageType.Fire;

        [SerializeField] private ElementReactionKind _result = ElementReactionKind.BonusDamage;

        [Tooltip("Multiplies the blow for Bonus Damage, and the blast for " +
                 "Explosion. One means no change.")]
        [SerializeField, Min(0f)] private float _damageMultiplier = 1.5f;

        [Tooltip("Only Apply Status uses this: what the pair leaves behind.")]
        [SerializeField] private StatusEffectDefinition _resultingStatus;

        [Tooltip("Only Explosion uses this.")]
        [SerializeField, Min(0.5f)] private float _radius = 3.5f;

        [Tooltip("Whether the element that was already there is spent doing " +
                 "this. On for the classic 'burn the charge off' reactions; off " +
                 "for a status that should keep paying out.")]
        [SerializeField] private bool _consumesExistingStatus = true;

        public DamageType ExistingElement => _existingElement;
        public DamageType IncomingElement => _incomingElement;
        public ElementReactionKind Result => _result;
        public float DamageMultiplier => Mathf.Max(0f, _damageMultiplier);
        public StatusEffectDefinition ResultingStatus => _resultingStatus;
        public float Radius => Mathf.Max(0.5f, _radius);
        public bool ConsumesExistingStatus => _consumesExistingStatus;
    }
}
