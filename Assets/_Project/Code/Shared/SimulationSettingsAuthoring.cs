using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Shared.Authoring
{
    /// <summary>
    /// Bakes simulation configs into ECS singletons. Lives in the SubScene
    /// beside the wave spawner.
    ///
    /// One authoring object for all simulation settings rather than one per
    /// system: these numbers are tuned together during a profiling pass, and
    /// scattering them across objects would make that pass an object hunt.
    /// </summary>
    public sealed class SimulationSettingsAuthoring : MonoBehaviour
    {
        [SerializeField] private PathfindingConfig _pathfinding;
        [SerializeField] private SeparationConfig _separation;

        public PathfindingConfig Pathfinding => _pathfinding;
        public SeparationConfig Separation => _separation;

        private sealed class SimulationSettingsBaker : Baker<SimulationSettingsAuthoring>
        {
            public override void Bake(SimulationSettingsAuthoring authoring)
            {
                if (authoring.Pathfinding == null || authoring.Separation == null)
                {
                    Debug.LogError(
                        $"[{nameof(SimulationSettingsAuthoring)}] PathfindingConfig or " +
                        "SeparationConfig is not assigned — enemy systems will not run.",
                        authoring);
                    return;
                }

                DependsOn(authoring.Pathfinding);
                DependsOn(authoring.Separation);

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new PathfindingSettings
                {
                    MaxRequestsPerFrame = authoring.Pathfinding.MaxRequestsPerFrame,
                    RepathInterval = authoring.Pathfinding.RepathInterval,
                    MinRepathInterval = authoring.Pathfinding.MinRepathInterval,
                    RepathDistanceThreshold = authoring.Pathfinding.RepathDistanceThreshold,
                    MaxCorners = authoring.Pathfinding.MaxCorners,
                    CornerReachRadius = authoring.Pathfinding.CornerReachRadius,
                    NavMeshSampleDistance = authoring.Pathfinding.NavMeshSampleDistance
                });

                AddComponent(entity, new SeparationSettings
                {
                    Radius = authoring.Separation.Radius,
                    Strength = authoring.Separation.Strength,

                    // Enforced here rather than trusted from the inspector, so a
                    // careless cell size degrades performance at worst and never
                    // silently drops neighbours out of the search.
                    CellSize = math.max(authoring.Separation.CellSize, authoring.Separation.Radius),
                    MaxNeighbors = authoring.Separation.MaxNeighbors
                });
            }
        }
    }
}
