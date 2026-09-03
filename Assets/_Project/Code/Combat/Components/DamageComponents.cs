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
    /// what the damage is called. The single place that will ever consult it is
    /// DamageResolutionSystem.
    /// </summary>
    public enum DamageType : byte
    {
        Physical = 0,
        Fire = 1,
        Cold = 2,
        Lightning = 3
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
