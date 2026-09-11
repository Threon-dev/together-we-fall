using Unity.Mathematics;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // The one place a status type is turned into behaviour.
    //
    // Every question the rest of the game asks about a status — what category
    // it is in, whether it stops movement, whether it stops casting, which
    // number it scales and in which direction, whether it is hard control and
    // therefore subject to diminishing returns — is answered here, by a switch
    // on the type. Nothing else in the project branches on a status type.
    //
    // Derived rather than authored, and that is the same decision this project
    // has already made twice. The phase of a support gem is read off its kind
    // instead of being a field, because "increased damage on kill" is a state
    // that should not exist; a keystone is a closed enum because a keystone
    // nobody implemented would be a value that silently does nothing. A Stun
    // authored with the Periodic Damage category, or a Root that forgot to tick
    // the box that stops movement, are exactly the same kind of state. The
    // asset says how long, how hard and how many times; the type says what it
    // IS, and adding a new one is an enum value AND a case here, together.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every status in the game, as a closed set.
    ///
    /// This is also the key the buffer on a target is unique by, and the name a
    /// skill refers to a status by across two databases — the reaction table and
    /// the skill table are baked by different authoring objects with no shared
    /// ordering, so an index would mean whichever happened to be first. The same
    /// argument that made item and skill ids an FNV hash; here the set is
    /// closed, so the enum IS the stable id and no hashing is needed.
    ///
    /// Sixteen values exactly, so the whole set fits a ushort mask with one bit
    /// each. None takes bit zero and is never set in a mask.
    /// </summary>
    public enum StatusEffectType : byte
    {
        None = 0,

        // Crowd control — what the target may do.
        Stun = 1,
        Slow = 2,
        Root = 3,
        Silence = 4,
        Fear = 5,

        // Periodic damage — what the target loses while it lasts.
        Ignite = 6,
        Poison = 7,
        Bleed = 8,
        Scorch = 9,

        // Elemental marks — carriers with no effect of their own, which exist
        // to be the left-hand side of a reaction.
        Shock = 10,
        Chill = 11,

        // Stat modifiers — what the target's numbers become.
        Weaken = 12,
        Vulnerable = 13,
        Haste = 14,
        Fortify = 15
    }

    /// <summary>
    /// What kind of thing a status is.
    ///
    /// Derived from the type, never authored beside it. It decides two things
    /// and no more: whether diminishing returns apply, and how the panel groups
    /// them. Behaviour comes from the predicates below, not from this — Ignite
    /// is both a mark and a burn, so a category that dictated behaviour would
    /// have to pick one of them and be wrong about the other.
    /// </summary>
    public enum StatusCategory : byte
    {
        /// <summary>Decides what the target may do.</summary>
        CrowdControl = 0,

        /// <summary>Hurts on a rhythm for as long as it lasts.</summary>
        PeriodicDamage = 1,

        /// <summary>Scales one of the target's numbers.</summary>
        StatModifier = 2,

        /// <summary>
        /// Does nothing on its own, and exists to be reacted with.
        ///
        /// Its own category rather than a stat modifier with no modifier,
        /// because "shocked" is not a weaker debuff — it is the thing the whole
        /// reaction table is written about, and every status that existed before
        /// this file is either this or periodic damage.
        /// </summary>
        ElementalMark = 3
    }

    /// <summary>Which of a target's numbers a status scales.</summary>
    public enum StatusModifierTarget : byte
    {
        None = 0,
        MoveSpeed = 1,
        DamageTaken = 2,
        DamageDealt = 3
    }

    /// <summary>
    /// A set of statuses, one bit each.
    ///
    /// The same shape and the same argument as ElementMask: a set has no room
    /// for a duplicate, so "a target cannot be stunned twice" is a property of
    /// the type rather than a rule somebody has to write. Used for what a
    /// condition asks about and what the presentation layer draws.
    /// </summary>
    public struct StatusMask
    {
        /// <summary>How many statuses exist. Derived, so adding one cannot desync it.</summary>
        public const int Count = (int)StatusEffectType.Fortify + 1;

        public static ushort Of(StatusEffectType type) => (ushort)(1 << (int)type);

        public static bool Has(ushort mask, StatusEffectType type)
            => type != StatusEffectType.None && (mask & Of(type)) != 0;

        public static ushort With(ushort mask, StatusEffectType type)
            => type == StatusEffectType.None ? mask : (ushort)(mask | Of(type));
    }

    /// <summary>
    /// The one place a status type means anything.
    ///
    /// A struct of static methods, like GridFit, EquipmentSlots and
    /// SkillConditions: the simulation asks it what a status does, the baker
    /// asks it whether an asset makes sense, and the presenter asks it what to
    /// draw. Three callers, one rule.
    /// </summary>
    public struct StatusEffects
    {
        /// <summary>How much a slow may take, so nothing is ever pinned by a number.</summary>
        public const float MinMoveMultiplier = 0.1f;

        public const float MaxMoveMultiplier = 3f;

        public const float MinDamageMultiplier = 0.1f;

        public const float MaxDamageMultiplier = 5f;

        public static StatusCategory CategoryOf(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Stun:
                case StatusEffectType.Slow:
                case StatusEffectType.Root:
                case StatusEffectType.Silence:
                case StatusEffectType.Fear:
                    return StatusCategory.CrowdControl;

                case StatusEffectType.Ignite:
                case StatusEffectType.Poison:
                case StatusEffectType.Bleed:
                case StatusEffectType.Scorch:
                    return StatusCategory.PeriodicDamage;

                case StatusEffectType.Weaken:
                case StatusEffectType.Vulnerable:
                case StatusEffectType.Haste:
                case StatusEffectType.Fortify:
                    return StatusCategory.StatModifier;

                default:
                    return StatusCategory.ElementalMark;
            }
        }

        /// <summary>
        /// Whether this status names an element, and therefore takes part in
        /// reactions.
        ///
        /// A burn and a mark do; a stun does not. Deliberately not a flag on the
        /// asset: "Stunned, element Fire" is a sentence with no meaning, and the
        /// reaction search would then have to decide what to do with it.
        /// </summary>
        public static bool CarriesElement(StatusEffectType type)
        {
            StatusCategory category = CategoryOf(type);
            return category == StatusCategory.PeriodicDamage ||
                   category == StatusCategory.ElementalMark;
        }

        /// <summary>
        /// Whether this is the hard kind of control — the kind that takes the
        /// game away from whoever is under it.
        ///
        /// One predicate doing three jobs, which is why it is worth naming. It
        /// decides which statuses grant immunity when they end, which statuses
        /// that immunity is checked against, and what an enemy with innate
        /// control resistance is immune to. Slow is control and is deliberately
        /// not in it: being slower is still playing.
        /// </summary>
        public static bool IsHardControl(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Stun:
                case StatusEffectType.Root:
                case StatusEffectType.Silence:
                case StatusEffectType.Fear:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Whether this status stops its target moving at all.</summary>
        public static bool BlocksMovement(StatusEffectType type)
            => type == StatusEffectType.Stun || type == StatusEffectType.Root;

        /// <summary>
        /// Whether this status stops its target casting.
        ///
        /// Silence and Stun, which is the whole difference between the three
        /// hard controls: Root takes movement and leaves skills, Silence takes
        /// skills and leaves movement, Stun takes both. Three statuses, two
        /// predicates, no third mechanism.
        /// </summary>
        public static bool BlocksCasting(StatusEffectType type)
            => type == StatusEffectType.Stun || type == StatusEffectType.Silence;

        /// <summary>
        /// Whether the target runs from whatever it was chasing.
        ///
        /// The one control that redirects rather than removes. It is a sign flip
        /// on a velocity that has already been computed, which is the only
        /// reason it is in at all — an AI state would be the complex enemy
        /// behaviour this project has deliberately not started.
        /// </summary>
        public static bool Flees(StatusEffectType type) => type == StatusEffectType.Fear;

        /// <summary>
        /// Which number this status scales, and in which direction.
        ///
        /// The sign lives here rather than in the asset because "Haste with a
        /// magnitude of minus thirty percent" is another state that should not
        /// exist. An author says how much; the type says which way. It is also
        /// what makes Slow and Haste one mechanism and Vulnerable and Fortify
        /// another, rather than four.
        /// </summary>
        public static StatusModifierTarget ModifierOf(StatusEffectType type, out float sign)
        {
            switch (type)
            {
                case StatusEffectType.Slow:
                    sign = -1f;
                    return StatusModifierTarget.MoveSpeed;

                case StatusEffectType.Haste:
                    sign = 1f;
                    return StatusModifierTarget.MoveSpeed;

                case StatusEffectType.Vulnerable:
                    sign = 1f;
                    return StatusModifierTarget.DamageTaken;

                case StatusEffectType.Fortify:
                    sign = -1f;
                    return StatusModifierTarget.DamageTaken;

                case StatusEffectType.Weaken:
                    sign = -1f;
                    return StatusModifierTarget.DamageDealt;

                default:
                    sign = 0f;
                    return StatusModifierTarget.None;
            }
        }

        /// <summary>
        /// How loudly this status should be drawn when a body carries several.
        ///
        /// Presentation asks it, and it lives here rather than in the presenter
        /// for the ordinary reason: "which of these matters most" is a fact
        /// about the status, and a second copy of it beside the drawing code
        /// would drift the moment a status is added.
        /// </summary>
        public static int TintPriority(StatusEffectType type)
        {
            if (IsHardControl(type))
                return 3;

            if (type == StatusEffectType.Slow || type == StatusEffectType.Haste)
                return 2;

            return CategoryOf(type) == StatusCategory.PeriodicDamage ? 1 : 0;
        }

        /// <summary>
        /// What colour a body under this status is washed with.
        ///
        /// Control drains towards grey, haste towards white, and everything
        /// elemental defers to the palette the rest of the game already uses —
        /// so a burning enemy and a fire damage number are the same orange.
        /// </summary>
        public static float4 TintOf(StatusEffectType type, DamageType element)
        {
            switch (type)
            {
                case StatusEffectType.Stun:
                    return new float4(1f, 0.95f, 0.45f, 1f);

                case StatusEffectType.Root:
                case StatusEffectType.Silence:
                    return new float4(0.55f, 0.5f, 0.65f, 1f);

                case StatusEffectType.Fear:
                    return new float4(0.6f, 0.35f, 0.7f, 1f);

                case StatusEffectType.Slow:
                    return new float4(0.5f, 0.7f, 1f, 1f);

                case StatusEffectType.Haste:
                    return new float4(1f, 1f, 0.85f, 1f);

                case StatusEffectType.Weaken:
                    return new float4(0.5f, 0.55f, 0.5f, 1f);

                case StatusEffectType.Vulnerable:
                    return new float4(1f, 0.6f, 0.6f, 1f);

                case StatusEffectType.Fortify:
                    return new float4(0.7f, 0.8f, 0.9f, 1f);

                default:
                    return DamageTypePalette.For(element);
            }
        }

        /// <summary>
        /// A few letters for the marker over a body. Not localised and not meant
        /// to be: icon art is a later problem, and a prototype that says STN is
        /// readable today.
        /// </summary>
        public static string GlyphOf(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Stun: return "STUN";
                case StatusEffectType.Slow: return "SLOW";
                case StatusEffectType.Root: return "ROOT";
                case StatusEffectType.Silence: return "SIL";
                case StatusEffectType.Fear: return "FEAR";
                case StatusEffectType.Ignite: return "IGN";
                case StatusEffectType.Poison: return "PSN";
                case StatusEffectType.Bleed: return "BLD";
                case StatusEffectType.Scorch: return "SCR";
                case StatusEffectType.Shock: return "SHK";
                case StatusEffectType.Chill: return "CHL";
                case StatusEffectType.Weaken: return "WKN";
                case StatusEffectType.Vulnerable: return "VUL";
                case StatusEffectType.Haste: return "HST";
                case StatusEffectType.Fortify: return "FRT";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// Clamps a gathered multiplier to something the rest of the game can
        /// survive. Ten stacks of a badly tuned slow should be very slow, never
        /// a standstill nothing can undo, and never negative.
        /// </summary>
        public static float ClampMove(float multiplier)
            => math.clamp(multiplier, MinMoveMultiplier, MaxMoveMultiplier);

        public static float ClampDamage(float multiplier)
            => math.clamp(multiplier, MinDamageMultiplier, MaxDamageMultiplier);
    }
}
