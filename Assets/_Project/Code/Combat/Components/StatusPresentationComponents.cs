using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Combat
{
    // ─────────────────────────────────────────────────────────────────────
    // PRESENTATION — what to show about what is afflicting this entity.
    //
    // Split from StatusGate rather than sharing one component, on the same
    // boundary the enemy components are split on: the gate is what the
    // simulation decides with, this is what a client would need to draw a
    // frame. Both are written by the same pass over the same buffer, because
    // walking it twice to keep two files tidy would be the wrong trade — but
    // when [GhostField] goes on, the line has to be visible at a glance.
    //
    // A level rather than a queue of events, which is the same choice the
    // curtain made and for the same reason. A status is on for four seconds;
    // announcing it as an event would mean the presenter tracking how long ago
    // each one arrived, which is state the buffer already holds.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What is visibly on this body.
    ///
    /// One mask and one colour, both reduced from the buffer once a frame. The
    /// presenter never sees a status, only the answer — so a hundred burning
    /// enemies cost a hundred bit tests rather than a hundred buffer reads
    /// across the managed boundary.
    /// </summary>
    public struct StatusVisual : IComponentData
    {
        /// <summary>Every status on this body, as a StatusMask. Zero is a clean one.</summary>
        public ushort Icons;

        /// <summary>
        /// The colour to wash the body in, decided by the loudest status on it.
        ///
        /// One colour rather than a blend: two debuffs averaged give a third
        /// colour that means neither of them, and the thing the player needs to
        /// read off a crowd at a glance is "that one cannot move", not the exact
        /// composition of what it is suffering.
        /// </summary>
        public float4 Tint;

        /// <summary>Whether there is anything to tint with. False restores the body colour.</summary>
        public bool HasTint;

        /// <summary>
        /// How loud the status the tint came from was, so the pass that reduces
        /// the buffer can tell whether the next entry outranks it.
        ///
        /// Written where the tint is and read nowhere else. Kept on the
        /// component rather than in a local because the reduce runs one entry at
        /// a time and has nowhere else to remember it — and because it is the
        /// honest answer to "why is this body yellow": something outranked
        /// everything else on it.
        /// </summary>
        public byte TintPriority;
    }
}
