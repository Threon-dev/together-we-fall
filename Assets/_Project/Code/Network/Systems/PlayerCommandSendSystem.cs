using Unity.Entities;
using Unity.NetCode;
using TogetherWeFall.Interaction;
using TogetherWeFall.Shared;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Network.Systems
{
    /// <summary>
    /// Client: folds what the player bridges wrote this frame into the owned
    /// avatar's command for this tick.
    ///
    /// PlayerPositionPublisher and PlayerActionPublisher are untouched. They
    /// still write positions, interactions and casts into the queues they
    /// always did — now in the client world, where nothing simulates, and this
    /// is the one reader. That is the seam their comments promised: the bridges
    /// did not have to learn about the network for the network to arrive.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial struct PlayerCommandSendSystem : ISystem
    {
        private const int MaxCastSlots = 8;

        private uint _interactCount;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerPositionsSingleton>();
            state.RequireForUpdate<InteractionRequestsSingleton>();
            state.RequireForUpdate<SkillEventsSingleton>();
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

            if (tick.IsValid)
            {
                foreach ((DynamicBuffer<PlayerCommand> commands, RefRO<GhostOwner> owner) in
                         SystemAPI.Query<DynamicBuffer<PlayerCommand>, RefRO<GhostOwner>>()
                             .WithAll<GhostOwnerIsLocal>())
                {
                    int playerId = owner.ValueRO.NetworkId;
                    int index = IndexOf(positions, playerId);

                    if (index < 0)
                        continue;

                    PlayerPositionElement body = positions[index];

                    var command = new PlayerCommand
                    {
                        Tick = tick,
                        Position = body.Position,
                        Facing = body.Facing,
                        AimDirection = body.Facing,
                        AimPoint = body.Position
                    };

                    for (int i = 0; i < interactions.Length; i++)
                    {
                        if (interactions[i].PlayerId == playerId)
                            _interactCount++;
                    }

                    bool aimed = false;

                    for (int i = 0; i < casts.Length; i++)
                    {
                        SkillCastRequest cast = casts[i];

                        if (cast.PlayerId != playerId || cast.SlotIndex < 0 || cast.SlotIndex >= MaxCastSlots)
                            continue;

                        command.CastHeldMask |= (byte)(1 << cast.SlotIndex);
                        command.AimDirection = cast.Direction;
                        command.AimPoint = cast.AimPoint;
                        aimed = true;
                    }

                    command.InteractCount = _interactCount;

                    // Several frames can fall inside one tick, and AddCommandData
                    // keeps the last. Right for a position, wrong for a button: a
                    // tap that went down and up within one tick would vanish. So a
                    // tick keeps every slot any of its frames held, and the aim of
                    // the last frame that cast.
                    if (commands.GetDataAtTick(tick, out PlayerCommand earlier) && earlier.Tick == tick)
                    {
                        command.CastHeldMask |= earlier.CastHeldMask;

                        if (!aimed && earlier.CastHeldMask != 0)
                        {
                            command.AimDirection = earlier.AimDirection;
                            command.AimPoint = earlier.AimPoint;
                        }
                    }

                    commands.AddCommandData(command);
                }
            }

            // Nothing else in the client world consumes these, and a queue that
            // is never drained is a queue that grows until it is a leak.
            interactions.Clear();
            casts.Clear();
        }

        private static int IndexOf(DynamicBuffer<PlayerPositionElement> positions, int playerId)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].PlayerId == playerId)
                    return i;
            }

            return -1;
        }
    }
}
