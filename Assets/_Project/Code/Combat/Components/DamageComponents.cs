using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — damage happens on the host and nowhere else.
    //
    // The shape here is the whole point of this part of the project. Nothing
    // subtracts health directly. Everything that wants to hurt something writes
    // an INTENT into a buffer on the target, and one system turns the pile of
    // intents into a result. That indirection is what makes "chain after a
    // kill" and "explode on kill" possible without any of the systems involved
    // knowing about each other.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What kind of damage this is.
    ///
    /// Carried all the way through the pipeline so an elemental conversion is a
    /// real change rather than a cosmetic one. Nothing resists anything yet —
    /// enemies have health and no mitigation — so today the type decides only
    /// what the damage is called.
    ///
    /// This is also THE element, singular. The status and reaction systems could
    /// have brought an ElementType of their own, and it would have needed
    /// converting at every boundary in the pipeline this one already crosses —
    /// two enums for one fact, of the kind that stay in step until the day one
    /// of them gains a value. A status is a DamageType with a lifetime, and a
    /// reaction is a pair of DamageTypes.
    /// </summary>
    public enum DamageType : byte
    {
        Physical = 0,
        Fire = 1,
        Cold = 2,
        Lightning = 3,

        /// <summary>
        /// Added for the element system, which needed a fifth to prove that
        /// "how many elements are there" is one number in one place. Nothing
        /// resists it and no skill deals it yet; it exists so a reaction table
        /// authored against it is not a special case.
        /// </summary>
        Chaos = 4
    }

    /// <summary>
    /// How much life something has left. Added to enemies at bake time.
    /// </summary>
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }

    /// <summary>
    /// One intent to hurt this entity, this frame.
    ///
    /// A buffer on the TARGET rather than a global event stream, so a hundred
    /// projectiles landing on a hundred enemies write to a hundred separate
    /// places and resolution parallelises over entities for free.
    ///
    /// The explosion fields ride along because the blow that lands the kill is
    /// what decides whether the corpse explodes. Carrying them here means
    /// DamageResolutionSystem can answer that question without looking up which
    /// skill, cast by whom, with which supports, produced this event.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct DamageEvent : IBufferElementData
    {
        public float Amount;
        public DamageType Type;

        /// <summary>Who gets the credit. Not an Entity: the killer may be gone by resolution.</summary>
        public int SourcePlayerId;

        /// <summary>Blast radius if this event lands the killing blow. Zero for most events.</summary>
        public float ExplosionRadius;

        public float ExplosionDamage;

        /// <summary>
        /// The fraction of life below which this blow simply finishes the
        /// target, or zero.
        ///
        /// Rides along for the same reason the explosion does: the resolver is
        /// the only place that can see how much life is left, and by the time
        /// it looks, the skill that decided this is long gone.
        /// </summary>
        public float CullThreshold;

        /// <summary>
        /// Mana the source gets back if this blow kills. Read off the killing
        /// blow only, exactly like the explosion.
        /// </summary>
        public float ManaOnKill;

        /// <summary>
        /// Whether the hit stage rolled this blow critical.
        ///
        /// The amount already carries the multiplier — this is here so the
        /// number on screen can say so. Without it a critical blow and a lucky
        /// one look identical, and a build bought entirely for crit would have
        /// nothing to show for itself.
        /// </summary>
        public bool Crit;

        /// <summary>
        /// Elements riding along with this blow that are not its own — what the
        /// projectile picked up on the way.
        ///
        /// Carried here rather than looked up on the target, because by the time
        /// damage is resolved the projectile that gathered them is back in the
        /// pool. It is the whole of "the projectile brought something with it":
        /// one byte, set where the effect met a zone, read where the reaction is
        /// worked out.
        /// </summary>
        public byte CarriedElements;

        /// <summary>
        /// A status this blow applies by name, beyond the mark its element
        /// leaves on its own.
        ///
        /// The only way anything non-elemental is ever applied. Nothing stuns of
        /// its own accord and no pair of elements produces a root, so a control
        /// or debuff status is on a target because a skill said so and for no
        /// other reason — which is also what keeps it a build decision rather
        /// than a side effect of picking an element.
        ///
        /// A type rather than an index, because the skill database and the
        /// status table are baked by two authoring objects that share no
        /// ordering. The same argument that made a gem name its skill by a
        /// stable id; the status set is closed, so the enum is the id.
        /// </summary>
        public StatusEffectType AppliedStatus;

        /// <summary>
        /// Whether this damage is itself the product of a status or a reaction.
        ///
        /// The rail that keeps reactions from feeding themselves. A burn tick
        /// that refreshed its own burn would never end, and a reaction blast
        /// whose damage reacted again would walk across a crowd — the same shape
        /// as the explode-on-kill support having to zero its own blast radius,
        /// and the same one line standing between a feature and a hung frame.
        ///
        /// False for every blow a player actually struck, which is why the flag
        /// is worded this way round: the default has to be the ordinary case.
        /// </summary>
        public bool FromReaction;

        /// <summary>
        /// Whether the cast behind this blow was fired by a trigger rather than
        /// by a key press.
        ///
        /// Nothing in combat reads it — it changes no damage and gates no rule.
        /// It exists for the damage meter, which is the one place the question
        /// "is my trigger gem actually firing" has to have an answer, and
        /// answering it any other way meant carrying a skill index through four
        /// more structs. Derived rather than authored: a cast at depth zero is a
        /// button, and everything deeper is a consequence.
        /// </summary>
        public bool FromTrigger;
    }

    /// <summary>
    /// "This entity has run out of health."
    ///
    /// Enableable, so the job that resolves damage can kill something without a
    /// structural change in the middle of a parallel pass. What death MEANS —
    /// the explosion, the entity going away — belongs to the system that reads
    /// this, one stage later.
    /// </summary>
    public struct Dead : IComponentData, IEnableableComponent
    {
        public int KilledByPlayerId;

        /// <summary>The element of the killing blow. An explosion is made of the same stuff.</summary>
        public DamageType Type;

        public float ExplosionRadius;
        public float ExplosionDamage;

        /// <summary>
        /// Mana owed to the killer for this body.
        ///
        /// Beside the explosion because it is the same kind of fact — what the
        /// killing blow was carrying — and read by the same system, one frame
        /// later, on the main thread where a character's pool can be written.
        /// </summary>
        public float ManaOnKill;
    }

    /// <summary>
    /// PRESENTATION — what to put on screen about the damage this entity took.
    ///
    /// Written by the resolver because that is the only place that knows the
    /// TOTAL: five projectiles landing on one enemy in one frame are five events
    /// and one number. Batching here rather than at the point of each hit is the
    /// difference between a readable figure and five of them stacked on top of
    /// each other.
    ///
    /// Enableable, so a frame in which nothing was hurt costs nothing to skip
    /// and raising it inside a parallel job is free — exactly like Dead below,
    /// which is read as a value and toggled as a flag in the same job.
    /// </summary>
    public struct DamageFeedback : IComponentData, IEnableableComponent
    {
        public float Amount;
        public DamageType Type;

        /// <summary>Whether this was the blow that finished it.</summary>
        public bool Killing;

        /// <summary>
        /// Whether anything that landed this frame was a critical blow.
        ///
        /// One flag for the frame, like the amount beside it: five projectiles
        /// on one body are one number, and if any of them crit the number is
        /// worth looking at.
        /// </summary>
        public bool Crit;
    }

    /// <summary>
    /// A body on its way out.
    ///
    /// Death is not instant destruction: the entity stops being an enemy, then
    /// shrinks and darkens for a moment, then goes away. Without the pause a
    /// crowd does not die, it simply stops existing between two frames — and
    /// that is the difference between a wave being cleared and a wave being
    /// switched off.
    ///
    /// The starting scale and colour are captured at the moment of death rather
    /// than baked, so whatever the body looked like is where the fade begins.
    /// </summary>
    public struct DeathFade : IComponentData, IEnableableComponent
    {
        public float Remaining;
        public float Duration;
        public float StartScale;
        public float4 StartColor;
    }

    /// <summary>
    /// Running totals for the run. The one shared counter the debug overlay
    /// reads, so nothing has to walk every corpse to answer "how many".
    ///
    /// Deliberately not per player yet: credit for a kill is a scoring decision,
    /// and there is nothing to spend score on. The killer id is already carried
    /// on every event, so splitting this later costs one loop.
    /// </summary>
    public struct CombatTally : IComponentData
    {
        public int Kills;
        public float DamageDealt;
    }
}
