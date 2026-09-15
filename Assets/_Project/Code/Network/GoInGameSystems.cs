using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// A client saying "I am loaded and ready for snapshots".
    ///
    /// Netcode sends no ghosts to a connection until it is marked in game on
    /// both ends. The client marks its side when it has a NetworkId and asks
    /// the server to mark the other.
    /// </summary>
    public struct GoInGameRequest : IRpcCommand
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct GoInGameClientSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<NetworkId>()
                .WithNone<NetworkStreamInGame>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<NetworkId> id, Entity connection) in
                     SystemAPI.Query<RefRO<NetworkId>>()
                         .WithNone<NetworkStreamInGame>()
                         .WithEntityAccess())
            {
                commands.AddComponent<NetworkStreamInGame>(connection);

                Entity request = commands.CreateEntity();
                commands.AddComponent<GoInGameRequest>(request);
                commands.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });

                UnityEngine.Debug.Log(
                    $"[GoInGameClientSystem] Connected as player {id.ValueRO.Value}.");
            }

            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Marks the connection in game and gives it a body.
    ///
    /// Waits for the avatar prefab, which rides in the scene's SubScene: a
    /// request that arrives while the lobby is still streaming in stays queued
    /// rather than spawning nothing.
    ///
    /// The avatar is linked to the connection, so a player who leaves takes it
    /// with them — and, having no avatar, drops out of the position buffer's
    /// targeting on the next tick (PlayerCommandReceiveSystem).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GoInGameServerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkPrefabs>();
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<GoInGameRequest, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity prefab = SystemAPI.GetSingleton<NetworkPrefabs>().Avatar;

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<ReceiveRpcCommandRequest> received, Entity request) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                         .WithAll<GoInGameRequest>()
                         .WithEntityAccess())
            {
                Entity connection = received.ValueRO.SourceConnection;
                int playerId = SystemAPI.GetComponent<NetworkId>(connection).Value;

                commands.AddComponent<NetworkStreamInGame>(connection);

                Entity avatar = commands.Instantiate(prefab);
                commands.SetComponent(avatar, new GhostOwner { NetworkId = playerId });
                commands.AppendToBuffer(connection, new LinkedEntityGroup { Value = avatar });

                commands.DestroyEntity(request);

                UnityEngine.Debug.Log($"[GoInGameServerSystem] Player {playerId} is in game.");
            }

            commands.Playback(state.EntityManager);
        }
    }
}
