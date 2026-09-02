using Unity.Entities;

namespace TogetherWeFall.Shared
{
    /// <summary>
    /// Pathfinding tuning as ECS data.
    ///
    /// The systems read this singleton rather than the ScriptableObject
    /// directly: a managed asset reference cannot cross into a Burst job, and
    /// more importantly, a server would receive these numbers from its own
    /// config rather than from a client's assets.
    /// </summary>
    public struct PathfindingSettings : IComponentData
    {
        public int MaxRequestsPerFrame;
        public float RepathInterval;
        public float MinRepathInterval;
        public float RepathDistanceThreshold;
        public int MaxCorners;
        public float CornerReachRadius;
        public float NavMeshSampleDistance;
    }

    /// <summary>
    /// Crowd separation tuning as ECS data.
    ///
    /// CellSize is stored already clamped to at least Radius: the neighbour
    /// search only scans the 3x3 block of cells around an enemy, so a smaller
    /// cell would silently miss neighbours that are still inside the radius.
    /// Clamping at bake time means the invariant is established once, rather
    /// than re-checked in the inner loop of a job that runs per enemy per frame.
    /// </summary>
    public struct SeparationSettings : IComponentData
    {
        public float Radius;
        public float Strength;
        public float CellSize;
        public int MaxNeighbors;
    }
}
