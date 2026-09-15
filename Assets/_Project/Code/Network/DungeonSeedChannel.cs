using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace TogetherWeFall.Network
{
    /// <summary>The floor's seed, from the host to every client.</summary>
    public struct DungeonSeedRpc : IRpcCommand
    {
        public uint Seed;
    }

    /// <summary>
    /// Client-side: a seed that arrived and has not been built yet.
    ///
    /// Not tied to any scene, so a seed that lands while this client is still
    /// fading out of the lobby waits for the dungeon to wake up and take it.
    /// </summary>
    public struct PendingDungeonSeed : IComponentData
    {
        public uint Value;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct DungeonSeedReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<DungeonSeedRpc, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            uint seed = 0;

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<DungeonSeedRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<DungeonSeedRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                seed = rpc.ValueRO.Seed;
                commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);

            // After playback: creating the holder is structural too.
            if (SystemAPI.TryGetSingletonRW(out RefRW<PendingDungeonSeed> pending))
            {
                pending.ValueRW.Value = seed;
            }
            else
            {
                Entity holder = state.EntityManager.CreateEntity();
                state.EntityManager.SetName(holder, "PendingDungeonSeed");
                state.EntityManager.AddComponentData(holder, new PendingDungeonSeed { Value = seed });
            }
        }
    }

    /// <summary>
    /// How GameBootstrap sends and receives the one seed a floor is built from.
    ///
    /// DungeonDirector.ResolveSeed stays the only place a seed is born, and it
    /// is only ever called on the host. The dungeon never travels: the number
    /// does, and each client builds the identical floor from it.
    /// </summary>
    public sealed class DungeonSeedChannel
    {
        /// <summary>Whether this process runs the simulation.</summary>
        public bool IsHost => ClientServerBootstrap.ServerWorld != null;

        public void Send(uint seed)
        {
            World server = ClientServerBootstrap.ServerWorld;
            if (server == null)
                return;

            EntityManager entityManager = server.EntityManager;
            Entity rpc = entityManager.CreateEntity();
            entityManager.AddComponentData(rpc, new DungeonSeedRpc { Seed = seed });
            entityManager.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public bool TryTake(out uint seed)
        {
            seed = 0;

            World client = World.DefaultGameObjectInjectionWorld;
            if (client == null || !client.IsCreated)
                return false;

            using EntityQuery query = client.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PendingDungeonSeed>());

            if (query.IsEmptyIgnoreFilter)
                return false;

            seed = query.GetSingleton<PendingDungeonSeed>().Value;
            client.EntityManager.DestroyEntity(query.GetSingletonEntity());
            return true;
        }
    }
}
