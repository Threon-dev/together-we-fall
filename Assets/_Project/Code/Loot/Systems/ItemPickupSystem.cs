using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Interaction.Systems;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Player;

namespace TogetherWeFall.Loot.Systems
{
    /// <summary>
    /// Turns "this player reached that item" into a request to put it in their
    /// bag.
    ///
    /// It no longer stores anything itself. With a grid inventory there is a
    /// question this system is in no position to answer — whether the item
    /// actually fits — and answering it here would mean a second copy of the
    /// placement rules. So it asks, exactly the way the UI asks when a player
    /// drags something, and InventoryPlacementSystem gives the same answer to
    /// both.
    ///
    /// The item is deliberately left lying on the floor. Taking it off the floor
    /// is what a successful placement means, and doing it here would produce the
    /// one outcome nobody wants: an item that vanished because the bag was full.
    /// A full bag leaves it where it is, which is what Diablo and PoE both do,
    /// and the player can press the key again after making room.
    ///
    /// Picking up is not a special case of interaction, it is the same case. The
    /// resolver already decided who reached what and settled any race over it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InteractionResolveSystem))]
    public partial struct ItemPickupSystem : ISystem
    {
        private EntityQuery _playerQuery;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, CarriedBag, InventoryPlacementRequest>()
                .Build();

            state.RequireForUpdate<ItemInstance>();
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var picked = new NativeList<PickedItem>(4, Allocator.Temp);

            foreach ((RefRO<ItemDisplayName> name, RefRO<InteractionTriggered> trigger,
                      Entity entity) in
                     SystemAPI.Query<RefRO<ItemDisplayName>, RefRO<InteractionTriggered>>()
                         .WithAll<ItemInstance>()
                         .WithEntityAccess())
            {
                picked.Add(new PickedItem
                {
                    Entity = entity,
                    PlayerId = trigger.ValueRO.ByPlayerId,
                    Name = name.ValueRO.Value
                });
            }

            if (picked.Length == 0)
                return;

            Request(ref state, picked);
        }

        private void Request(ref SystemState state, NativeList<PickedItem> picked)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();

            using NativeArray<Entity> playerEntities = _playerQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> players =
                _playerQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);
            using NativeArray<CarriedBag> bags =
                _playerQuery.ToComponentDataArray<CarriedBag>(Allocator.Temp);

            for (int i = 0; i < picked.Length; i++)
            {
                PickedItem entry = picked[i];

                // The claim is consumed either way. Leaving it raised would have
                // the resolver hand the same item over again next frame, and a
                // refused placement would retry forever.
                state.EntityManager.SetComponentEnabled<InteractionTriggered>(
                    entry.Entity, false);

                int player = IndexOfPlayer(players, entry.PlayerId);

                if (player < 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[ItemPickupSystem] No character for player {entry.PlayerId} — " +
                        $"{entry.Name} was left on the floor.");
                    continue;
                }

                // Money never reaches the bag. A coin is an item for exactly as
                // long as it is lying on the floor; picking it up adds its value
                // to the purse and hands the entity back to the pool, so a
                // floor's worth of payout costs no squares and no placement
                // request at all.
                //
                // Asked before the request below rather than inside the
                // placement stage, because "this is money" is a fact about the
                // item and the grid has no business knowing it.
                if (Currency.TryCollect(
                        state.EntityManager, items, playerEntities[player], entry.Entity))
                {
                    continue;
                }

                state.EntityManager
                    .GetBuffer<InventoryPlacementRequest>(playerEntities[player])
                    .Add(new InventoryPlacementRequest
                    {
                        Container = bags[player].Container,
                        Item = entry.Entity,

                        // Auto rather than a coordinate: the player pressed a key
                        // next to something on the ground, they did not point at
                        // a square.
                        Mode = InventoryPlacementMode.Auto
                    });
            }
        }

        private static int IndexOfPlayer(in NativeArray<PlayerCharacter> players, int playerId)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].PlayerId == playerId)
                    return i;
            }

            return -1;
        }

        private struct PickedItem
        {
            public Entity Entity;
            public int PlayerId;
            public FixedString64Bytes Name;
        }
    }
}
