using Unity.Entities;
using TogetherWeFall.Lobby;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// Client: points the local vendor at the shelf ghost.
    ///
    /// The vendor is a SubScene entity in both worlds, but only the host's copy
    /// ever gets a StockContainer — VendorStockSystem runs there. LobbyUI reads
    /// the shelf through the vendor, unchanged, so this fills the client's copy
    /// in with the replicated shelf.
    ///
    /// ponytail: one vendor, one shelf. A second vendor needs the shelf to carry
    /// which vendor it belongs to.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct VendorShelfLinkSystem : ISystem
    {
        private EntityQuery _shelfQuery;

        public void OnCreate(ref SystemState state)
        {
            _shelfQuery = SystemAPI.QueryBuilder().WithAll<VendorShelf>().Build();

            state.RequireForUpdate<VendorComponent>();
            state.RequireForUpdate(_shelfQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            using Unity.Collections.NativeArray<Entity> shelves =
                _shelfQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

            Entity shelf = shelves[shelves.Length - 1];

            foreach (RefRW<VendorComponent> vendor in SystemAPI.Query<RefRW<VendorComponent>>())
            {
                if (vendor.ValueRO.StockContainer != shelf)
                    vendor.ValueRW.StockContainer = shelf;
            }
        }
    }
}
