using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Player;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// An entity on the wire: its ghost id, or <see cref="None"/>.
    ///
    /// Entities are different numbers in two worlds, so every reference a
    /// request or a result carries travels as the id both worlds agree on. Only
    /// ghosts can be named that way — which is everything a panel points at:
    /// items, bags, shelves. NPCs are not ghosts, and are found by kind.
    ///
    /// ponytail: the id alone, without the spawn tick. Pooled ghosts are never
    /// destroyed, so no id is reused while a panel could still be pointing at
    /// it; carry the tick too once ghosts start being destroyed and respawned.
    /// </summary>
    public struct GhostRef
    {
        public const long None = -1;

        public static long Of(Entity entity, ComponentLookup<GhostInstance> ghosts)
        {
            if (entity == Entity.Null || !ghosts.HasComponent(entity))
                return None;

            return (uint)ghosts[entity].ghostId;
        }

        public static Entity Resolve(long id, NativeParallelHashMap<int, Entity> ghosts)
        {
            return id != None && ghosts.TryGetValue((int)(uint)id, out Entity entity)
                ? entity
                : Entity.Null;
        }

        /// <summary>Every ghost in the world by id. Built only on a frame with messages.</summary>
        public static NativeParallelHashMap<int, Entity> Map(EntityQuery ghosts)
        {
            using NativeArray<Entity> entities = ghosts.ToEntityArray(Allocator.Temp);
            using NativeArray<GhostInstance> ids = ghosts.ToComponentDataArray<GhostInstance>(Allocator.Temp);

            var map = new NativeParallelHashMap<int, Entity>(entities.Length > 0 ? entities.Length : 1, Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
                map.TryAdd(ids[i].ghostId, entities[i]);

            return map;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Requests: what the panels ask for, client → host
    // ─────────────────────────────────────────────────────────────────────

    public struct EquipRequestRpc : IRpcCommand
    {
        public EquipRequestKind Kind;
        public long Item;
        public EquipmentSlot Slot;
        public EquipmentSlot OtherSlot;
        public bool UseTarget;
        public int TargetX;
        public int TargetY;
        public bool Rotated;
    }

    public struct SocketRequestRpc : IRpcCommand
    {
        public SocketRequestKind Kind;
        public long Gear;
        public long Gem;
        public int SocketIndex;
        public int BarSlotIndex;
    }

    public struct PlacementRequestRpc : IRpcCommand
    {
        public long Container;
        public long Item;
        public InventoryPlacementMode Mode;
        public int OriginX;
        public int OriginY;
        public bool Rotated;
    }

    /// <summary>The vendor is not named: there is one, and it is not a ghost.</summary>
    public struct VendorRequestRpc : IRpcCommand
    {
        public long Item;
        public VendorTransactionKind Kind;
    }

    /// <summary>The forge is not named, for the same reason as the vendor.</summary>
    public struct CraftRequestRpc : IRpcCommand
    {
        public long Item;
        public CraftOperation Operation;
        public int SocketIndex;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Results: what the host answered, host → the one player who asked
    // ─────────────────────────────────────────────────────────────────────

    public struct EquipResultRpc : IRpcCommand
    {
        public long Item;
        public EquipmentSlot Slot;
        public EquipStatus Status;
    }

    public struct SocketResultRpc : IRpcCommand
    {
        public long Gear;
        public long Gem;
        public int SocketIndex;
        public SocketStatus Status;
    }

    public struct PlacementResultRpc : IRpcCommand
    {
        public long Item;
        public InventoryPlacementStatus Status;
        public int OriginX;
        public int OriginY;
        public bool Rotated;
    }

    public struct VendorResultRpc : IRpcCommand
    {
        public long Item;
        public VendorTransactionStatus Status;
        public int Price;
    }

    public struct CraftResultRpc : IRpcCommand
    {
        public long Item;
        public CraftOperation Operation;
        public CraftStatus Status;
        public int Price;
    }

    /// <summary>
    /// Client: sends what InventoryUI and LobbyUI queued on this player's
    /// character this frame, and empties the queues.
    ///
    /// The panels are unchanged. They still add requests to the character they
    /// are drawing — now a ghost, whose request buffers are not replicated and
    /// so are this screen's own — and this is the one reader.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CharacterRequestForwardSystem : ISystem
    {
        private ComponentLookup<GhostInstance> _ghosts;

        public void OnCreate(ref SystemState state)
        {
            _ghosts = state.GetComponentLookup<GhostInstance>(isReadOnly: true);

            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            int localPlayerId = SystemAPI.GetSingleton<NetworkId>().Value;
            _ghosts.Update(ref state);

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<PlayerCharacter> character,
                      DynamicBuffer<EquipRequest> equips,
                      DynamicBuffer<SocketRequest> sockets,
                      DynamicBuffer<InventoryPlacementRequest> placements,
                      DynamicBuffer<VendorTransactionRequest> trades,
                      DynamicBuffer<CraftRequest> crafts) in
                     SystemAPI.Query<RefRO<PlayerCharacter>,
                         DynamicBuffer<EquipRequest>,
                         DynamicBuffer<SocketRequest>,
                         DynamicBuffer<InventoryPlacementRequest>,
                         DynamicBuffer<VendorTransactionRequest>,
                         DynamicBuffer<CraftRequest>>())
            {
                if (character.ValueRO.PlayerId != localPlayerId)
                    continue;

                for (int i = 0; i < equips.Length; i++)
                {
                    EquipRequest request = equips[i];
                    Send(commands, new EquipRequestRpc
                    {
                        Kind = request.Kind,
                        Item = GhostRef.Of(request.Item, _ghosts),
                        Slot = request.Slot,
                        OtherSlot = request.OtherSlot,
                        UseTarget = request.UseTarget,
                        TargetX = request.TargetX,
                        TargetY = request.TargetY,
                        Rotated = request.Rotated
                    });
                }

                for (int i = 0; i < sockets.Length; i++)
                {
                    SocketRequest request = sockets[i];
                    Send(commands, new SocketRequestRpc
                    {
                        Kind = request.Kind,
                        Gear = GhostRef.Of(request.Gear, _ghosts),
                        Gem = GhostRef.Of(request.Gem, _ghosts),
                        SocketIndex = request.SocketIndex,
                        BarSlotIndex = request.BarSlotIndex
                    });
                }

                for (int i = 0; i < placements.Length; i++)
                {
                    InventoryPlacementRequest request = placements[i];
                    Send(commands, new PlacementRequestRpc
                    {
                        Container = GhostRef.Of(request.Container, _ghosts),
                        Item = GhostRef.Of(request.Item, _ghosts),
                        Mode = request.Mode,
                        OriginX = request.OriginX,
                        OriginY = request.OriginY,
                        Rotated = request.Rotated
                    });
                }

                for (int i = 0; i < trades.Length; i++)
                {
                    Send(commands, new VendorRequestRpc
                    {
                        Item = GhostRef.Of(trades[i].Item, _ghosts),
                        Kind = trades[i].Kind
                    });
                }

                for (int i = 0; i < crafts.Length; i++)
                {
                    Send(commands, new CraftRequestRpc
                    {
                        Item = GhostRef.Of(crafts[i].Item, _ghosts),
                        Operation = crafts[i].Operation,
                        SocketIndex = crafts[i].SocketIndex
                    });
                }

                equips.Clear();
                sockets.Clear();
                placements.Clear();
                trades.Clear();
                crafts.Clear();
            }

            commands.Playback(state.EntityManager);
        }

        private static void Send<T>(EntityCommandBuffer commands, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity entity = commands.CreateEntity();
            commands.AddComponent(entity, rpc);
            commands.AddComponent<SendRpcCommandRequest>(entity);
        }
    }

    /// <summary>
    /// Host: turns each request back into the buffer entry the equipment,
    /// socket, inventory, vendor and forge systems have always read — on the
    /// sender's own character, found by the connection's NetworkId.
    ///
    /// Nothing here judges a request. An id that names nothing becomes
    /// Entity.Null, and a container that is not the sender's is refused by the
    /// same ContainerOwner check that has always refused it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TogetherWeFall.Inventory.Systems.InventoryPlacementSystem))]
    [UpdateBefore(typeof(TogetherWeFall.Equipment.Systems.EquipmentSystem))]
    [UpdateBefore(typeof(TogetherWeFall.Equipment.Systems.SocketSystem))]
    [UpdateBefore(typeof(TogetherWeFall.Lobby.Systems.VendorTransactionSystem))]
    [UpdateBefore(typeof(TogetherWeFall.Lobby.Systems.CraftingSystem))]
    public partial struct CharacterRequestReceiveSystem : ISystem
    {
        private EntityQuery _ghostQuery;
        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
            _ghostQuery = SystemAPI.QueryBuilder().WithAll<GhostInstance>().Build();
            _characterQuery = SystemAPI.QueryBuilder().WithAll<PlayerCharacter>().Build();

            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<ReceiveRpcCommandRequest>()
                .WithAny<EquipRequestRpc, SocketRequestRpc, PlacementRequestRpc, VendorRequestRpc, CraftRequestRpc>()
                .Build());
            state.RequireForUpdate(_characterQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;

            using NativeParallelHashMap<int, Entity> ghosts = GhostRef.Map(_ghostQuery);
            using NativeArray<Entity> characters = _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> sheets = _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            Entity vendor = FindNpc(ref state, NpcServiceType.Vendor);
            Entity forge = FindNpc(ref state, NpcServiceType.Crafting);

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<EquipRequestRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<EquipRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity character = CharacterOf(entityManager, received.ValueRO.SourceConnection, characters, sheets);
                EquipRequestRpc message = rpc.ValueRO;

                if (character != Entity.Null)
                {
                    entityManager.GetBuffer<EquipRequest>(character).Add(new EquipRequest
                    {
                        Kind = message.Kind,
                        Item = GhostRef.Resolve(message.Item, ghosts),
                        Slot = message.Slot,
                        OtherSlot = message.OtherSlot,
                        UseTarget = message.UseTarget,
                        TargetX = message.TargetX,
                        TargetY = message.TargetY,
                        Rotated = message.Rotated
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<SocketRequestRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<SocketRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity character = CharacterOf(entityManager, received.ValueRO.SourceConnection, characters, sheets);
                SocketRequestRpc message = rpc.ValueRO;

                if (character != Entity.Null)
                {
                    entityManager.GetBuffer<SocketRequest>(character).Add(new SocketRequest
                    {
                        Kind = message.Kind,
                        Gear = GhostRef.Resolve(message.Gear, ghosts),
                        Gem = GhostRef.Resolve(message.Gem, ghosts),
                        SocketIndex = message.SocketIndex,
                        BarSlotIndex = message.BarSlotIndex
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<PlacementRequestRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<PlacementRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity character = CharacterOf(entityManager, received.ValueRO.SourceConnection, characters, sheets);
                PlacementRequestRpc message = rpc.ValueRO;

                if (character != Entity.Null)
                {
                    entityManager.GetBuffer<InventoryPlacementRequest>(character).Add(new InventoryPlacementRequest
                    {
                        Container = GhostRef.Resolve(message.Container, ghosts),
                        Item = GhostRef.Resolve(message.Item, ghosts),
                        Mode = message.Mode,
                        OriginX = message.OriginX,
                        OriginY = message.OriginY,
                        Rotated = message.Rotated
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<VendorRequestRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<VendorRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity character = CharacterOf(entityManager, received.ValueRO.SourceConnection, characters, sheets);

                if (character != Entity.Null)
                {
                    entityManager.GetBuffer<VendorTransactionRequest>(character).Add(new VendorTransactionRequest
                    {
                        Vendor = vendor,
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Kind = rpc.ValueRO.Kind
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<CraftRequestRpc> rpc, RefRO<ReceiveRpcCommandRequest> received, Entity entity) in
                     SystemAPI.Query<RefRO<CraftRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity character = CharacterOf(entityManager, received.ValueRO.SourceConnection, characters, sheets);

                if (character != Entity.Null)
                {
                    entityManager.GetBuffer<CraftRequest>(character).Add(new CraftRequest
                    {
                        Station = forge,
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Operation = rpc.ValueRO.Operation,
                        SocketIndex = rpc.ValueRO.SocketIndex
                    });
                }

                commands.DestroyEntity(entity);
            }

            // Last: destroying the messages is structural, and every buffer
            // written above would be stale after it.
            commands.Playback(entityManager);
        }

        private Entity FindNpc(ref SystemState state, NpcServiceType type)
        {
            foreach ((RefRO<NpcService> service, Entity entity) in
                     SystemAPI.Query<RefRO<NpcService>>().WithEntityAccess())
            {
                if (service.ValueRO.Type == type)
                    return entity;
            }

            return Entity.Null;
        }

        private static Entity CharacterOf(
            EntityManager entityManager, Entity connection,
            NativeArray<Entity> characters, NativeArray<PlayerCharacter> sheets)
        {
            if (!entityManager.HasComponent<NetworkId>(connection))
                return Entity.Null;

            int playerId = entityManager.GetComponentData<NetworkId>(connection).Value;

            for (int i = 0; i < sheets.Length; i++)
            {
                if (sheets[i].PlayerId == playerId)
                    return characters[i];
            }

            return Entity.Null;
        }
    }

    /// <summary>
    /// Host: sends every result the systems wrote this tick to the player whose
    /// character it is on, and empties the host's result buffers.
    ///
    /// Emptying is this system's job now: the panels used to clear them after
    /// reporting a refusal, and the panels read the client world. Last in the
    /// simulation, so a result written by anything this tick is already there.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct CharacterResultForwardSystem : ISystem
    {
        private ComponentLookup<GhostInstance> _ghosts;
        private EntityQuery _connectionQuery;

        public void OnCreate(ref SystemState state)
        {
            _ghosts = state.GetComponentLookup<GhostInstance>(isReadOnly: true);
            _connectionQuery = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build();

            state.RequireForUpdate<PlayerCharacter>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _ghosts.Update(ref state);

            using NativeArray<Entity> connections = _connectionQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<NetworkId> ids = _connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<PlayerCharacter> character,
                      DynamicBuffer<EquipResult> equips,
                      DynamicBuffer<SocketResult> sockets,
                      DynamicBuffer<InventoryPlacementResult> placements,
                      DynamicBuffer<VendorTransactionResult> trades,
                      DynamicBuffer<CraftResult> crafts) in
                     SystemAPI.Query<RefRO<PlayerCharacter>,
                         DynamicBuffer<EquipResult>,
                         DynamicBuffer<SocketResult>,
                         DynamicBuffer<InventoryPlacementResult>,
                         DynamicBuffer<VendorTransactionResult>,
                         DynamicBuffer<CraftResult>>())
            {
                if (equips.Length + sockets.Length + placements.Length + trades.Length + crafts.Length == 0)
                    continue;

                // None for a character nobody is connected as — the arena's ally
                // dummy — whose results are simply dropped.
                Entity connection = ConnectionOf(connections, ids, character.ValueRO.PlayerId);

                if (connection != Entity.Null)
                {
                    for (int i = 0; i < equips.Length; i++)
                    {
                        Send(commands, connection, new EquipResultRpc
                        {
                            Item = GhostRef.Of(equips[i].Item, _ghosts),
                            Slot = equips[i].Slot,
                            Status = equips[i].Status
                        });
                    }

                    for (int i = 0; i < sockets.Length; i++)
                    {
                        Send(commands, connection, new SocketResultRpc
                        {
                            Gear = GhostRef.Of(sockets[i].Gear, _ghosts),
                            Gem = GhostRef.Of(sockets[i].Gem, _ghosts),
                            SocketIndex = sockets[i].SocketIndex,
                            Status = sockets[i].Status
                        });
                    }

                    for (int i = 0; i < placements.Length; i++)
                    {
                        Send(commands, connection, new PlacementResultRpc
                        {
                            Item = GhostRef.Of(placements[i].Item, _ghosts),
                            Status = placements[i].Status,
                            OriginX = placements[i].OriginX,
                            OriginY = placements[i].OriginY,
                            Rotated = placements[i].Rotated
                        });
                    }

                    for (int i = 0; i < trades.Length; i++)
                    {
                        Send(commands, connection, new VendorResultRpc
                        {
                            Item = GhostRef.Of(trades[i].Item, _ghosts),
                            Status = trades[i].Status,
                            Price = trades[i].Price
                        });
                    }

                    for (int i = 0; i < crafts.Length; i++)
                    {
                        Send(commands, connection, new CraftResultRpc
                        {
                            Item = GhostRef.Of(crafts[i].Item, _ghosts),
                            Operation = crafts[i].Operation,
                            Status = crafts[i].Status,
                            Price = crafts[i].Price
                        });
                    }
                }

                equips.Clear();
                sockets.Clear();
                placements.Clear();
                trades.Clear();
                crafts.Clear();
            }

            commands.Playback(state.EntityManager);
        }

        private static Entity ConnectionOf(NativeArray<Entity> connections, NativeArray<NetworkId> ids, int playerId)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i].Value == playerId)
                    return connections[i];
            }

            return Entity.Null;
        }

        private static void Send<T>(EntityCommandBuffer commands, Entity connection, T rpc)
            where T : unmanaged, IRpcCommand
        {
            Entity entity = commands.CreateEntity();
            commands.AddComponent(entity, rpc);
            commands.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Client: puts the host's answers into this player's result buffers, which
    /// InventoryUI and LobbyUI read and clear exactly as before.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CharacterResultReceiveSystem : ISystem
    {
        private EntityQuery _ghostQuery;

        public void OnCreate(ref SystemState state)
        {
            _ghostQuery = SystemAPI.QueryBuilder().WithAll<GhostInstance>().Build();

            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<ReceiveRpcCommandRequest>()
                .WithAny<EquipResultRpc, SocketResultRpc, PlacementResultRpc, VendorResultRpc, CraftResultRpc>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager entityManager = state.EntityManager;
            int localPlayerId = SystemAPI.GetSingleton<NetworkId>().Value;

            Entity character = Entity.Null;

            foreach ((RefRO<PlayerCharacter> sheet, Entity entity) in
                     SystemAPI.Query<RefRO<PlayerCharacter>>().WithEntityAccess())
            {
                if (sheet.ValueRO.PlayerId == localPlayerId)
                    character = entity;
            }

            using NativeParallelHashMap<int, Entity> ghosts = GhostRef.Map(_ghostQuery);
            using var commands = new EntityCommandBuffer(Allocator.Temp);

            // A result for a character that has not arrived yet is dropped: it
            // reports a click, and a report that late has nobody left to tell.
            bool hasCharacter = character != Entity.Null;

            foreach ((RefRO<EquipResultRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<EquipResultRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (hasCharacter)
                {
                    entityManager.GetBuffer<EquipResult>(character).Add(new EquipResult
                    {
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Slot = rpc.ValueRO.Slot,
                        Status = rpc.ValueRO.Status
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<SocketResultRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<SocketResultRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (hasCharacter)
                {
                    entityManager.GetBuffer<SocketResult>(character).Add(new SocketResult
                    {
                        Gear = GhostRef.Resolve(rpc.ValueRO.Gear, ghosts),
                        Gem = GhostRef.Resolve(rpc.ValueRO.Gem, ghosts),
                        SocketIndex = rpc.ValueRO.SocketIndex,
                        Status = rpc.ValueRO.Status
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<PlacementResultRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<PlacementResultRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (hasCharacter)
                {
                    entityManager.GetBuffer<InventoryPlacementResult>(character).Add(new InventoryPlacementResult
                    {
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Status = rpc.ValueRO.Status,
                        OriginX = rpc.ValueRO.OriginX,
                        OriginY = rpc.ValueRO.OriginY,
                        Rotated = rpc.ValueRO.Rotated
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<VendorResultRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<VendorResultRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (hasCharacter)
                {
                    entityManager.GetBuffer<VendorTransactionResult>(character).Add(new VendorTransactionResult
                    {
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Status = rpc.ValueRO.Status,
                        Price = rpc.ValueRO.Price
                    });
                }

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<CraftResultRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<CraftResultRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (hasCharacter)
                {
                    entityManager.GetBuffer<CraftResult>(character).Add(new CraftResult
                    {
                        Item = GhostRef.Resolve(rpc.ValueRO.Item, ghosts),
                        Operation = rpc.ValueRO.Operation,
                        Status = rpc.ValueRO.Status,
                        Price = rpc.ValueRO.Price
                    });
                }

                commands.DestroyEntity(entity);
            }

            commands.Playback(entityManager);
        }
    }
}
