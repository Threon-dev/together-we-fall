using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using TogetherWeFall.Lobby;
using TogetherWeFall.Lobby.Systems;

namespace TogetherWeFall.Network
{
    /// <summary>A client toggling its own ready flag at the portal.</summary>
    public struct PortalReadyRpc : IRpcCommand
    {
        public bool Ready;
    }

    /// <summary>
    /// What the host's portal looks like now: who is ready and how far the
    /// departure has got. Four ready slots because a session holds four players.
    /// </summary>
    public struct PortalStateRpc : IRpcCommand
    {
        public const int MaxReady = 4;

        public FixedString32Bytes Target;
        public SceneTransitionPhase Phase;
        public int ReadyCount;
        public int Ready0;
        public int Ready1;
        public int Ready2;
        public int Ready3;
    }

    /// <summary>
    /// Client: LobbyUI still adds PortalReadyRequest to the portal it is looking
    /// at — the portal in the client world, which is a plain SubScene entity and
    /// decides nothing. This sends each toggle to the host instead.
    ///
    /// The request's PlayerId is not sent: the host takes it from the connection.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct PortalReadyForwardSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DungeonPortal>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach (DynamicBuffer<PortalReadyRequest> requests in
                     SystemAPI.Query<DynamicBuffer<PortalReadyRequest>>().WithAll<DungeonPortal>())
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    Entity rpc = commands.CreateEntity();
                    commands.AddComponent(rpc, new PortalReadyRpc { Ready = requests[i].Ready });
                    commands.AddComponent<SendRpcCommandRequest>(rpc);
                }

                requests.Clear();
            }

            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Host: turns a client's toggle back into the request DungeonPortalSystem
    /// has always read, with the sender's NetworkId as the player.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DungeonPortalSystem))]
    public partial struct PortalReadyReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<PortalReadyRpc, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<PortalReadyRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<PortalReadyRpc>, RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                int playerId = SystemAPI.GetComponent<NetworkId>(received.ValueRO.SourceConnection).Value;

                foreach (DynamicBuffer<PortalReadyRequest> requests in
                         SystemAPI.Query<DynamicBuffer<PortalReadyRequest>>().WithAll<DungeonPortal>())
                {
                    requests.Add(new PortalReadyRequest { PlayerId = playerId, Ready = rpc.ValueRO.Ready });
                }

                commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Host: tells every client what the portal looks like whenever that
    /// changes — or whenever somebody new is in game, so a player who arrives
    /// late sees the same ready list as everybody else.
    ///
    /// ponytail: assumes one portal per scene, which is what the lobby has.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DungeonPortalSystem))]
    public partial struct PortalStateBroadcastSystem : ISystem
    {
        private EntityQuery _inGameQuery;
        private int _sentSignature;

        public void OnCreate(ref SystemState state)
        {
            _inGameQuery = SystemAPI.QueryBuilder()
                .WithAll<NetworkId, NetworkStreamInGame>()
                .Build();

            _sentSignature = int.MinValue;

            state.RequireForUpdate<DungeonPortal>();
        }

        public void OnUpdate(ref SystemState state)
        {
            bool found = false;
            var message = new PortalStateRpc();

            foreach ((RefRO<SceneTransition> transition, DynamicBuffer<LobbyReadyPlayer> ready) in
                     SystemAPI.Query<RefRO<SceneTransition>, DynamicBuffer<LobbyReadyPlayer>>()
                         .WithAll<DungeonPortal>())
            {
                message.Target = transition.ValueRO.Target;
                message.Phase = transition.ValueRO.Phase;
                message.ReadyCount = ready.Length < PortalStateRpc.MaxReady ? ready.Length : PortalStateRpc.MaxReady;
                message.Ready0 = ready.Length > 0 ? ready[0].PlayerId : 0;
                message.Ready1 = ready.Length > 1 ? ready[1].PlayerId : 0;
                message.Ready2 = ready.Length > 2 ? ready[2].PlayerId : 0;
                message.Ready3 = ready.Length > 3 ? ready[3].PlayerId : 0;

                found = true;
                break;
            }

            if (!found)
                return;

            int signature = (int)message.Phase;
            signature = signature * 31 + message.ReadyCount;
            signature = signature * 31 + message.Ready0;
            signature = signature * 31 + message.Ready1;
            signature = signature * 31 + message.Ready2;
            signature = signature * 31 + message.Ready3;
            signature = signature * 31 + _inGameQuery.CalculateEntityCount();

            if (signature == _sentSignature)
                return;

            _sentSignature = signature;

            // Created after the loop: it is a structural change, and the ready
            // buffer read above would not survive it.
            Entity rpc = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpc, message);
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpc);
        }
    }

    /// <summary>
    /// Client: writes the host's portal state onto the local portal, where
    /// LobbyUI draws the ready list from and SceneLoadBridge waits for Ready —
    /// both unchanged, both reading the client world as always.
    ///
    /// A message that finds no portal yet (the lobby is still streaming in) is
    /// kept for the next frame, except a Ready: kept, it would send the next
    /// lobby this client walks into straight back out of it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct PortalStateReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<PortalStateRpc, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<PortalStateRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<PortalStateRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                PortalStateRpc message = rpc.ValueRO;
                bool applied = false;

                foreach ((RefRW<SceneTransition> transition, DynamicBuffer<LobbyReadyPlayer> ready) in
                         SystemAPI.Query<RefRW<SceneTransition>, DynamicBuffer<LobbyReadyPlayer>>()
                             .WithAll<DungeonPortal>())
                {
                    transition.ValueRW.Target = message.Target;
                    transition.ValueRW.Phase = message.Phase;

                    ready.Clear();
                    if (message.ReadyCount > 0) ready.Add(new LobbyReadyPlayer { PlayerId = message.Ready0 });
                    if (message.ReadyCount > 1) ready.Add(new LobbyReadyPlayer { PlayerId = message.Ready1 });
                    if (message.ReadyCount > 2) ready.Add(new LobbyReadyPlayer { PlayerId = message.Ready2 });
                    if (message.ReadyCount > 3) ready.Add(new LobbyReadyPlayer { PlayerId = message.Ready3 });

                    applied = true;
                }

                if (applied || message.Phase == SceneTransitionPhase.Ready)
                    commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);
        }
    }
}
