using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Enemies
{
    /// <summary>
    /// What an enemy looks like before anything has happened to it.
    ///
    /// Baked onto the prefab and never written again, so a body coming back out
    /// of the pool has somewhere to read its original size, colour and health
    /// from. Deriving them from the corpse instead would work exactly once.
    /// </summary>
    public struct EnemySpawnDefaults : IComponentData
    {
        public float MaxHealth;
        public float Scale;
        public float4 BodyColor;
    }

    /// <summary>
    /// Takes enemies out of the pool and puts them back.
    ///
    /// Enemies are the heaviest churn in the game by a wide margin: a hundred
    /// arrive per wave and every one of them is eventually destroyed. Both were
    /// already batched into one call, but each call is a structural change, and
    /// a fight has one nearly every frame.
    ///
    /// EnemyTag being enableable is what makes the pool cost nothing to express.
    /// It already meant "this is an enemy right now" — a corpse lowers it while
    /// it fades — so an idle body in the pool is a state the whole simulation
    /// already knows how to ignore.
    /// </summary>
    public struct EnemyPool
    {
        /// <summary>How far below the world an idle body waits, out of the frustum.</summary>
        public const float ParkDepth = -1000f;

        /// <summary>
        /// Wakes as many bodies as the pool can supply and returns how many that
        /// was. Everything an enemy accumulated in its previous life is reset
        /// here, at the one moment it is certain nothing is reading it.
        /// </summary>
        public static int Activate(
            EntityManager entityManager,
            in NativeArray<Entity> free,
            in NativeArray<float3> positions)
        {
            int count = math.min(free.Length, positions.Length);

            for (int i = 0; i < count; i++)
            {
                Entity enemy = free[i];
                EnemySpawnDefaults defaults =
                    entityManager.GetComponentData<EnemySpawnDefaults>(enemy);

                entityManager.SetComponentData(enemy, new LocalTransform
                {
                    Position = positions[i],
                    Rotation = quaternion.identity,
                    Scale = defaults.Scale
                });

                entityManager.SetComponentData(enemy, new Health
                {
                    Current = defaults.MaxHealth,
                    Max = defaults.MaxHealth
                });

                entityManager.SetComponentData(
                    enemy, new URPMaterialPropertyBaseColor { Value = defaults.BodyColor });

                // The steering state of whoever used this body last. Left alone,
                // a reused enemy would spend its first seconds walking towards
                // where the previous one was going.
                MovementData movement = entityManager.GetComponentData<MovementData>(enemy);
                movement.DesiredVelocity = default;
                entityManager.SetComponentData(enemy, movement);

                entityManager.SetComponentData(enemy, new ChaseTarget { HasTarget = false });
                entityManager.SetComponentData(enemy, new PathProgress());

                // The navmesh foothold belongs to the old position. Clearing it
                // forces the first frame to map against where the body is now.
                entityManager.SetComponentData(
                    enemy, new NavMeshAgentLocation { IsMapped = false });

                entityManager.GetBuffer<PathPoint>(enemy).Clear();

                // Whatever the previous occupant was burning with, and whatever
                // it had recently been stunned by. Left alone, a reused body
                // would come back out of the pool already ignited — and, worse,
                // would react with the next hit it took, or refuse the first
                // stun of its new life because of one the last occupant took.
                entityManager.GetBuffer<ActiveStatusEffect>(enemy).Clear();
                entityManager.GetBuffer<CrowdControlImmunity>(enemy).Clear();

                // The answer those two add up to. The status pass rebuilds it
                // from nothing every frame, but the first frame out of the pool
                // is one the movement systems also read, and a body that came
                // back rooted would spend it standing still.
                entityManager.SetComponentData(enemy, StatusGate.Neutral);
                entityManager.SetComponentData(enemy, new StatusVisual());

                entityManager.SetComponentEnabled<Dead>(enemy, false);
                entityManager.SetComponentEnabled<DeathFade>(enemy, false);
                entityManager.SetComponentEnabled<DamageFeedback>(enemy, false);

                // Queued for a path from its first frame, exactly as a freshly
                // baked enemy is.
                entityManager.SetComponentEnabled<NeedsRepath>(enemy, true);

                // Last: the moment this goes up, every enemy system starts
                // seeing it, and everything above had better already be true.
                entityManager.SetComponentEnabled<EnemyTag>(enemy, true);
            }

            return count;
        }

        /// <summary>
        /// Returns finished bodies to the pool. EnemyTag is already down — it
        /// went down when they died — so all that is left is to park them and
        /// lower the flag that says they are still fading.
        /// </summary>
        public static void Release(EntityManager entityManager, in NativeArray<Entity> spent)
        {
            for (int i = 0; i < spent.Length; i++)
            {
                LocalTransform transform =
                    entityManager.GetComponentData<LocalTransform>(spent[i]);

                transform.Position = new float3(0f, ParkDepth, 0f);
                entityManager.SetComponentData(spent[i], transform);

                entityManager.SetComponentEnabled<DeathFade>(spent[i], false);
            }
        }
    }
}
