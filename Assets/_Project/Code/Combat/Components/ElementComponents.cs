using Unity.Entities;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — which elements are on something, and for how long.
    //
    // The one idea this whole feature rests on: "shocked enemy" and "projectile
    // that flew through a fire wall" are the same thing said twice. Both are a
    // carrier holding an element it did not start with, and both only matter at
    // the instant a blow lands and a second element arrives. So there is one
    // place that resolves what two elements meeting means — ElementReactionSystem
    // — and the two carriers differ only in where the element is written down.
    //
    // A status lives on the TARGET and has a lifetime in seconds. A carried tag
    // lives on the EFFECT — a projectile, a hit, a blast — and lasts exactly as
    // long as that effect does, which is why it needs no timer of its own.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A set of elements, one bit each.
    ///
    /// Used for what a projectile picked up on the way and what arrives beside
    /// the main element of a blow. A mask rather than a buffer of tags, and the
    /// reason is the rule it enforces for free: passing through two fire zones
    /// leaves one fire tag, because a set has no room for a duplicate. The
    /// alternative — a buffer element per pickup — would need that rule written
    /// out, and would need copying by hand every time a projectile forks, which
    /// is the one place on this path that has to stay cheap. A field on
    /// SkillProjectile is inherited by a fork for nothing.
    ///
    /// One byte covers every element there is, which is what makes it free to
    /// carry through PendingHit, PendingArea and DamageEvent alike.
    /// </summary>
    public struct ElementMask
    {
        /// <summary>How many elements exist. Derived, so adding one cannot desync it.</summary>
        public const int Count = (int)DamageType.Chaos + 1;

        public static byte Of(DamageType element) => (byte)(1 << (int)element);

        public static bool Has(byte mask, DamageType element) => (mask & Of(element)) != 0;

        public static byte With(byte mask, DamageType element) => (byte)(mask | Of(element));

        public static byte Without(byte mask, DamageType element)
            => (byte)(mask & ~Of(element));
    }

    /// <summary>
    /// One element currently afflicting this entity.
    ///
    /// A buffer on the target, like DamageEvent and for the same reason: a
    /// hundred enemies burning at once are a hundred separate places, and the
    /// tick parallelises over entities for free.
    ///
    /// At most one entry per element, ever. Keyed that way rather than by which
    /// status definition produced it, because that is what makes "the other
    /// element already on this target" a question with one answer — and it
    /// bounds the buffer at the number of elements without anyone having to
    /// prune it.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct ElementalStatus : IBufferElementData
    {
        public DamageType Element;

        /// <summary>Which status this is, as an index into the reaction database.</summary>
        public int Definition;

        public float RemainingDuration;

        /// <summary>Seconds until the next damage tick. Unused by statuses that do not burn.</summary>
        public float TickRemaining;

        public int Stacks;

        /// <summary>
        /// Who gets the credit for what this status does.
        ///
        /// A player id rather than an Entity, exactly as on DamageEvent: an
        /// Ignite outlives the projectile that lit it, and quite often the
        /// caster too.
        /// </summary>
        public int SourcePlayerId;

        /// <summary>
        /// When this was last applied or refreshed.
        ///
        /// The tie-break for which status a blow reacts with when a target
        /// carries several. Newest wins — the player's most recent decision is
        /// the one they are expecting to pay off.
        /// </summary>
        public float AppliedAt;
    }
}
