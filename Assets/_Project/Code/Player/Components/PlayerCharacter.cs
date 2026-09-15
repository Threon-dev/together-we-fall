using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// The player, as an entity.
    ///
    /// Up to now a player existed in ECS only as a position in a buffer, which
    /// was enough while the only question anyone asked was "who do I chase".
    /// Equipment asks a different question — "what is this player wearing" —
    /// and that needs something to hang state on.
    ///
    /// The two are deliberately not merged. PlayerPositionElement is a flat,
    /// cheap targeting index that enemy jobs read every frame; this is the
    /// character sheet. Merging them would put an inventory buffer in the hot
    /// path of every enemy on the floor.
    ///
    /// Nothing creates these by hand: PlayerCharacterRegistrySystem derives them
    /// from the position buffer, so a player joining in coop gets a character
    /// without anybody having to remember to make one.
    /// </summary>
    public struct PlayerCharacter : IComponentData
    {
        [GhostField]
        public int PlayerId;
    }

    /// <summary>
    /// The host putting a body somewhere it did not walk.
    ///
    /// The transform belongs to a CharacterController on a GameObject, so the
    /// host cannot write it — it writes this, and PlayerPositionPublisher moves
    /// the body when the version changes. In a networked build that is exactly
    /// a server correction arriving in a snapshot, which is why it lands in the
    /// file a snapshot unpacker will replace.
    /// </summary>
    public struct PlayerWarp : IComponentData
    {
        /// <summary>Raised on every warp. A counter, so two warps to the same spot are still two.</summary>
        [GhostField]
        public uint Version;

        [GhostField]
        public float3 Position;

        /// <summary>Which way to face on arrival. Zero keeps the facing.</summary>
        [GhostField]
        public float3 Facing;

        /// <summary>Whether the host is still holding the body: input neither moves nor turns it.</summary>
        [GhostField]
        public bool Holding;

        /// <summary>
        /// Seconds the body takes to get there. Zero is a teleport; a dash slides.
        /// Every writer sets it, because the component keeps the last warp's value.
        /// </summary>
        [GhostField]
        public float SlideSeconds;
    }
}
