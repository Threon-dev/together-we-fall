using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Inventory
{
    // ─────────────────────────────────────────────────────────────────────
    // A container is an entity: the grid on it, the cells beside it. The
    // player's bag, a stash and a chest on the floor are the same three
    // components with different numbers, so nothing downstream needs to know
    // which kind it is looking at.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The shape of one container.
    ///
    /// Only the shape. What is in it lives in the cell buffer beside this, and
    /// what each item is lives on the item entity — a container knows how big it
    /// is and nothing else about its contents.
    /// </summary>
    public struct InventoryGridComponent : IComponentData
    {
        [GhostField]
        public int Width;
        [GhostField]
        public int Height;

        public int CellCount => Width * Height;

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>Row-major, matching the buffer layout: index = y * Width + x.</summary>
        public int IndexOf(int x, int y) => y * Width + x;
    }

    /// <summary>
    /// One cell of a container. Entity.Null means empty.
    ///
    /// A multi-cell item writes itself into every cell it covers, so the
    /// question "is this square free" is one array read rather than a search
    /// through a list of items and their sizes. The cost is that an item appears
    /// in the buffer several times, which is exactly why nothing counts items by
    /// counting cells.
    ///
    /// 64 by default covers the 12x5 bag without the buffer spilling out of the
    /// chunk. Containers are a handful of entities, so the 512 bytes this
    /// reserves per container is not a number worth economising on.
    /// </summary>
    [InternalBufferCapacity(64)]
    public struct InventoryCell : IBufferElementData
    {
        [GhostField]
        public Entity OccupyingItem;

        public bool IsFree => OccupyingItem == Entity.Null;
    }

    /// <summary>
    /// Where an item is, from the item's side.
    ///
    /// Deliberately redundant with the cell buffer: the cells answer "what is at
    /// this square", this answers "where is this item", and both questions are
    /// asked constantly. GridFit is the only thing that writes either, which is
    /// what keeps them from disagreeing.
    ///
    /// ContainerEntity is Entity.Null when the item is owned but not in a grid —
    /// equipped, most of the time.
    /// </summary>
    public struct ItemGridPlacement : IComponentData
    {
        [GhostField]
        public Entity ContainerEntity;
        [GhostField]
        public int OriginX;
        [GhostField]
        public int OriginY;
        [GhostField]
        public bool IsRotated;
    }

    /// <summary>
    /// "Somebody owns this item."
    ///
    /// The flag that keeps a carried item out of the loot pool. An item entity
    /// comes from the same pool as a world drop and goes back to it when
    /// destroyed; between those it is either lying on the floor
    /// (InteractableTag) or owned by a character (this). Free, to the pool,
    /// means neither.
    ///
    /// Enableable rather than a bool, so the pool finds free items with a query
    /// instead of anybody keeping a list that can drift.
    /// </summary>
    public struct ItemStored : IComponentData, IEnableableComponent
    {
    }

    /// <summary>What a placement request is asking for.</summary>
    public enum InventoryPlacementMode : byte
    {
        /// <summary>Put it exactly here. The drag-and-drop case.</summary>
        Exact = 0,

        /// <summary>Put it wherever it fits. The pickup case.</summary>
        Auto = 1,

        /// <summary>Take it out of whatever container it is in.</summary>
        Remove = 2
    }

    /// <summary>
    /// A client asking to move an item.
    ///
    /// The same request/result split as interaction and equipping, for the same
    /// reason: a client that wrote cells directly could put a two-by-three
    /// breastplate inside a one-by-one square, or into somebody else's bag. This
    /// says what it wants; the host decides whether that is true.
    ///
    /// The queue lives on the player character rather than in one global list,
    /// so one player's UI has no address at which to reach another player's bag.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct InventoryPlacementRequest : IBufferElementData
    {
        /// <summary>Where it should end up. Ignored for Remove.</summary>
        public Entity Container;

        public Entity Item;
        public InventoryPlacementMode Mode;

        /// <summary>Only read for Exact.</summary>
        public int OriginX;
        public int OriginY;

        /// <summary>
        /// Only honoured when the item's definition allows rotation. A client
        /// that asks to rotate a fixed item is refused rather than corrected,
        /// because silently placing it unrotated would put it somewhere the
        /// player did not point at.
        /// </summary>
        public bool Rotated;
    }

    /// <summary>How a placement request turned out.</summary>
    public enum InventoryPlacementStatus : byte
    {
        Placed = 0,
        Removed = 1,
        RejectedOutOfBounds = 2,
        RejectedOccupied = 3,
        RejectedNoRoom = 4,
        RejectedUnknownItem = 5,
        RejectedNoContainer = 6,
        RejectedCannotRotate = 7
    }

    /// <summary>
    /// What the host did about a request.
    ///
    /// Written even when the answer is no, because "no" is the interesting case
    /// for the UI: it is the difference between an item springing back to where
    /// it was and an item that quietly vanished. Cleared by whoever reads it.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct InventoryPlacementResult : IBufferElementData
    {
        public Entity Item;
        public InventoryPlacementStatus Status;

        /// <summary>Where it actually ended up. Only meaningful for Placed.</summary>
        public int OriginX;
        public int OriginY;
        public bool Rotated;

        public bool Succeeded =>
            Status == InventoryPlacementStatus.Placed || Status == InventoryPlacementStatus.Removed;
    }

    /// <summary>
    /// The container a character carries things in.
    ///
    /// A reference to a separate entity rather than the cells living on the
    /// character, so the bag, the stash and a chest on the floor are the same
    /// kind of thing. The day a stash exists it is another one of these, not a
    /// second implementation of the same idea.
    /// </summary>
    public struct CarriedBag : IComponentData
    {
        [GhostField]
        public Entity Container;
    }

    /// <summary>
    /// Who a container belongs to.
    ///
    /// The check that makes the request/result split mean something: a request
    /// naming a container is only honoured when that container is the requesting
    /// player's. A shared stash would be a container with no owner, and that is
    /// a deliberate conversation about write conflicts rather than something to
    /// fall into by leaving this off.
    /// </summary>
    public struct ContainerOwner : IComponentData
    {
        [GhostField]
        public int PlayerId;
    }

    /// <summary>
    /// How big a character's bag is, baked from CharacterConfig.
    ///
    /// On the same baked entity as CharacterBaseStats, because it is the same
    /// kind of number: authored balance, not a performance knob.
    /// </summary>
    public struct CharacterInventorySize : IComponentData
    {
        public int Width;
        public int Height;
    }
}
