using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using TogetherWeFall.Audio;
using TogetherWeFall.Combat;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.Network
{
    /// <summary>A VfxEvent, on its way from the host to every screen.</summary>
    public struct VfxEventRpc : IRpcCommand
    {
        public VfxEventKind Kind;
        public float3 Position;
        public float3 EndPosition;
        public float4 Color;
        public DamageType Element;
        public float Magnitude;
        public bool Emphasis;
        public int VfxId;
        public bool SweepRight;
    }

    /// <summary>An AudioEvent, on its way from the host to every screen.</summary>
    public struct AudioEventRpc : IRpcCommand
    {
        public AudioCue Cue;
        public float3 Position;
        public float Volume;
        public float Pitch;
    }

    /// <summary>
    /// Host: sends what the simulation wants seen and heard this tick to every
    /// client, and empties the host's queues.
    ///
    /// Emptying is this system's job now. VfxPresenter used to clear the VFX
    /// queue after drawing it, but the presenter reads the client world, and a
    /// host queue nobody drains grows for the rest of the session. The audio
    /// queue clears itself each tick (AudioEventRegistrySystem), which is why
    /// this runs last in the simulation, after every writer and before that clear.
    ///
    /// ponytail: capped per tick, and whatever is over the cap is dropped — a
    /// forty-kill frame is forty bodies and roughly one sound either way. If the
    /// cap is visibly too low, raise it or batch several events per message.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct EventForwardSystem : ISystem
    {
        private const int MaxVfxPerTick = 64;
        private const int MaxAudioPerTick = 16;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VfxEventsSingleton>();
            state.RequireForUpdate<AudioEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            DynamicBuffer<AudioEvent> audio = SystemAPI.GetSingletonBuffer<AudioEvent>();

            if (vfx.Length == 0 && audio.Length == 0)
                return;

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            int vfxCount = math.min(vfx.Length, MaxVfxPerTick);
            for (int i = 0; i < vfxCount; i++)
            {
                VfxEvent effect = vfx[i];

                Entity rpc = commands.CreateEntity();
                commands.AddComponent(rpc, new VfxEventRpc
                {
                    Kind = effect.Kind,
                    Position = effect.Position,
                    EndPosition = effect.EndPosition,
                    Color = effect.Color,
                    Element = effect.Element,
                    Magnitude = effect.Magnitude,
                    Emphasis = effect.Emphasis,
                    VfxId = effect.VfxId,
                    SweepRight = effect.SweepRight
                });
                commands.AddComponent<SendRpcCommandRequest>(rpc);
            }

            int audioCount = math.min(audio.Length, MaxAudioPerTick);
            for (int i = 0; i < audioCount; i++)
            {
                AudioEvent sound = audio[i];

                Entity rpc = commands.CreateEntity();
                commands.AddComponent(rpc, new AudioEventRpc
                {
                    Cue = sound.Cue,
                    Position = sound.Position,
                    Volume = sound.Volume,
                    Pitch = sound.Pitch
                });
                commands.AddComponent<SendRpcCommandRequest>(rpc);
            }

            vfx.Clear();
            audio.Clear();

            // Last: creating entities is structural, and both buffers above
            // would be stale after it.
            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Client: puts the host's events into the local queues, where the
    /// unchanged VfxPresenter and AudioPresenter pick them up.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct EventReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VfxEventsSingleton>();
            state.RequireForUpdate<AudioEventsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<VfxEvent> vfx = SystemAPI.GetSingletonBuffer<VfxEvent>();
            DynamicBuffer<AudioEvent> audio = SystemAPI.GetSingletonBuffer<AudioEvent>();

            using var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<VfxEventRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<VfxEventRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                VfxEventRpc message = rpc.ValueRO;

                vfx.Add(new VfxEvent
                {
                    Kind = message.Kind,
                    Position = message.Position,
                    EndPosition = message.EndPosition,
                    Color = message.Color,
                    Element = message.Element,
                    Magnitude = message.Magnitude,
                    Emphasis = message.Emphasis,
                    VfxId = message.VfxId,
                    SweepRight = message.SweepRight
                });

                commands.DestroyEntity(entity);
            }

            foreach ((RefRO<AudioEventRpc> rpc, Entity entity) in
                     SystemAPI.Query<RefRO<AudioEventRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                audio.Add(new AudioEvent
                {
                    Cue = rpc.ValueRO.Cue,
                    Position = rpc.ValueRO.Position,
                    Volume = rpc.ValueRO.Volume,
                    Pitch = rpc.ValueRO.Pitch
                });

                commands.DestroyEntity(entity);
            }

            commands.Playback(state.EntityManager);
        }
    }
}
