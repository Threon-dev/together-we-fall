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

        [Tooltip("Seconds of damage history the meter keeps. The window every " +
                 "rate on the panel is worked out over, so short enough to " +
                 "react and long enough that one slow skill is not a spike.")]
        [SerializeField, Range(1f, 30f)] private float _meterWindow = 6f;

        [Tooltip("How far it slides to each side. Zero is a dummy that stands " +
                 "still, which is most of them — a moving target is for " +
                 "checking that a projectile skill leads properly.")]
        [SerializeField, Min(0f)] private float _patrolDistance;

        [Tooltip("Full sweeps per second.")]
        [SerializeField, Range(0.05f, 2f)] private float _patrolFrequency = 0.25f;

        [Tooltip("Which way it slides, in world space. Normalised at bake time.")]
        [SerializeField] private Vector3 _patrolAxis = Vector3.right;

        public float MaxHealth => _maxHealth;
        public float FlashSeconds => _flashSeconds;
        public float MeterWindow => _meterWindow;
        public float PatrolDistance => _patrolDistance;
        public float PatrolFrequency => _patrolFrequency;
        public Vector3 PatrolAxis => _patrolAxis;

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

                // Every dummy is metered. A dummy exists to be measured against,
                // and one that took damage without recording it would be the odd
                // one out for no reason a player could see.
                AddComponent(entity, new DamageMeter { Window = authoring.MeterWindow });
                AddBuffer<DamageMeterEntry>(entity);

                // Present and down, so clearing the log later costs no
                // structural change — the same reason the claim flag is baked
                // onto a chest.
                AddComponent<DamageMeterReset>(entity);
                SetComponentEnabled<DamageMeterReset>(entity, false);

                AddPatrol(entity, authoring);
            }

            /// <summary>
            /// Only for a dummy that was asked to move. Without the component no
            /// system touches its transform, which is how a still dummy stays
            /// still with no branch anywhere.
            /// </summary>
            private void AddPatrol(Entity entity, TrainingDummyAuthoring authoring)
            {
                if (authoring.PatrolDistance <= 0f)
                    return;

                float3 axis = authoring.PatrolAxis;

                // A zero axis would be a dummy that patrols nowhere while still
                // paying for a job. Falling back beats refusing: the author asked
                // for movement.
                if (math.lengthsq(axis) < 1e-6f)
                    axis = new float3(1f, 0f, 0f);

                AddComponent(entity, new DummyPatrol
                {
                    Origin = authoring.transform.position,
                    Axis = math.normalize(axis),
                    Distance = authoring.PatrolDistance,
                    Frequency = authoring.PatrolFrequency
                });
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
