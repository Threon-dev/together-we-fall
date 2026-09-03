using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Interaction.Systems;
using TogetherWeFall.Player;

namespace TogetherWeFall.Loot.Systems
{
    /// <summary>
    /// Takes an item off the floor and puts it in the inventory of whoever
    /// picked it up.
    ///
    /// That inventory is a buffer on the player entity — the place the flat
    /// collected-loot list was always going to give way to. What this system
    /// does has not changed: it writes "this player now owns this item" and
    /// destroys what was lying on the ground. Where it writes it has.
    ///
    /// Picking up is not a special case of interaction, it is the same case. The
    /// resolver already decided who reached what and settled any race over it;
    /// all that is left here is what it means for an item.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InteractionResolveSystem))]
    public partial struct ItemPickupSystem : ISystem
    {
        private EntityQuery _playerQuery;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, InventoryItem>()
                .Build();

            state.RequireForUpdate<ItemInstance>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var picked = new NativeList<PickedItem>(4, Allocator.Temp);

            foreach ((RefRO<ItemInstance> item, RefRO<ItemDisplayName> name,
                      RefRO<InteractionTriggered> trigger, Entity entity) in
                     SystemAPI.Query<RefRO<ItemInstance>, RefRO<ItemDisplayName>,
                         RefRO<InteractionTriggered>>()
                         .WithEntityAccess())
            {
                picked.Add(new PickedItem
                {
                    Entity = entity,
                    PlayerId = trigger.ValueRO.ByPlayerId,
                    Item = item.ValueRO,
                    Name = name.ValueRO.Value
                });
            }

            if (picked.Length == 0)
                return;

            using var taken = new NativeList<Entity>(picked.Length, Allocator.Temp);

            Store(ref state, picked, taken);

            // Returned to the pool rather than destroyed. Not a structural
            // change any more, but still done last: the item is only free once
            // it is certainly recorded.
            if (taken.Length > 0)
                LootItemPool.Release(state.EntityManager, taken.AsArray());
        }

        private void Store(
            ref SystemState state, NativeList<PickedItem> picked, NativeList<Entity> taken)
        {
            using NativeArray<Entity> playerEntities = _playerQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> players =
                _playerQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < picked.Length; i++)
            {
                PickedItem entry = picked[i];
                int player = IndexOfPlayer(players, entry.PlayerId);

                if (player < 0)
                {
                    // No character to put it in. Release the claim so the item
                    // stays on the floor and can be picked up again, rather than
                    // disappearing into nothing.
                    state.EntityManager.SetComponentEnabled<InteractionTriggered>(
                        entry.Entity, false);

                    UnityEngine.Debug.LogWarning(
                        $"[ItemPickupSystem] No character for player {entry.PlayerId} — " +
                        $"{entry.Name} was left on the floor.");
                    continue;
                }

                state.EntityManager.GetBuffer<InventoryItem>(playerEntities[player])
                    .Add(new InventoryItem
                    {
                        ItemId = entry.Item.ItemId,
                        Rarity = entry.Item.Rarity,

                        // Carried through unchanged: an item does not become safe
                        // by being picked up, it becomes safe by being extracted
                        // with.
                        RiskState = entry.Item.RiskState
                    });

                taken.Add(entry.Entity);

                UnityEngine.Debug.Log(
                    $"[ItemPickupSystem] Player {entry.PlayerId} picked up " +
                    $"{entry.Name} ({entry.Item.Rarity}).");
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
            public ItemInstance Item;
            public FixedString64Bytes Name;
        }
    }
}
