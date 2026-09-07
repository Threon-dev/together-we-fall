using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Skills
{
    /// <summary>
    /// One projectile waiting to be put into the air.
    ///
    /// Shared between the cast system, which fires them, and the projectile
    /// system, which forks them. Both need to describe a projectile before it
    /// flies, and both take theirs from the same pool.
    /// </summary>
    public struct ProjectileSpawn
    {
        /// <summary>
        /// How far below the world an idle projectile waits.
        ///
        /// Parked rather than shrunk: a degenerate mesh is still submitted,
        /// while something a kilometre under the floor is thrown out by the
        /// frustum. Parking also leaves the baked scale alone, so nothing has to
        /// remember what size a projectile is supposed to be.
        /// </summary>
        public const float ParkDepth = -1000f;

        public float3 Position;
        public SkillProjectile Projectile;

        /// <summary>
        /// Puts as many of these into the air as the pool can supply, and
        /// returns how many that was.
        ///
        /// Nothing is created here. Every projectile already exists; firing one
        /// is a handful of component writes and one flag, none of which is a
        /// structural change. Running out is not an error — the pool size is the
        /// designed ceiling on projectiles in flight, and the shots that do not
        /// fit are the ones nobody could have followed anyway.
        /// </summary>
        public static int ActivateAll(
            EntityManager entityManager,
            in NativeArray<Entity> free,
            NativeList<ProjectileSpawn> spawns)
        {
            int count = math.min(free.Length, spawns.Length);

            for (int i = 0; i < count; i++)
            {
                Entity projectile = free[i];

                // Read, move, write: the scale and rotation were baked onto the
                // prefab and are none of this method's business.
                LocalTransform transform =
                    entityManager.GetComponentData<LocalTransform>(projectile);

                transform.Position = spawns[i].Position;
                entityManager.SetComponentData(projectile, transform);

                entityManager.SetComponentData(projectile, spawns[i].Projectile);

                // Coloured by damage type, so an elemental conversion is visible
                // in the air rather than only in the numbers.
                entityManager.SetComponentData(projectile, new URPMaterialPropertyBaseColor
                {
                    Value = DamageTypePalette.For(spawns[i].Projectile.Type)
                });

                entityManager.SetComponentEnabled<ProjectileSpent>(projectile, false);
                entityManager.SetComponentEnabled<ProjectileActive>(projectile, true);
            }

            return count;
        }

        /// <summary>
        /// Takes a projectile out of the air and back into the pool. Also not a
        /// structural change: it goes under the floor and its flag goes down.
        /// </summary>
        public static void Release(EntityManager entityManager, in NativeArray<Entity> spent)
        {
            for (int i = 0; i < spent.Length; i++)
            {
                LocalTransform transform =
                    entityManager.GetComponentData<LocalTransform>(spent[i]);

                transform.Position = new float3(0f, ParkDepth, 0f);
                entityManager.SetComponentData(spent[i], transform);

                entityManager.SetComponentEnabled<ProjectileActive>(spent[i], false);
                entityManager.SetComponentEnabled<ProjectileSpent>(spent[i], false);
            }
        }
    }

    /// <summary>
    /// What each damage type looks like.
    ///
    /// A type with one static method rather than a static class of constants:
    /// the project has no static classes, and this is the kind of table that
    /// wants to sit next to nothing else.
    /// </summary>
    public struct DamageTypePalette
    {
        public static float4 For(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return new float4(1f, 0.45f, 0.12f, 1f);
                case DamageType.Cold: return new float4(0.35f, 0.75f, 1f, 1f);
                case DamageType.Lightning: return new float4(0.95f, 0.9f, 0.25f, 1f);
                case DamageType.Chaos: return new float4(0.7f, 0.25f, 0.85f, 1f);
                default: return new float4(0.85f, 0.85f, 0.9f, 1f);
            }
        }
    }
}
