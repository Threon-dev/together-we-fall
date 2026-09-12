using Unity.Entities;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // The damage meter: what a build is actually doing, as opposed to what it
    // says on the gem.
    //
    // It measures nothing new. Every blow already writes a DamageEvent on its
    // target, already says who struck it, already says whether it was a
    // reaction and — since the meter needed it — whether the cast behind it was
    // a trigger. The meter is a reader placed one stage before the resolver
    // drains that buffer, and nothing else in the pipeline knows it exists.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks a target that keeps a record of what has been done to it.
    ///
    /// On the dummy rather than on the player, because the question is about one
    /// target under fire and that is what a dummy is for. It also means several
    /// players can beat on the same dummy — the entries carry who struck them,
    /// so the panel splits the log rather than mixing two builds into one
    /// number.
    /// </summary>
    public struct DamageMeter : IComponentData
    {
        /// <summary>
        /// Seconds of history to keep. Everything older is dropped each frame,
        /// which is what makes the buffer bounded without anybody choosing a
        /// capacity that a chain reaction can beat.
        /// </summary>
        public float Window;
    }

    /// <summary>
    /// One blow, as the meter saw it.
    ///
    /// Thirty-two inline is roughly a busy second; past that the buffer spills
    /// into the heap for a frame or two and the pruning pulls it back. That is
    /// the right way round — an overflowing meter must not drop the hits it is
    /// there to count.
    /// </summary>
    [InternalBufferCapacity(32)]
    public struct DamageMeterEntry : IBufferElementData
    {
        /// <summary>
        /// Game time when it landed. The same clock cooldowns and durations run
        /// on, so a rate worked out from these is damage per second of the
        /// fight — which is what a build is compared on — rather than per second
        /// of wall clock with the hit-stops in it.
        /// </summary>
        public float Timestamp;

        public float Damage;
        public DamageType Element;

        /// <summary>Who struck it. The whole reason two players can share a dummy.</summary>
        public int SourcePlayerId;

        /// <summary>A burn tick or a reaction blast rather than a blow.</summary>
        public bool FromReaction;

        /// <summary>Something the build did on its own, rather than a key press.</summary>
        public bool FromTrigger;
    }

    /// <summary>
    /// "Throw the log away."
    ///
    /// Enableable, so the panel asks by raising a flag rather than by writing to
    /// a buffer it does not own — the same shape as every other request in the
    /// project, in its smallest possible form.
    /// </summary>
    public struct DamageMeterReset : IComponentData, IEnableableComponent
    {
    }
}
