using Unity.Entities;

namespace TogetherWeFall.Lobby
{
    /// <summary>
    /// What a crafting station can do to an item.
    ///
    /// All three change something that is already per-instance: the socket
    /// buffer lives on the item entity and is rolled when the item is handed out
    /// of the pool. That is the whole list of what crafting CAN touch today —
    /// affixes are authored on the definition, so two copies of an item are
    /// identical and there is nothing on an instance to reroll. Rerolling
    /// affixes is one field on ItemInstance away, and it is not this feature.
    /// </summary>
    public enum CraftOperation : byte
    {
        /// <summary>One more hole, joined to the last one's link group.</summary>
        AddSocket = 0,

        /// <summary>Pull a socket into the group of the socket before it.</summary>
        LinkSocket = 1,

        /// <summary>Roll this weapon's built-in attacks again. Gems stay put.</summary>
        RerollSkills = 2
    }

    /// <summary>
    /// The forge NPC, and its price list.
    ///
    /// Prices on the station rather than in a recipe database, because all three
    /// operations above take one item and some currency and produce nothing new.
    /// A recipe asset earns its keep the day an operation consumes a second item;
    /// until then it would be a ScriptableObject with an empty ingredients array
    /// and a baker to match.
    /// </summary>
    public struct CraftingStation : IComponentData
    {
        public int AddSocketCost;
        public int LinkSocketCost;
        public int RerollSkillsCost;

        public int CostOf(CraftOperation operation)
        {
            switch (operation)
            {
                case CraftOperation.AddSocket: return AddSocketCost;
                case CraftOperation.LinkSocket: return LinkSocketCost;
                case CraftOperation.RerollSkills: return RerollSkillsCost;
                default: return int.MaxValue;
            }
        }
    }

    /// <summary>A client asking for one operation on one item it is carrying.</summary>
    [InternalBufferCapacity(2)]
    public struct CraftRequest : IBufferElementData
    {
        public Entity Station;
        public Entity Item;
        public CraftOperation Operation;

        /// <summary>Which hole to pull into its neighbour's group. Only read by LinkSocket.</summary>
        public int SocketIndex;
    }

    public enum CraftStatus : byte
    {
        Crafted = 0,

        RejectedNoStation = 1,

        /// <summary>The item is not in the requesting player's own bag.</summary>
        RejectedNotCarried = 2,

        RejectedTooPoor = 3,
        RejectedNoSuchSocket = 4,

        /// <summary>Sixteen holes is the ceiling the item database bakes against.</summary>
        RejectedSocketLimit = 5,

        /// <summary>Nothing welded in, so nothing to roll again.</summary>
        RejectedNothingToReroll = 6,

        /// <summary>That socket is already in its neighbour's group.</summary>
        RejectedAlreadyLinked = 7
    }

    /// <summary>What the host did about a craft. Written even when the answer is no.</summary>
    [InternalBufferCapacity(2)]
    public struct CraftResult : IBufferElementData
    {
        public Entity Item;
        public CraftOperation Operation;
        public CraftStatus Status;
        public int Price;

        public bool Succeeded => Status == CraftStatus.Crafted;
    }
}
