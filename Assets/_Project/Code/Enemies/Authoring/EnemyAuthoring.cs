using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using TogetherWeFall.Combat;
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

                float4 bodyColor = ReadBodyColor(authoring);
                float scale = authoring.transform.localScale.x;

                // Down at bake time. Every enemy in the world comes out of the
                // pool, and the pool raises this when a wave orders a body.
                AddComponent<EnemyTag>(entity);
                SetComponentEnabled<EnemyTag>(entity, false);

                // What to restore a reused body to. Written once, read every
                // time one is woken up.
                AddComponent(entity, new EnemySpawnDefaults
                {
                    MaxHealth = authoring.Config.MaxHealth,
                    Scale = scale > 0f ? scale : 1f,
                    BodyColor = bodyColor
                });

                AddComponent(entity, new MovementData
                {
                    Speed = authoring.Config.MoveSpeed,
                    RotationSpeed = authoring.Config.RotationSpeed,
                    StoppingDistance = authoring.Config.StoppingDistance,
                    DesiredVelocity = default
                });

                AddComponent(entity, new Health
                {
                    Current = authoring.Config.MaxHealth,
                    Max = authoring.Config.MaxHealth
                });

                // The buffer everything that wants to hurt this enemy writes
                // into. Baked rather than added on the first hit: adding a
                // component is a structural change, and the first hit is
                // exactly the moment a hundred of them arrive at once.
                AddBuffer<DamageEvent>(entity);

                // What is currently burning, shocking or chilling this body.
                // Baked for the same reason as the damage buffer: adding it on
                // the first status would be a structural change, and the first
                // status arrives in the middle of a fight.
                AddBuffer<ElementalStatus>(entity);

                // Down until it runs out of health, so dying costs no structural
                // change inside the parallel job that resolves damage.
                AddComponent<Dead>(entity);
                SetComponentEnabled<Dead>(entity, false);

                AddComponent(entity, new DeathFade
                {
                    Duration = authoring.Config.DeathFadeSeconds
                });
                SetComponentEnabled<DeathFade>(entity, false);

                // Raised by the resolver on any frame this takes damage, read
                // and lowered by the system that turns it into a number.
                AddComponent<DamageFeedback>(entity);
                SetComponentEnabled<DamageFeedback>(entity, false);

                // A per-instance colour override, so a dying body can darken
                // without every enemy in the world darkening with it. Seeded
                // from the prefab material, which stays the one place the enemy
                // colour is chosen.
                AddComponent(entity, new URPMaterialPropertyBaseColor { Value = bodyColor });

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

            /// <summary>
            /// The prefab material colour, or white if there is nothing to read.
            /// White is a loud failure — every enemy comes out pale — which is
            /// better than a silent one where they all come out black.
            /// </summary>
            private float4 ReadBodyColor(EnemyAuthoring authoring)
            {
                var renderer = authoring.GetComponent<MeshRenderer>();
                Material material = renderer != null ? renderer.sharedMaterial : null;

                if (material == null)
                    return new float4(1f, 1f, 1f, 1f);

                DependsOn(material);

                Color color = material.color;
                return new float4(color.r, color.g, color.b, color.a);
            }
        }
    }
}
