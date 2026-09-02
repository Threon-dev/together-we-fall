using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Enemies.Authoring
{
    /// <summary>
    /// Converts the enemy GameObject prefab into an ECS entity. Parameters come
    /// from EnemyConfig so the numbers live in one place instead of spreading
    /// across prefabs.
    /// </summary>
    public sealed class EnemyAuthoring : MonoBehaviour
    {
        [SerializeField] private EnemyConfig _config;

        public EnemyConfig Config => _config;

        private sealed class EnemyBaker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                if (authoring.Config == null)
                {
                    Debug.LogError(
                        $"[{nameof(EnemyAuthoring)}] No EnemyConfig assigned — " +
                        $"enemy '{authoring.name}' will not move.", authoring);
                    return;
                }

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Read the config through DependsOn, otherwise changing numbers
                // in the SO would not trigger a re-bake and the edits would
                // silently fail to reach the game.
                DependsOn(authoring.Config);

                AddComponent<EnemyTag>(entity);

                AddComponent(entity, new MovementData
                {
                    Speed = authoring.Config.MoveSpeed,
                    RotationSpeed = authoring.Config.RotationSpeed,
                    StoppingDistance = authoring.Config.StoppingDistance,
                    DesiredVelocity = default
                });

                AddComponent(entity, new ChaseTarget { HasTarget = false });
                AddComponent(entity, new PathProgress());
                AddComponent<EnemyPresentation>(entity);

                // Left unmapped: the spawn position is not known at bake time,
                // so the first frame resolves it against the live navmesh.
                AddComponent(entity, new NavMeshAgentLocation { IsMapped = false });

                AddBuffer<PathPoint>(entity);

                // No path computed yet, so the enemy is queued for a recalc from
                // the very first frame. Nothing reads this flag during stage 2.
                AddComponent<NeedsRepath>(entity);
                SetComponentEnabled<NeedsRepath>(entity, true);
            }
        }
    }
}
