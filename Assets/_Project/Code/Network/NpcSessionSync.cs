using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using TogetherWeFall.Lobby;
using TogetherWeFall.Lobby.Systems;
using TogetherWeFall.Player;

namespace TogetherWeFall.Network
{
    /// <summary>"You just walked up to this kind of NPC", to one player.</summary>
    public struct NpcSessionRpc : IRpcCommand
    {
        public NpcServiceType Type;
    }

    /// <summary>
    /// Host: tells each player about the sessions NpcInteractionSystem opened
    /// for them this tick, and empties the host's queue.
    ///
    /// The type travels, not the entity: NPCs are SubScene entities that exist
    /// in both worlds under different ids, and the client finds its own copy.
    /// The queue is emptied here because its old reader, LobbyUI, now reads the
    /// client world.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NpcInteractionSystem))]
    public partial struct NpcSessionForwardSystem : ISystem
    {
        private EntityQuery _connectionQuery;

        public void OnCreate(ref SystemState state)
        {
            _connectionQuery = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build();
            state.RequireForUpdate<NpcService>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using NativeArray<Entity> connections = _connectionQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<NetworkId> ids = _connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<PlayerCharacter> character, DynamicBuffer<NpcSessionOpened> sessions) in
                     SystemAPI.Query<RefRO<PlayerCharacter>, DynamicBuffer<NpcSessionOpened>>())
            {
                if (sessions.Length == 0)
                    continue;

                int index = IndexOf(ids, character.ValueRO.PlayerId);

                if (index >= 0)
                {
                    Entity rpc = commands.CreateEntity();
                    commands.AddComponent(rpc, new NpcSessionRpc { Type = sessions[sessions.Length - 1].Type });
                    commands.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connections[index] });
                }

                sessions.Clear();
            }

            commands.Playback(state.EntityManager);
        }

        private static int IndexOf(NativeArray<NetworkId> ids, int playerId)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i].Value == playerId)
                    return i;
            }

            return -1;
        }
    }

    /// <summary>
    /// Client: opens the session on the local character against the local copy
    /// of that NPC, which is what the unchanged LobbyUI drains.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct NpcSessionReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<NpcSessionRpc, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            // Every player's character is replicated here, and the session is
            // this screen's alone.
            int localPlayerId = SystemAPI.TryGetSingleton(out NetworkId id) ? id.Value : -1;

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<NpcSessionRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<NpcSessionRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                NpcServiceType type = rpc.ValueRO.Type;
                Entity npc = Entity.Null;

                foreach ((RefRO<NpcService> service, Entity candidate) in
                         SystemAPI.Query<RefRO<NpcService>>().WithEntityAccess())
                {
                    if (service.ValueRO.Type == type)
                    {
                        npc = candidate;
                        break;
                    }
                }

                if (npc != Entity.Null)
                {
                    foreach ((RefRO<PlayerCharacter> character, DynamicBuffer<NpcSessionOpened> sessions) in
                             SystemAPI.Query<RefRO<PlayerCharacter>, DynamicBuffer<NpcSessionOpened>>())
                    {
                        if (character.ValueRO.PlayerId == localPlayerId)
                            sessions.Add(new NpcSessionOpened { Npc = npc, Type = type });
                    }
                }

                commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);
        }
    }
}
