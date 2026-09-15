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

                // Carries on through the body it just struck. Folded at cast
                // like the fork it sits beside, and answered at the impact for
                // the same reason — the projectile has to reach something
                // before there is anything to pass through.
                case SkillModifierKind.Pierce:

                // Decides what a blow does to a body that was already nearly
                // gone, which is a question only the resolver can answer.
                case SkillModifierKind.CullingStrike:

                // Folded at cast and answered where the projectile lands, like
                // the pierce beside it.
                case SkillModifierKind.ImpactBurst:

                // How far the next jump may look, decided at each body struck.
                case SkillModifierKind.IncreasedChainRange:

                // Carried to the body and spent in a reaction there.
                case SkillModifierKind.InfuseElement:
                    return SkillModifierPhase.Hit;

                case SkillModifierKind.ExplodeOnKill:

                // Pays out on a body falling, exactly like the burst beside it.
                // Both are carried to the kill by the blow rather than acting
                // at the moment of casting.
                case SkillModifierKind.ManaOnKill:
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
        /// Whether a skill lands on allies rather than enemies. Asked by the
        /// fold (no weapon damage in a heal), the cast (which queue it goes to)
        /// and the panel — one answer for three readers.
        /// </summary>
        public static bool IsSupportive(SkillEffectKind effect)
            => effect == SkillEffectKind.AllyTarget || effect == SkillEffectKind.AllyAura;

        /// <summary>Whether a skill is laid out as Count elements, which AddedCount can grow.</summary>
        public static bool IsPattern(SkillEffectKind effect)
            => effect == SkillEffectKind.Volley || effect == SkillEffectKind.Fissure ||
               effect == SkillEffectKind.Rain || effect == SkillEffectKind.Cyclone;

        /// <summary>Whether a projectile comes out of it, for the supports that act on one.</summary>
        public static bool Flies(SkillEffectKind effect)
            => effect == SkillEffectKind.Projectile || effect == SkillEffectKind.Volley;

        /// <summary>
        /// The effect the character's cast animation is filed under. The
        /// controller knows the first five; a pattern plays whichever of those it
        /// looks like, so a new effect never means a new animator state.
        /// </summary>
        public static SkillEffectKind AnimatesAs(SkillEffectKind effect)
        {
            switch (effect)
            {
                case SkillEffectKind.Volley:
                case SkillEffectKind.Beam:
                    return SkillEffectKind.Projectile;

                case SkillEffectKind.LeapSlam:
                case SkillEffectKind.Cyclone:
                case SkillEffectKind.Fissure:
                    return SkillEffectKind.MeleeArc;

                case SkillEffectKind.Rain:
                    return SkillEffectKind.AreaBurst;

                default:
                    return effect;
            }
        }

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

                // Raised by SkillCastSystem, which is where the roll happens:
                // a cast either crits or it does not, and everything it
                // produces carries that answer.
                case TriggerConditionType.OnCrit:
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

        // ─────────────────────────────────────────────────────────────────
        // Whether a gem can do anything at all where it has been put.
        //
        // Some supports cannot act on some skills, and no amount of plumbing
        // fixes it: a swing has no projectile to split, a bolt has no area to
        // widen, a weapon's free attack has no price to raise. Those are facts
        // about what the skill IS.
        //
        // What used to happen was that such a gem sat in its hole, drew its
        // letter, and did nothing — indistinguishable from a gem that was
        // working. Everything below exists so the panel can say so instead, in
        // the same words the fold would use if it could speak. One table, two
        // readers: the socket cell and the tooltip.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this kind of support changes anything about this skill.
        ///
        /// False is a promise that the fold will do literally nothing with it —
        /// not that it is a weak choice. Anything that depends on what ELSE is
        /// socketed is not asked here; see NeedsCompanion.
        /// </summary>
        public static bool AppliesTo(SkillModifierKind kind, in SkillShape skill)
        {
            if (!skill.Exists)
                return true;

            // A blink deals nothing of its own — its blows are the primary key's
            // skill — so the only supports it can spend are the ones about the
            // blink: how many steps, how often, how many stored, what it costs.
            // A dash is the same bargain with a single blow: how often, how many
            // stored, what it costs — and nothing about the blow itself.
            if (skill.Effect == SkillEffectKind.DashStrike)
            {
                return kind == SkillModifierKind.ReducedCooldown ||
                       kind == SkillModifierKind.AddedCharges ||
                       (kind == SkillModifierKind.IncreasedManaCost && skill.ManaCost > 0f);
            }

            if (skill.Effect == SkillEffectKind.BlinkStrike)
            {
                return kind == SkillModifierKind.AddedChains ||
                       kind == SkillModifierKind.IncreasedChainRange ||
                       kind == SkillModifierKind.ReducedCooldown ||
                       kind == SkillModifierKind.AddedCharges ||
                       (kind == SkillModifierKind.IncreasedManaCost && skill.ManaCost > 0f);
            }

            // A team spell has a heal to grow, a radius if it is an aura, and a
            // press to pay for. A status override is deliberately not on the
            // list: it would be a stun gem cast on your partner.
            if (IsSupportive(skill.Effect))
            {
                return kind == SkillModifierKind.IncreasedDamage ||
                       kind == SkillModifierKind.ReducedCooldown ||
                       kind == SkillModifierKind.AddedCharges ||
                       (kind == SkillModifierKind.IncreasedArea && skill.Effect == SkillEffectKind.AllyAura) ||
                       (kind == SkillModifierKind.IncreasedManaCost && skill.ManaCost > 0f);
            }

            switch (kind)
            {
                // Only what travels can be made faster, split, pushed through a
                // body or made to burst where it lands.
                case SkillModifierKind.Fork:
                case SkillModifierKind.Pierce:
                case SkillModifierKind.IncreasedProjectileSpeed:
                case SkillModifierKind.ImpactBurst:
                    return Flies(skill.Effect);

                // Only one effect stays on the ground long enough to last
                // longer.
                case SkillModifierKind.IncreasedDuration:
                    return skill.Effect == SkillEffectKind.PersistentZone;

                // A bolt is a line between two bodies and has no area at all;
                // a projectile has one only if it was authored to burst on
                // impact, and a radius of zero scaled by anything is zero.
                // (An impact burst gem beside it gives it one; see NeedsCompanion.)
                case SkillModifierKind.IncreasedArea:
                    if (skill.Effect == SkillEffectKind.ChainBolt)
                        return false;

                    return !Flies(skill.Effect) || skill.Radius > 0f;

                // A zone pulses for as long as it burns, so a chain from a zone
                // would be a number of jumps decided by the duration rather
                // than by what was socketed. Everything else chains.
                case SkillModifierKind.AddedChains:
                case SkillModifierKind.IncreasedChainRange:
                    return skill.Effect != SkillEffectKind.PersistentZone;

                case SkillModifierKind.AddedCount:
                    return IsPattern(skill.Effect);

                // A volley is all at once, so there is no tempo to change.
                case SkillModifierKind.PatternTempo:
                    return IsPattern(skill.Effect) && skill.Effect != SkillEffectKind.Volley;

                // A body lands in one place, so copies of a leap would be the
                // same blast stacked on itself. A channel is one beam from one
                // hand: copies would strike along lines nobody can see.
                case SkillModifierKind.Multicast:
                case SkillModifierKind.IncreasedSpread:
                    return skill.Effect != SkillEffectKind.LeapSlam && skill.Effect != SkillEffectKind.Beam;

                // A zone carries no element but its own and has no pulse to
                // infuse; the rest all strike bodies.
                case SkillModifierKind.InfuseElement:
                    return skill.Effect != SkillEffectKind.PersistentZone;

                // A chain is one effect touching several bodies, and firing per
                // jump would make the count depend on how crowded the room is. A
                // beam pulses eight times a second, and a trigger per pulse would
                // be a cast per frame for as long as the button is held.
                case SkillModifierKind.TriggerOnHit:
                    return skill.Effect != SkillEffectKind.ChainBolt && skill.Effect != SkillEffectKind.Beam;

                // Nothing to make more expensive. True of exactly the two
                // attacks welded into weapons, which are free on purpose.
                case SkillModifierKind.IncreasedManaCost:
                    return skill.ManaCost > 0f;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Short words for why a gem is inert here, for the tooltip. Empty when
        /// it is not.
        /// </summary>
        public static string WhyInert(SkillModifierKind kind, in SkillShape skill)
        {
            if (AppliesTo(kind, skill))
                return string.Empty;

            if ((skill.Effect == SkillEffectKind.BlinkStrike || skill.Effect == SkillEffectKind.DashStrike) &&
                kind != SkillModifierKind.IncreasedManaCost)
                return "its blows are the primary key's skill — link this there";

            if (IsSupportive(skill.Effect) && kind != SkillModifierKind.IncreasedManaCost)
                return "it only heals and strengthens allies";

            switch (kind)
            {
                case SkillModifierKind.Fork:
                case SkillModifierKind.Pierce:
                case SkillModifierKind.IncreasedProjectileSpeed:
                case SkillModifierKind.ImpactBurst:
                    return "it fires no projectile";

                case SkillModifierKind.IncreasedDuration:
                    return "it leaves nothing on the ground";

                case SkillModifierKind.IncreasedArea:
                    return skill.Effect == SkillEffectKind.ChainBolt
                        ? "a bolt jumps between bodies rather than covering ground"
                        : "it strikes one body and nothing around it";

                case SkillModifierKind.AddedChains:
                case SkillModifierKind.IncreasedChainRange:
                    return "a zone pulses where it lies rather than jumping onward";

                case SkillModifierKind.AddedCount:
                    return "it is one effect, not a pattern of them";

                case SkillModifierKind.PatternTempo:
                    return IsPattern(skill.Effect)
                        ? "a volley leaves all at once"
                        : "it is one effect, not a pattern of them";

                case SkillModifierKind.Multicast:
                case SkillModifierKind.IncreasedSpread:
                    return skill.Effect == SkillEffectKind.Beam
                        ? "a channel is one beam from one hand"
                        : "a body lands in one place";

                case SkillModifierKind.InfuseElement:
                    return "a zone burns with its own element only";

                case SkillModifierKind.TriggerOnHit:
                    return skill.Effect == SkillEffectKind.Beam
                        ? "a beam pulses too often to trigger on each pulse"
                        : "a bolt is one effect touching many bodies";

                case SkillModifierKind.IncreasedManaCost:
                    return "it costs nothing to cast";

                default:
                    return "it has nothing to act on";
            }
        }

        /// <summary>
        /// The support this one needs beside it to do anything, or None.
        ///
        /// The other half of "this gem is doing nothing", and a different half:
        /// this is about the GROUP rather than the skill. A spread support
        /// widens the gap between copies of a cast, so with one cast there is
        /// no gap to widen — however good the skill it is socketed beside.
        /// </summary>
        public static SkillModifierKind NeedsCompanion(SkillModifierKind kind)
            => kind == SkillModifierKind.IncreasedSpread
                ? SkillModifierKind.Multicast
                : kind;

        /// <summary>Whether this kind needs something else in the group at all.</summary>
        public static bool HasCompanion(SkillModifierKind kind)
            => NeedsCompanion(kind) != kind;
    }
}
