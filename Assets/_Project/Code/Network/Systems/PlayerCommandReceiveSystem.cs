using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using TogetherWeFall.Interaction;
using TogetherWeFall.Interaction.Systems;
using TogetherWeFall.Shared;
using TogetherWeFall.Skills;
using TogetherWeFall.Skills.Systems;

namespace TogetherWeFall.Network.Systems
{
    /// <summary>
    /// Host: unpacks every avatar's command for this tick into the queues the
    /// simulation already reads.
    ///
    /// The mirror of PlayerCommandSendSystem, and the "snapshot unpacker" that
    /// PlayerPositions.cs said would one day fill the position buffer. Enemy,
    /// interaction and skill systems cannot tell these entries from the ones the
    /// bridges used to write directly — which is the point.
    ///
    /// The player id is the connection's NetworkId, taken from the avatar's
    /// owner rather than from anything in the command: a client does not get to
    /// say whose request it is.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    [UpdateBefore(typeof(InteractionResolveSystem))]
    [UpdateBefore(typeof(BlinkStrikeSystem))]
    [UpdateBefore(typeof(SkillCastSystem))]
    public partial struct PlayerCommandReceiveSystem : ISystem
    {
        private const int MaxCastSlots = 8;

        /// <summary>
        /// Every player id a command has ever arrived for. Only these are this
        /// system's to switch off: the buffer also holds entries nobody sends
        /// over the network — AllyDummySystem publishes a stand-in ally there —
        /// and those keep whatever their own writer says.
        /// </summary>
        private NativeHashSet<int> _networkedPlayers;

        public void OnCreate(ref SystemState state)
        {
            _networkedPlayers = new NativeHashSet<int>(4, Allocator.Persistent);

            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerPositionsSingleton>();
            state.RequireForUpdate<InteractionRequestsSingleton>();
            state.RequireForUpdate<SkillEventsSingleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            _networkedPlayers.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            NetworkTick tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;

            DynamicBuffer<PlayerPositionElement> positions =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>();
            DynamicBuffer<InteractionRequest> interactions =
                SystemAPI.GetSingletonBuffer<InteractionRequest>();
            DynamicBuffer<SkillCastRequest> casts =
                SystemAPI.GetSingletonBuffer<SkillCastRequest>();

            // No networked player is targetable until their avatar says otherwise
            // this tick. One who disconnected has no avatar any more — it was
            // linked to the connection — so they drop out of targeting on their
            // own, and their element stays so indices do not shift
            // (PlayerPositions.cs).
            for (int i = 0; i < positions.Length; i++)
            {
                PlayerPositionElement element = positions[i];

                if (!_networkedPlayers.Contains(element.PlayerId))
                    continue;

                element.IsTargetable = false;
                positions[i] = element;
            }

            foreach ((DynamicBuffer<PlayerCommand> commands,
                      RefRO<GhostOwner> owner,
                      RefRW<PlayerAvatar> avatar,
                      RefRW<LocalTransform> transform) in
                     SystemAPI.Query<DynamicBuffer<PlayerCommand>,
                         RefRO<GhostOwner>,
                         RefRW<PlayerAvatar>,
                         RefRW<LocalTransform>>())
            {
                // The latest command at or before this tick, so a tick whose
                // packet is late repeats the last known state rather than
                // dropping the player out of existence for a frame.
                if (!tick.IsValid || !commands.GetDataAtTick(tick, out PlayerCommand command))
                    continue;

                int playerId = owner.ValueRO.NetworkId;

                Upsert(positions, new PlayerPositionElement
                {
                    PlayerId = playerId,
                    Position = command.Position,
                    Facing = command.Facing,
                    IsTargetable = true
                });

                transform.ValueRW.Position = command.Position;

                var flatFacing = new float3(command.Facing.x, 0f, command.Facing.z);
                if (math.lengthsq(flatFacing) > 0.0001f)
                    transform.ValueRW.Rotation = quaternion.LookRotationSafe(flatFacing, math.up());

                if (command.InteractCount != avatar.ValueRO.SeenInteractCount)
                {
                    avatar.ValueRW.SeenInteractCount = command.InteractCount;
                    interactions.Add(new InteractionRequest
                    {
                        PlayerId = playerId,
                        Position = command.Position
                    });
                }

                for (int slot = 0; slot < MaxCastSlots; slot++)
                {
                    if ((command.CastHeldMask & (1 << slot)) == 0)
                        continue;

                    casts.Add(new SkillCastRequest
                    {
                        PlayerId = playerId,
                        SlotIndex = slot,
                        Origin = command.Position,
                        Direction = command.AimDirection,
                        AimPoint = command.AimPoint
                    });
                }
            }
        }

        private static void Upsert(DynamicBuffer<PlayerPositionElement> positions, PlayerPositionElement element)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].PlayerId == element.PlayerId)
                {
                    positions[i] = element;
                    return;
                }
            }

            positions.Add(element);
        }
    }
}
