using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Interaction;
using TogetherWeFall.Interaction.Systems;
using TogetherWeFall.Player;

namespace TogetherWeFall.Lobby.Systems
{
    /// <summary>
    /// Turns "this player reached that NPC" into "this player opened that
    /// service".
    ///
    /// The same shape as ChestInteractionSystem and ItemPickupSystem: the
    /// resolver already decided who touched what, and this is the system that
    /// says what touching an NPC means. Which is almost nothing — it announces
    /// the session on the character and stops. Everything a shop or a forge
    /// actually does arrives later, as a request, from the panel the player is
    /// now looking at.
    ///
    /// Unlike a chest, an NPC does not stop being interactable when it is used.
    /// There is no race to win: two players can be in the same shop, because a
    /// vendor transaction only ever moves items between the vendor shelf and the
    /// bag of whoever asked.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InteractionResolveSystem))]
    public partial struct NpcInteractionSystem : ISystem
    {
        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter, NpcSessionOpened>()
                .Build();

            state.RequireForUpdate<NpcService>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var claims = new NativeList<NpcClaim>(2, Allocator.Temp);

            foreach ((RefRO<NpcService> service, RefRO<InteractionTriggered> trigger, Entity entity)
                     in SystemAPI.Query<RefRO<NpcService>, RefRO<InteractionTriggered>>()
                         .WithEntityAccess())
            {
                claims.Add(new NpcClaim
                {
                    Npc = entity,
                    PlayerId = trigger.ValueRO.ByPlayerId,
                    Type = service.ValueRO.Type
                });
            }

            if (claims.Length == 0)
                return;

            // Collected before anything is written, because the loop below
            // switches off the very component the query above filters on.
            Announce(ref state, claims);
        }

        private void Announce(ref SystemState state, NativeList<NpcClaim> claims)
        {
            using NativeArray<Entity> characters = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> players =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < claims.Length; i++)
            {
                NpcClaim claim = claims[i];

                // Spent whatever comes of it. A press that found nobody to tell
                // is a press that missed, not one that waits for a character to
                // be registered.
                state.EntityManager.SetComponentEnabled<InteractionTriggered>(claim.Npc, false);

                int index = IndexOfPlayer(players, claim.PlayerId);
                if (index < 0)
                    continue;

                state.EntityManager
                    .GetBuffer<NpcSessionOpened>(characters[index])
                    .Add(new NpcSessionOpened { Npc = claim.Npc, Type = claim.Type });
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

        private struct NpcClaim
        {
            public Entity Npc;
            public int PlayerId;
            public NpcServiceType Type;
        }
    }
}
