using Unity.Entities;
using UnityEngine.Experimental.AI;

namespace TogetherWeFall.Enemies
{
    /// <summary>
    /// AUTHORITATIVE — the enemy's foothold on the navmesh surface.
    ///
    /// Belongs with the server-only state and is never replicated: a client
    /// needs the resulting position, not the polygon it came from, and polygon
    /// ids are meaningless across processes anyway.
    ///
    /// Kept as persistent state rather than resolved per frame because mapping a
    /// raw position onto the navmesh is a spatial search, while stepping from a
    /// polygon you already stand on is a local walk. Holding the location turns
    /// a per-frame search into a per-frame step.
    /// </summary>
    public struct NavMeshAgentLocation : IComponentData
    {
        public NavMeshLocation Location;

        /// <summary>
        /// False until the first successful mapping, and again whenever the
        /// stored polygon stops being valid — after a navmesh rebuild, or if the
        /// enemy ends up off the surface entirely.
        /// </summary>
        public bool IsMapped;
    }
}
