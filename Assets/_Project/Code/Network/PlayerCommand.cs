using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace TogetherWeFall.Network
{
    /// <summary>
    /// One tick of "I am standing here, facing there, holding these buttons".
    ///
    /// The whole of what a client is allowed to say about its player, and the
    /// same thing PlayerPositionPublisher and PlayerActionPublisher already said
    /// into ECS before there was a network: a position, an aim and which keys
    /// are down. What was in reach, whether a cooldown is up and how many
    /// projectiles come out stay on the host.
    ///
    /// The position is trusted. ponytail: fine for coop among friends; a server
    /// that moves the body from the input instead is the upgrade if cheating
    /// ever matters.
    /// </summary>
    public struct PlayerCommand : ICommandData
    {
        public NetworkTick Tick { get; set; }

        public float3 Position;
        public float3 Facing;
        public float3 AimDirection;
        public float3 AimPoint;

        /// <summary>Bit per cast slot, set while that slot's button is held.</summary>
        public byte CastHeldMask;

        /// <summary>
        /// How many times Interact has been pressed since connecting. A counter
        /// rather than a flag, so a press between two ticks is not lost and a
        /// command repeated for a missing tick is not a second press.
        /// </summary>
        public uint InteractCount;
    }

    /// <summary>
    /// The ghost that stands for a connected player: owned by their connection,
    /// carries their commands to the host and their position back to everybody
    /// else.
    /// </summary>
    public struct PlayerAvatar : IComponentData
    {
        /// <summary>Host-side: the last InteractCount already turned into a request.</summary>
        public uint SeenInteractCount;
    }

    /// <summary>
    /// The ghost prefabs the host instantiates at runtime, baked from
    /// CharacterStats in every scene. Everything else that is a ghost comes out
    /// of a pool whose prefab another database already carries.
    /// </summary>
    public struct NetworkPrefabs : IComponentData
    {
        public Entity Avatar;
        public Entity Character;
        public Entity Bag;
        public Entity VendorShelf;
    }
}
