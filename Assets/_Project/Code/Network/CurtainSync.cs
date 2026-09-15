using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using TogetherWeFall.Curtain;

namespace TogetherWeFall.Network
{
    /// <summary>A CurtainRequest, on its way from the host to every screen.</summary>
    public struct CurtainRpc : IRpcCommand
    {
        public float Target;
        public float Duration;
        public float4 Colour;
        public CurtainReason Reason;
    }

    /// <summary>
    /// Host: copies every curtain request the simulation made this tick to all
    /// clients.
    ///
    /// Copies, does not consume: the host's own CurtainSystem still runs the
    /// fade, because DungeonPortalSystem waits on the host's curtain being
    /// covered. Last in the group, so a request added by anything this tick is
    /// already there; the host's CurtainSystem clears the buffer at the start of
    /// the next tick, so nothing is sent twice.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct CurtainForwardSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CurtainSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<CurtainRequest> requests = SystemAPI.GetSingletonBuffer<CurtainRequest>(isReadOnly: true);

            if (requests.Length == 0)
                return;

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < requests.Length; i++)
            {
                CurtainRequest request = requests[i];

                Entity rpc = commands.CreateEntity();
                commands.AddComponent(rpc, new CurtainRpc
                {
                    Target = request.Target,
                    Duration = request.Duration,
                    Colour = request.Colour,
                    Reason = request.Reason
                });
                commands.AddComponent<SendRpcCommandRequest>(rpc);
            }

            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Client: puts the host's curtain request into the local curtain, which the
    /// unchanged CurtainPresenter draws.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CurtainReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CurtainSingleton>();
            state.RequireForUpdate(SystemAPI.QueryBuilder()
                .WithAll<CurtainRpc, ReceiveRpcCommandRequest>()
                .Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<CurtainRequest> requests = SystemAPI.GetSingletonBuffer<CurtainRequest>();

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<CurtainRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<CurtainRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                requests.Add(new CurtainRequest
                {
                    Target = rpc.ValueRO.Target,
                    Duration = rpc.ValueRO.Duration,
                    Colour = rpc.ValueRO.Colour,
                    Reason = rpc.ValueRO.Reason
                });

                commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);
        }
    }
}
