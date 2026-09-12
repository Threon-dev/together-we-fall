using Unity.Entities;

namespace TogetherWeFall.Lobby
{
    /// <summary>
    /// A shop.
    ///
    /// Its stock is an ordinary container entity — the same InventoryGridComponent
    /// and cell buffer the player's bag is made of, with different numbers. That
    /// is the entire reason a vendor cost one system instead of a subsystem:
    /// "is there room for this sword" and "what is in this square" were already
    /// answered, once, by GridFit.
    ///
    /// What the stock container deliberately does NOT have is a ContainerOwner.
    /// Without one, InventoryPlacementSystem refuses every request naming it, so
    /// a client cannot drag itself a free sword; the only thing that may move an
    /// item in or out is VendorTransactionSystem, which charges for it.
    /// </summary>
    public struct VendorComponent : IComponentData
    {
        /// <summary>Null until VendorStockSystem has built and filled it.</summary>
        public Entity StockContainer;

        public int StockWidth;
        public int StockHeight;

        /// <summary>What the player pays, against the item's base price.</summary>
        public float BuyPriceMultiplier;

        /// <summary>What the player receives. Below one, or selling back is free money.</summary>
        public float SellPriceMultiplier;
    }

    /// <summary>
    /// One line of a vendor's fixed stock list, baked from the authoring asset.
    ///
    /// Ids rather than entities, because the entities do not exist at bake time:
    /// stock comes out of the same pooled item entities as a floor drop, and the
    /// pool is not built until the loot systems start. The consequence is worth
    /// stating plainly — a vendor with ten items occupies ten of the pool's
    /// slots for the whole session.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct VendorStockEntry : IBufferElementData
    {
        public int ItemId;
    }

    public enum VendorTransactionKind : byte
    {
        Buy = 0,
        Sell = 1
    }

    /// <summary>
    /// A client asking to trade. The same request/result split as everything
    /// else a client asks for, and here it is load-bearing twice over: the
    /// client names an item but never a price, and it names a container but
    /// never writes to one.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct VendorTransactionRequest : IBufferElementData
    {
        public Entity Vendor;
        public Entity Item;
        public VendorTransactionKind Kind;
    }

    public enum VendorTransactionStatus : byte
    {
        Bought = 0,
        Sold = 1,

        RejectedNoVendor = 2,
        RejectedUnknownItem = 3,

        /// <summary>Buying something that is not in this vendor's stock.</summary>
        RejectedNotStocked = 4,

        /// <summary>Selling something that is not in the seller's own bag.</summary>
        RejectedNotCarried = 5,

        RejectedTooPoor = 6,

        /// <summary>Nowhere to put the goods — the buyer's bag or the vendor's shelf.</summary>
        RejectedNoRoom = 7,

        /// <summary>The pool had no spare entities to pay out coins with.</summary>
        RejectedNoChange = 8,

        /// <summary>Currency is not merchandise.</summary>
        RejectedNotForSale = 9
    }

    /// <summary>
    /// What the host did about a trade.
    ///
    /// Carries the price because a refusal is the case worth reporting and
    /// "you cannot afford this" is only meaningful next to a number.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct VendorTransactionResult : IBufferElementData
    {
        public Entity Item;
        public VendorTransactionStatus Status;
        public int Price;

        public bool Succeeded =>
            Status == VendorTransactionStatus.Bought || Status == VendorTransactionStatus.Sold;
    }
}
