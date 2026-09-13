using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// A body standing in for a second player, so team spells have someone to
    /// land on.
    ///
    /// Not an ally entity of its own kind: it publishes a position under a
    /// player id of its own, so PlayerCharacterRegistrySystem gives it a real
    /// character — health, stats, statuses — and a heal aimed at it takes
    /// exactly the path a heal on a coop partner will. This entity is only the
    /// capsule that path is drawn on.
    /// </summary>
    public struct AllyDummy : IComponentData
    {
        public int PlayerId;

        /// <summary>The share of its life it sits at: where it starts, and where it drains back to.</summary>
        public float WoundedFraction;

        /// <summary>Share of its maximum life lost per second while above that.</summary>
        public float DrainPerSecond;

        public float4 RestColor;

        /// <summary>Whether it has been knocked down to wounded yet. Once, when its sheet first has a size.</summary>
        public bool Primed;
    }
}
