namespace TogetherWeFall.Skills
{
    /// <summary>
    /// When a support actually does its work.
    ///
    /// The distinction the skill pipeline has always made without naming: some
    /// supports decide how much exists and how big it is, which is a decision
    /// taken before anything is in the world; others act on a body being struck;
    /// one acts on a body dying. A support that fires at the wrong moment is not
    /// weaker, it is impossible — increased damage cannot be applied to a hit
    /// that has already been resolved.
    /// </summary>
    public enum SkillModifierPhase : byte
    {
        /// <summary>
        /// Folded into the numbers before anything is created. How many
        /// projectiles, how big the blast, how hard it hits.
        /// </summary>
        Cast = 0,

        /// <summary>Acts when something is struck: chains, splits, triggers.</summary>
        Hit = 1,

        /// <summary>Acts when something dies.</summary>
        Kill = 2
    }

    /// <summary>
    /// What each kind of support is, as facts rather than as authored fields.
    ///
    /// A struct of static methods, the same shape as GridFit and EquipmentSlots
    /// and for the same reason: the panel wants to tell the player when a gem
    /// acts, and the answer must be the same one the pipeline acts on.
    ///
    /// The phase is DERIVED from the kind rather than authored on the asset. It
    /// is a property of what a modifier does, not a choice: "increased damage,
    /// on kill" is not a weaker support, it is a state that cannot exist, and a
    /// field on the asset would let somebody write it down and then wonder why
    /// it does nothing.
    /// </summary>
    public struct SkillModifiers
    {
        /// <summary>
        /// Which moment this kind belongs to.
        ///
        /// Every answer here matches where the pipeline actually implements it:
        /// the Cast ones are folded in SkillDatabase.Resolve, the Hit ones live
        /// in SkillProjectileSystem and SkillHitSystem, and the one Kill answer
        /// is DeathReactionSystem. If this table and those systems ever
        /// disagree, the table is the one that is wrong.
        /// </summary>
        public static SkillModifierPhase PhaseOf(SkillModifierKind kind)
        {
            switch (kind)
            {
                // Split on impact, so the projectile has to reach something
                // first. It is the reason this enum exists rather than
                // everything simply being folded at cast.
                case SkillModifierKind.Fork:

                // Jumps onward from a body that was struck.
                case SkillModifierKind.AddedChains:

                // Casts something else where this landed.
                case SkillModifierKind.TriggerOnHit:
                    return SkillModifierPhase.Hit;

                case SkillModifierKind.ExplodeOnKill:
                    return SkillModifierPhase.Kill;

                // Everything else changes how much exists, how big it is or what
                // element it is — all decisions taken before anything is in the
                // world at all.
                default:
                    return SkillModifierPhase.Cast;
            }
        }

        /// <summary>
        /// One letter for a socket cell, which is twenty pixels across and has
        /// room for exactly that.
        /// </summary>
        public static string Marker(SkillModifierPhase phase)
        {
            switch (phase)
            {
                case SkillModifierPhase.Hit: return "H";
                case SkillModifierPhase.Kill: return "K";
                default: return "C";
            }
        }

        /// <summary>The word, for anywhere with room for one.</summary>
        public static string Describe(SkillModifierPhase phase)
        {
            switch (phase)
            {
                case SkillModifierPhase.Hit: return "on hit";
                case SkillModifierPhase.Kill: return "on kill";
                default: return "on cast";
            }
        }
    }
}
