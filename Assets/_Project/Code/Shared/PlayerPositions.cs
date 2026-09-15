using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Shared
{
    /// <summary>
    /// Marker for the entity that holds the player position buffer.
    ///
    /// This is the seam the whole architecture is built around: enemy systems
    /// read ONLY this buffer and have no idea where the data came from. Today
    /// PlayerPositionPublisher fills it from local input; once networking
    /// arrives a snapshot unpacker will, and not a single enemy system changes.
    /// </summary>
    public struct PlayerPositionsSingleton : IComponentData
    {
    }

    /// <summary>
    /// One player's position. A buffer rather than a single field, deliberately:
    /// the project conventions call for multiple players, the difference in code
    /// right now is a dozen lines, and reworking ChaseTargetSystem later would
    /// cost more.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PlayerPositionElement : IBufferElementData
    {
        public int PlayerId;
        public float3 Position;

        /// <summary>
        /// Which way the body faces. Carried so a remote player's body can be
        /// turned the way its owner turned it; no enemy system reads it.
        /// </summary>
        public float3 Facing;

        /// <summary>
        /// Dead or disconnected players stay in the buffer but are not valid
        /// targets. Removing the element would shift indices and break
        /// ChaseTarget references within the same frame.
        /// </summary>
        public bool IsTargetable;
    }
}
