using Unity.Entities;

namespace TogetherWeFall.Equipment
{
    // ─────────────────────────────────────────────────────────────────────
    // Sockets: holes in a piece of gear, and what is sitting in them.
    //
    // The whole point of the model is that a skill is not a property of a
    // character. It is an object a player found, which lives in a hole in
    // another object they found, and which behaves differently depending on
    // what is in the holes next to it. Move the gem to another weapon and it
    // takes its behaviour with it; move it next to different supports and the
    // behaviour changes without a line of code.
    //
    // Sockets therefore live on the ITEM entity, not on the character. An item
    // taken off keeps its gems — they are simply out of reach until it is worn
    // again, because a socket only feeds the skill bar while its gear is
    // equipped.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What sort of gem an item is, if it is one at all.
    ///
    /// On the item rather than on a separate asset type: a gem is a thing that
    /// lies in the bag, is dropped by chests, occupies cells and is lost on
    /// death, exactly like a sword. Making it a different kind of asset would
    /// mean a second copy of all of that.
    /// </summary>
    public enum GemKind : byte
    {
        /// <summary>Not a gem. Every ordinary item.</summary>
        None = 0,

        /// <summary>Casts something. Only these can be bound to the skill bar.</summary>
        Active = 1,

        /// <summary>Changes the actives linked to it. Never casts on its own.</summary>
        Support = 2
    }

    /// <summary>
    /// One socket on one piece of gear.
    ///
    /// LinkGroup is the entire mechanic: supports affect the actives sharing
    /// their group and nothing else. Two sockets in the same group are what PoE
    /// draws as a link, and a group with several actives in it has every support
    /// apply to all of them — which is the model from the start rather than a
    /// later special case, because it costs nothing here and rewrites the fold
    /// if it arrives later.
    ///
    /// The layout is authored on the item and fixed. Rolling sockets and links
    /// per drop is a whole feature of its own and sits on top of this one.
    /// </summary>
    [InternalBufferCapacity(6)]
    public struct GearSocket : IBufferElementData
    {
        public int SocketIndex;
        public int LinkGroup;

        /// <summary>The gem sitting in it, or Entity.Null.</summary>
        public Entity InsertedGem;

        public bool IsEmpty => InsertedGem == Entity.Null;
    }

    /// <summary>What a socket request is asking for.</summary>
    public enum SocketRequestKind : byte
    {
        /// <summary>Take a gem out of the bag and put it in this socket.</summary>
        Insert = 0,

        /// <summary>Take whatever is in this socket back into the bag.</summary>
        Remove = 1,

        /// <summary>Point a skill bar slot at this socket.</summary>
        BindBar = 2,

        /// <summary>Empty a skill bar slot.</summary>
        ClearBar = 3
    }

    /// <summary>
    /// A client asking to change what is socketed, or what the bar points at.
    ///
    /// The same request/result split as equipping, and one queue for all four
    /// kinds for the same reason the equipment queue has four: inserting a gem
    /// is a removal from the grid and a write to a socket, and the two halves
    /// must not be able to happen apart.
    ///
    /// Socketing is not a combat action and is deliberately allowed at any time,
    /// in a dungeon or out of one, exactly as in PoE. Nothing here checks a
    /// phase.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct SocketRequest : IBufferElementData
    {
        public SocketRequestKind Kind;

        /// <summary>The gear whose socket this is about. Ignored by ClearBar.</summary>
        public Entity Gear;

        /// <summary>The gem to insert. Only read by Insert.</summary>
        public Entity Gem;

        public int SocketIndex;

        /// <summary>Which hotkey. Only read by BindBar and ClearBar.</summary>
        public int BarSlotIndex;
    }

    /// <summary>How a socket request turned out.</summary>
    public enum SocketStatus : byte
    {
        Inserted = 0,
        Removed = 1,
        Bound = 2,
        Cleared = 3,

        RejectedNoSuchSocket = 4,
        RejectedSocketFull = 5,
        RejectedSocketEmpty = 6,
        RejectedNotAGem = 7,
        RejectedNotCarried = 8,
        RejectedBagFull = 9,

        /// <summary>A support gem was dragged onto the skill bar.</summary>
        RejectedNotActive = 10,

        RejectedNoSuchBarSlot = 11
    }

    /// <summary>
    /// What the host did about a socket request.
    ///
    /// Written even when the answer is no, for the same reason the placement and
    /// equipment results are: a refusal leaves the world untouched, so without
    /// this the player would watch a gem spring back and be told nothing.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct SocketResult : IBufferElementData
    {
        public Entity Gear;
        public Entity Gem;
        public int SocketIndex;
        public SocketStatus Status;

        public bool Succeeded => Status <= SocketStatus.Cleared;
    }
}
