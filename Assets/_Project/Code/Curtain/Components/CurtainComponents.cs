using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Curtain
{
    // ─────────────────────────────────────────────────────────────────────
    // The screen curtain: one number saying how covered the view is.
    //
    // The opacity is simulation state, not presentation state, and that is the
    // whole design. A fade is something other systems need to WAIT on — build
    // the next floor once the screen is black, hand control back once it is
    // clear — and a value owned by a MonoBehaviour is a value no system can
    // ask about without a bridge pointing the wrong way.
    //
    // So ECS advances the number and the presenter paints it. In a networked
    // build the reason a curtain is down is host state that replicates; the
    // curtain itself is not replicated at all, because every client can derive
    // it from that reason and each one draws its own screen.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why the screen is covered.
    ///
    /// Carried rather than inferred because the answer decides what happens
    /// once the screen is black, and that decision belongs to whoever asked
    /// rather than to whoever notices. It also makes a log line legible.
    /// </summary>
    public enum CurtainReason : byte
    {
        None = 0,

        /// <summary>Between floors, while the next one is generated.</summary>
        FloorTransition = 1,

        /// <summary>The run is over, one way or the other.</summary>
        RunEnd = 2,

        /// <summary>The local player went down.</summary>
        Death = 3,

        /// <summary>Somebody asked, for reasons of their own.</summary>
        Manual = 4
    }

    /// <summary>Marks the entity holding the curtain.</summary>
    public struct CurtainSingleton : IComponentData
    {
    }

    /// <summary>
    /// How covered the screen is, and where that is heading.
    ///
    /// Opacity is the truth and Target is the intent; a system that needs to
    /// know whether it may swap the world out reads IsCovered, and one that
    /// needs to know whether the player can see reads IsClear. Both are about
    /// where the fade IS, never about where it was told to go.
    /// </summary>
    public struct CurtainState : IComponentData
    {
        /// <summary>0 is clear, 1 is fully covered.</summary>
        public float Opacity;

        public float Target;

        /// <summary>Seconds a full sweep from clear to covered takes.</summary>
        public float Duration;

        public float4 Colour;

        public CurtainReason Reason;

        /// <summary>
        /// Deliberately not "Opacity == 1". A fade that stops a hundredth short
        /// of covered would leave a system waiting forever for a difference
        /// nobody can see.
        /// </summary>
        public bool IsCovered => Opacity >= 0.999f;

        public bool IsClear => Opacity <= 0.001f;

        public bool IsMoving => math.abs(Target - Opacity) > 0.001f;
    }

    /// <summary>
    /// Somebody asking for the screen to be covered or cleared.
    ///
    /// The same request queue as everywhere else, and for a reason particular
    /// to this one: two systems can want the screen for different things in the
    /// same frame — a floor transition and a death, say — and a queue makes the
    /// outcome of that a rule instead of a race between whoever wrote last.
    ///
    /// The rule is that the last request in the buffer stands, because a
    /// curtain is a level rather than an event: the newest intent is the
    /// current intent, and there is nothing sensible to do with the older one.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct CurtainRequest : IBufferElementData
    {
        public float Target;

        /// <summary>Seconds for a full sweep. Zero means use the standing one.</summary>
        public float Duration;

        /// <summary>Zero alpha means keep the colour already in use.</summary>
        public float4 Colour;

        public CurtainReason Reason;
    }
}
