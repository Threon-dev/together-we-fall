using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Vfx
{
    // ─────────────────────────────────────────────────────────────────────
    // PRESENTATION — things that happened, for something to draw.
    //
    // The one seam this whole part of the project hangs on. Simulation systems
    // announce what happened and never learn what it looked like; whatever is
    // drawing reads the queue and decides. Today that is pooled line renderers
    // and a camera that shakes. When VFX Graph arrives, one file changes and no
    // system that produces these events is touched.
    //
    // It is also the boundary a networked build needs: these are derived from
    // state a client already has, so they are generated locally rather than
    // replicated. Nothing here is authoritative — losing an event costs a
    // flash, not a kill.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>What happened. Deliberately few and deliberately generic.</summary>
    public enum VfxEventKind : byte
    {
        /// <summary>One jump of a chain, from one body to the next.</summary>
        ChainLink = 0,

        /// <summary>Something went off with a radius.</summary>
        Explosion = 1,

        /// <summary>An enemy died.</summary>
        Death = 2,

        /// <summary>
        /// The opening stroke, from the caster to whatever the bolt found.
        /// Its own kind rather than another link because it is the one part of
        /// a chain the player is responsible for aiming, and it reads better
        /// heavier than the jumps it sets off.
        /// </summary>
        BoltStrike = 3,

        /// <summary>How much something was just hurt for.</summary>
        DamageNumber = 4,

        /// <summary>
        /// Two elements met: a reaction fired, or a projectile picked one up.
        ///
        /// Its own kind rather than a small Explosion, and the reason is the
        /// punctuation rather than the picture. An explosion shakes the camera
        /// and stops time, which is right for one blast and catastrophic for
        /// something that happens on ordinary hits — shooting a burning crowd
        /// would leave hit-stop permanently engaged, which is precisely the
        /// constant hum the refusal rules exist to prevent. This one draws and
        /// nothing else.
        /// </summary>
        ElementBurst = 5
    }

    /// <summary>Marks the entity holding the presentation queue.</summary>
    public struct VfxEventsSingleton : IComponentData
    {
    }

    /// <summary>
    /// One thing worth drawing.
    ///
    /// Flat and untyped-by-kind on purpose: a chain link needs two points, an
    /// explosion needs a radius, a death needs neither, and giving each its own
    /// buffer would mean three queues to drain and three systems to remember to
    /// write to. One queue with a couple of unused fields is the cheaper trade.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct VfxEvent : IBufferElementData
    {
        public VfxEventKind Kind;

        public float3 Position;

        /// <summary>The far end of a chain link. Unused by everything else.</summary>
        public float3 EndPosition;

        /// <summary>Element colour, so a fire burst does not look like a frost one.</summary>
        public float4 Color;

        /// <summary>Radius for an explosion, damage for a number, strength otherwise.</summary>
        public float Magnitude;

        /// <summary>
        /// Worth making bigger. Only damage numbers read it, where it means the
        /// blow was the killing one — a flat field rather than a second event
        /// kind, because it changes how the same thing is drawn, not what it is.
        /// </summary>
        public bool Emphasis;
    }
}
