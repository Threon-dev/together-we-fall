using Unity.Entities;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — what is currently afflicting something.
    //
    // One buffer for every status there is. Crowd control, damage over time and
    // stat debuffs are not three features with three lifetimes, three expiry
    // passes and three ways of stacking; they are one lifetime with three
    // answers to "and what does it do", and the answers live in StatusEffects.
    //
    // This file used to hold ElementalStatus alone, which was the same buffer
    // keyed by element. That key was chosen so that "what else is on this
    // target" had exactly one answer, which is what the reaction search rests
    // on. Widening the set to statuses that carry no element at all meant the
    // key had to widen with it: it is now the status TYPE, and the elemental
    // half of the invariant survives because at most one status per element may
    // be authored as that element's mark — which the baker checks, as it always
    // did.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One status currently on this entity.
    ///
    /// A buffer on the target, like DamageEvent and for the same reason: a
    /// hundred enemies burning at once are a hundred separate places, and the
    /// tick parallelises over entities for free.
    ///
    /// At most one entry per type, ever. That is what bounds the buffer without
    /// anyone pruning it, and what makes "is this target stunned" a question
    /// with one answer rather than a sum over duplicates.
    /// </summary>
    [InternalBufferCapacity(3)]
    public struct ActiveStatusEffect : IBufferElementData
    {
        /// <summary>What this is. The key, and the only thing that decides behaviour.</summary>
        public StatusEffectType Type;

        /// <summary>
        /// The element it marks its target with.
        ///
        /// Meaningful only for the types StatusEffects.CarriesElement admits.
        /// A stun has no element and the reaction search skips it, rather than
        /// every status having to nominate one and physical quietly becoming
        /// the answer for all of them.
        /// </summary>
        public DamageType Element;

        /// <summary>Which status this is, as an index into the status database.</summary>
        public int Definition;

        public float RemainingDuration;

        /// <summary>Seconds until the next damage tick. Unused by statuses that do not burn.</summary>
        public float TickRemaining;

        public int Stacks;

        /// <summary>
        /// What this is worth right now: magnitude per stack times stacks.
        ///
        /// Written at every application and refresh rather than derived where it
        /// is read. Not a cache of a constant — the day a slow is stronger
        /// because the caster is, this is where that is decided, and the
        /// alternative would be a movement job that has to reach the caster's
        /// stat sheet through a player id. It also keeps the gate pass free of
        /// the status database.
        /// </summary>
        public float Magnitude;

        /// <summary>
        /// Who gets the credit for what this status does.
        ///
        /// A player id rather than an Entity, exactly as on DamageEvent: an
        /// ignite outlives the projectile that lit it, and quite often the
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

    /// <summary>
    /// Everything the statuses on this entity add up to.
    ///
    /// A single derived answer rather than a system per control type. The spec
    /// this came from asked for StunBlockSystem, RootBlockSystem,
    /// SilenceBlockSystem and SlowMovementSystem; they would be four systems
    /// asking two questions — may this thing move, may this thing cast — of the
    /// same buffer, in the same frame, and the two consumers would each have to
    /// consult all four. So the pass that already walks every status once a
    /// frame reduces them to this, and movement and casting each read one field.
    ///
    /// Not enableable, and that is deliberate against this project's usual
    /// habit. An enableable component read from a job needs WithPresent, which
    /// is the gotcha that already cost this project a day when enemies stopped
    /// repathing — and here the neutral value has to be readable by the damage
    /// resolver for every target whether or not anything is afflicting it. A
    /// struct of ones costs a write per enemy per frame and nothing else.
    /// </summary>
    public struct StatusGate : IComponentData
    {
        public bool BlocksMovement;
        public bool BlocksCasting;

        /// <summary>Run from whatever this was chasing, rather than towards it.</summary>
        public bool Flees;

        public float MoveSpeedMultiplier;

        public float DamageTakenMultiplier;

        /// <summary>
        /// What this entity's own blows are worth.
        ///
        /// Weaken writes it and nothing reads it yet, because enemies do not
        /// attack. The seam is one multiplication beside the code that will know
        /// — the same honesty as ModifierConditionType.CasterRecentlyHit, which
        /// is documented as having no source rather than quietly left out.
        /// </summary>
        public float DamageDealtMultiplier;

        /// <summary>Nothing is afflicting this entity. What the gate is on a clean body.</summary>
        public static StatusGate Neutral => new StatusGate
        {
            MoveSpeedMultiplier = 1f,
            DamageTakenMultiplier = 1f,
            DamageDealtMultiplier = 1f
        };
    }

    /// <summary>
    /// A hard control this entity cannot be put under again yet.
    ///
    /// Diminishing returns, and not a balance nicety: this game has trigger gems
    /// that cast on a kill and chains that hit forty bodies, so a skill that
    /// stuns is one link away from a stun that never ends. Without this the
    /// combination is not strong, it is a crowd that stops being a fight — and
    /// in co-op, once enemies can strike back, the same combination on the other
    /// side is a player who stops being a player.
    ///
    /// Keyed by type, so being stunned does not protect against being rooted.
    /// Only hard control grants it; slow is deliberately outside, because being
    /// slower is still playing and a diminishing slow would mostly read as the
    /// gem having stopped working.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct CrowdControlImmunity : IBufferElementData
    {
        public StatusEffectType Type;

        public float Remaining;
    }

    /// <summary>
    /// What this entity resists before diminishing returns are even consulted.
    ///
    /// Two fields and a decision behind each. A boss that can be perma-stunned
    /// by the right build is not a boss, and diminishing returns alone do not
    /// fix that — they cap the uptime, they do not stop the fight being a
    /// stun-lock with gaps. So the boss is simply immune to hard control and
    /// remains open to everything soft: slow, burn, vulnerability. That keeps
    /// control gear meaningful in a boss room without making the room trivial.
    ///
    /// Baked from EnemyConfig, so it is an authored property of a kind of enemy
    /// rather than a special case anybody has to remember to write. There are no
    /// boss enemies yet — the boss room is only the hardest fight — so today
    /// every enemy is baked with the ordinary values, and the day one exists it
    /// is a config asset and no code.
    /// </summary>
    public struct CrowdControlResistance : IComponentData
    {
        /// <summary>Stun, Root, Silence and Fear do not land at all.</summary>
        public bool ImmuneToHardControl;

        /// <summary>
        /// Scales the duration of everything that does land. One is ordinary,
        /// zero makes a status arrive already expired.
        /// </summary>
        public float DurationMultiplier;

        public static CrowdControlResistance None => new CrowdControlResistance
        {
            ImmuneToHardControl = false,
            DurationMultiplier = 1f
        };
    }
}
