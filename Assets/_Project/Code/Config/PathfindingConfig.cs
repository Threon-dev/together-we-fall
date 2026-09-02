using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Pathfinding tuning. The single most important knob here is
    /// MaxRequestsPerFrame: path queries run on the main thread, so this value
    /// — not the enemy count — is what bounds their per-frame cost.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PathfindingConfig",
        menuName = "Together We Fall/Pathfinding Config")]
    public sealed class PathfindingConfig : ScriptableObject
    {
        [Header("Time slicing")]
        [Tooltip("How many path queries may run in a single frame. Raising it " +
                 "makes enemies react faster and costs main-thread time; the " +
                 "cost is flat in the enemy count, which is the whole point.")]
        [SerializeField, Range(1, 200)] private int _maxRequestsPerFrame = 20;

        [Header("When to recalculate")]
        [Tooltip("Normal interval between recalculations for one enemy.")]
        [SerializeField] private float _repathInterval = 0.5f;

        [Tooltip("Hard floor between two recalculations for the same enemy. " +
                 "Without it an enemy standing off the navmesh would re-request " +
                 "a path every frame and eat the whole budget by itself.")]
        [SerializeField] private float _minRepathInterval = 0.15f;

        [Tooltip("How far the target must move before the path is considered " +
                 "stale, regardless of the interval.")]
        [SerializeField] private float _repathDistanceThreshold = 2f;

        [Header("Path shape")]
        [Tooltip("Upper bound on stored corners. Corners beyond this are " +
                 "dropped; the path is recalculated long before an enemy walks " +
                 "that far anyway.")]
        [SerializeField, Range(2, 64)] private int _maxCorners = 16;

        [Tooltip("How close an enemy must get to a corner to move on to the " +
                 "next one. Too small and enemies stall on corners, hunting for " +
                 "a point they keep overshooting.")]
        [SerializeField] private float _cornerReachRadius = 0.5f;

        [Tooltip("Search radius used to snap a position onto the navmesh. " +
                 "Enemies spawn slightly off the surface, and a raw position " +
                 "would simply fail the query.")]
        [SerializeField] private float _navMeshSampleDistance = 3f;

        public int MaxRequestsPerFrame => _maxRequestsPerFrame;
        public float RepathInterval => _repathInterval;
        public float MinRepathInterval => _minRepathInterval;
        public float RepathDistanceThreshold => _repathDistanceThreshold;
        public int MaxCorners => _maxCorners;
        public float CornerReachRadius => _cornerReachRadius;
        public float NavMeshSampleDistance => _navMeshSampleDistance;
    }
}
