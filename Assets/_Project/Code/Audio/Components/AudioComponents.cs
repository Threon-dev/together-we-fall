using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Audio
{
    // ─────────────────────────────────────────────────────────────────────
    // PRESENTATION — things that happened, for something to play.
    //
    // Deliberately the same seam as VfxEvent, down to living on its own entity.
    // A system announces that a chest opened here; it never learns which clip
    // that was, how loud it came out, or whether anything was listening.
    //
    // The multiplayer property is the one that made VfxEvent look like this:
    // sound is derived from state a client already has, so in a networked build
    // these are produced locally rather than replicated. Losing one costs a
    // noise, not a kill. Keeping them off the authoritative queues means the
    // answer to "does this replicate" stays a property of which entity a thing
    // lives on, rather than a list somebody maintains.
    //
    // A second queue rather than adding a Sound kind to VfxEvent: the two are
    // budgeted differently and cut differently. Forty deaths in a frame want
    // forty little corpses and about three thuds, and that is a rule about
    // sound, not about drawing.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What made a noise.
    ///
    /// A small fixed vocabulary the simulation speaks, not a clip reference: a
    /// job cannot hold a managed asset, a byte replicates and an AudioClip does
    /// not, and which of four grunts plays is a question for the config rather
    /// than for the system that killed something.
    ///
    /// Values are indices into the config's table, so they may be appended but
    /// never reshuffled.
    /// </summary>
    public enum AudioCue : byte
    {
        None = 0,
        SkillCast = 1,
        ProjectileImpact = 2,
        Explosion = 3,
        ChainZap = 4,
        EnemyDeath = 5,
        ChestOpen = 6,
        ItemPickup = 7,
        UiClick = 8
    }

    /// <summary>Marks the entity holding the sound queue.</summary>
    public struct AudioEventsSingleton : IComponentData
    {
    }

    /// <summary>
    /// One thing worth hearing.
    ///
    /// Flat, like VfxEvent, and for the same reason: one queue with a field the
    /// odd cue ignores is cheaper than a buffer per kind of sound and a system
    /// that has to remember which one to write to.
    ///
    /// Whether a cue is spatial is NOT here. That belongs to the cue — a menu
    /// click has no position and never will — and putting it on the event would
    /// let two callers disagree about the same sound.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct AudioEvent : IBufferElementData
    {
        public AudioCue Cue;

        /// <summary>Where it happened. Ignored for cues the config marks flat.</summary>
        public float3 Position;

        /// <summary>
        /// Multiplier on the volume the config authored. One means "as
        /// written"; a bigger explosion may ask for more.
        /// </summary>
        public float Volume;

        /// <summary>
        /// Exact pitch, or zero to let the config roll one from its range.
        ///
        /// Zero rather than a bool because almost every caller wants the roll,
        /// and the ones that do not — a chain that climbs a step per jump — want
        /// to say a number, not to say "yes, and here it is".
        /// </summary>
        public float Pitch;
    }
}
