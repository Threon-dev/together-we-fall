using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Interaction
{
    // ─────────────────────────────────────────────────────────────────────
    // The request/result boundary, drawn explicitly.
    //
    // A client never acts on the world. It says "I pressed the button, and I
    // was standing here"; the host decides what, if anything, that touched.
    // Even in a single process the two halves are separate types and separate
    // systems, because the moment they are one thing, a client is deciding
    // what it opened — and that is the decision an authoritative host exists
    // to make.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Marks the entity holding the interaction request queue.</summary>
    public struct InteractionRequestsSingleton : IComponentData
    {
    }

    /// <summary>
    /// One player asking to interact, this frame.
    ///
    /// Deliberately carries no target. A client that named its target could name
    /// a chest across the map; sending only a position means the host resolves
    /// what was in reach, using its own view of the world.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct InteractionRequest : IBufferElementData
    {
        public int PlayerId;
        public float3 Position;
    }

    /// <summary>
    /// "This can be interacted with right now."
    ///
    /// Enableable, and that is the whole trick: an opened chest and a picked-up
    /// item stop being targets by disabling their own tag, so the resolver never
    /// needs to know what kinds of interactable exist or how each one decides it
    /// is spent.
    /// </summary>
    public struct InteractableTag : IComponentData, IEnableableComponent
    {
    }

    /// <summary>How close a player must be. Lives on the target, not the player.</summary>
    public struct InteractionRadius : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// The result half: the host decided this player interacts with this entity.
    ///
    /// Enableable so a claim costs no structural change, and consumed by
    /// whichever system owns that kind of interactable. One resolver decides WHO
    /// touched WHAT; what it means is never its business.
    /// </summary>
    public struct InteractionTriggered : IComponentData, IEnableableComponent
    {
        public int ByPlayerId;
    }
}
