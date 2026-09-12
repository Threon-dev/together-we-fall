using Unity.Collections;
using Unity.Entities;

namespace TogetherWeFall.Lobby
{
    // ─────────────────────────────────────────────────────────────────────
    // The lobby is a place, not a screen.
    //
    // Nothing here is a new kind of entity. An NPC is an interactable with a
    // radius — the same two components a chest has — plus one byte saying what
    // talking to it means. The resolver that already decides who reached what
    // needed no change at all, which is the whole reason the lobby cost this
    // little: "walk up and press E" was solved the day chests existed.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What an NPC is for.
    ///
    /// A byte on one component rather than a component per kind, because the
    /// only thing that ever asks is the UI deciding which panel to open. A
    /// marker per kind would mean a query per kind in the one place that must
    /// handle all of them together.
    /// </summary>
    public enum NpcServiceType : byte
    {
        None = 0,
        Vendor = 1,
        Crafting = 2,

        /// <summary>Stands beside the dummies. Talking to it opens the meter.</summary>
        TrainingGround = 3,

        DungeonPortal = 4
    }

    /// <summary>An NPC, and what it does. Beside InteractableTag, never instead of it.</summary>
    public struct NpcService : IComponentData
    {
        public NpcServiceType Type;
    }

    /// <summary>
    /// "This player just opened this NPC's service."
    ///
    /// An event queue on the character rather than a stored "currently talking
    /// to" component, because which window is open is client state, exactly
    /// like whether the inventory panel is showing. The host decides that a
    /// player reached a vendor; what the client then draws, and for how long, is
    /// nobody else's business — and in coop that means one player browsing a
    /// shop cannot block another from the same NPC.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct NpcSessionOpened : IBufferElementData
    {
        public Entity Npc;
        public NpcServiceType Type;
    }

    /// <summary>
    /// The way out of the lobby. Baked onto the portal NPC.
    ///
    /// The ready list lives on this entity rather than on a lobby singleton: the
    /// portal IS the thing being agreed about, and a singleton would be a second
    /// entity that has to be created, found and kept in step with the one NPC
    /// that gives it meaning.
    /// </summary>
    public struct DungeonPortal : IComponentData
    {
        /// <summary>The scene to load once everybody has agreed.</summary>
        public FixedString32Bytes TargetScene;
    }

    /// <summary>A player who has said they are ready to descend.</summary>
    [InternalBufferCapacity(4)]
    public struct LobbyReadyPlayer : IBufferElementData
    {
        public int PlayerId;
    }

    /// <summary>
    /// A client toggling its own readiness.
    ///
    /// A request rather than a direct write for the usual reason, plus one of
    /// its own: a client that wrote the ready list could mark somebody else
    /// ready and drag the team into a run they had not agreed to.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PortalReadyRequest : IBufferElementData
    {
        public int PlayerId;
        public bool Ready;
    }

    /// <summary>How far along leaving this scene is.</summary>
    public enum SceneTransitionPhase : byte
    {
        Idle = 0,

        /// <summary>Everyone agreed; the curtain is coming down.</summary>
        Fading = 1,

        /// <summary>The screen is black. Whoever can load a scene may do it now.</summary>
        Ready = 2
    }

    /// <summary>
    /// The simulation's half of a scene change.
    ///
    /// The same seam as the curtain, and for the same reason: the decision to
    /// leave, and the moment it is safe to leave, are both simulation state that
    /// a headless host reaches at the same time as a client. Only the last step
    /// — the call that actually swaps scenes — needs a MonoBehaviour, and that
    /// is all SceneLoadBridge does.
    /// </summary>
    public struct SceneTransition : IComponentData
    {
        public FixedString32Bytes Target;
        public SceneTransitionPhase Phase;
    }
}
