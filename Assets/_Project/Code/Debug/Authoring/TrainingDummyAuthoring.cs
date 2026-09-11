using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Enemies;

namespace TogetherWeFall.DebugTools.Authoring
{
    /// <summary>
    /// Bakes a training dummy: something to cast at that stays where it is put.
    ///
    /// The component list is the whole design. EnemyTag, so every targeting
    /// query finds it; Health and the damage buffer, so the numbers are real
    /// and land in the damage counter; and nothing else. No MovementData means
    /// no system moves it, no DeathFade means nothing can start removing it,
    /// and no ChaseTarget means it never looks for a player.
    /// </summary>
    public sealed class TrainingDummyAuthoring : MonoBehaviour
    {
        [Tooltip("Restored every frame it is damaged, so it never dies. The " +
                 "number only decides how much of a hit is visible in one frame.")]
        [SerializeField, Min(1f)] private float _maxHealth = 1000f;

        [Tooltip("How long it flashes when hit. Short — the point is to see that " +
                 "something landed, not to light up the arena.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _flashSeconds = 0.12f;

        public float MaxHealth => _maxHealth;
        public float FlashSeconds => _flashSeconds;

        private sealed class TrainingDummyBaker : Baker<TrainingDummyAuthoring>
        {
            public override void Bake(TrainingDummyAuthoring authoring)
            {
                // Dynamic rather than Renderable: the targeting systems read
                // LocalTransform, and Renderable alone would only give the
                // entity a LocalToWorld to be drawn with.
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                float4 restColor = ReadBodyColor(authoring);

                AddComponent<EnemyTag>(entity);

                AddComponent(entity, new Health
                {
                    Current = authoring.MaxHealth,
                    Max = authoring.MaxHealth
                });

                AddBuffer<DamageEvent>(entity);

                // Dummies burn, freeze and stun like anything else. They are
                // the one thing in the game that stands still and never dies,
                // which makes them the only way to watch a status run its whole
                // course, a reaction fire on a body you chose, and a stun be
                // refused because the last one has not finished protecting it.
                AddBuffer<ActiveStatusEffect>(entity);
                AddBuffer<CrowdControlImmunity>(entity);

                AddComponent(entity, StatusGate.Neutral);
                AddComponent<StatusVisual>(entity);

                // Ordinary resistance. A dummy is the baseline everything is
                // measured against, so making it special would defeat it.
                AddComponent(entity, CrowdControlResistance.None);

                // Present so the damage pipeline can raise it in its parallel
                // job, and lowered again the same frame by TrainingDummySystem.
                // A dummy that could not be marked dead would need a special
                // case inside the one system that must never grow one.
                AddComponent<Dead>(entity);
                SetComponentEnabled<Dead>(entity, false);

                // Numbers over a dummy are most of the point of having one.
                AddComponent<DamageFeedback>(entity);
                SetComponentEnabled<DamageFeedback>(entity, false);

                AddComponent(entity, new TrainingDummy
                {
                    FlashDuration = authoring.FlashSeconds,
                    RestColor = restColor
                });

                AddComponent(entity, new URPMaterialPropertyBaseColor { Value = restColor });
            }

            private float4 ReadBodyColor(TrainingDummyAuthoring authoring)
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
