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
        Kill = 2,

        /// <summary>
        /// Does not act on the skill at all — it decides who casts it.
        ///
        /// Its own phase rather than a fourth kind of Cast, because it is the
        /// one support whose moment is not inside a cast: it happens when
        /// something in the world satisfies a condition, and the cast is the
        /// consequence. Implemented in TriggerEvaluationSystem, which is the
        /// only stage that reads no skill and produces one.
        /// </summary>
        Trigger = 3
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

                // Casts the actives beside it when the world says so, rather
                // than when a key is pressed.
                case SkillModifierKind.TriggerOnCondition:
                    return SkillModifierPhase.Trigger;

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
                case SkillModifierPhase.Trigger: return "T";
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
                case SkillModifierPhase.Trigger: return "casts itself";
                default: return "on cast";
            }
        }

        /// <summary>
        /// Whether this kind takes the key away from the actives beside it.
        ///
        /// Asked by the cast system to refuse a manual press and by the panel to
        /// mark the hotkey as automatic, which is the same question and must
        /// therefore have one answer. Derived from the phase rather than listed,
        /// so a second sort of trigger cannot be added without landing here.
        /// </summary>
        public static bool IsAutomatic(SkillModifierKind kind)
            => PhaseOf(kind) == SkillModifierPhase.Trigger;

        /// <summary>
        /// Whether anything in the game can currently make this condition true.
        ///
        /// Four of the six trigger conditions are about the player being hurt,
        /// and the player cannot be hurt yet. A gem authored against one of them
        /// is not broken, it is early — so this is a warning at bake and a note
        /// in the panel rather than a refusal, and it is one table so that the
        /// warning and the note cannot disagree about which four.
        ///
        /// The day player health lands, the answers here change in the same
        /// commit as the Announce calls that make them true. If this table and
        /// the announcers ever disagree, the table is the one that is wrong —
        /// the same rule as PhaseOf.
        /// </summary>
        public static bool HasSource(TriggerConditionType condition)
        {
            switch (condition)
            {
                // Raised by DeathReactionSystem.
                case TriggerConditionType.OnKill:

                // Raised by ElementReactionSystem.
                case TriggerConditionType.OnStatusApplied:
                    return true;

                // OnLowHealth needs no announcer — it is a state read off the
                // character every frame — but it needs a Health component to
                // read, and characters have none. The rest need a player who
                // can be hurt, crit or block.
                default:
                    return false;
            }
        }

        /// <summary>The same question for a support condition. Only one is early.</summary>
        public static bool HasSource(ModifierConditionType condition)
            => condition != ModifierConditionType.CasterRecentlyHit;
    }
}
