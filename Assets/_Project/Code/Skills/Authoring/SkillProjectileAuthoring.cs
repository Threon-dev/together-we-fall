using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace TogetherWeFall.Skills.Authoring
{
    /// <summary>
    /// Bakes the projectile prefab.
    ///
    /// Every value here is a placeholder: the cast writes the real ones the
    /// moment it instantiates one. What baking buys is the archetype — a
    /// projectile comes out of the prefab with its spent flag and its colour
    /// override already attached, so firing one is a copy rather than a copy
    /// plus three structural changes.
    ///
    /// One prefab for every element, tinted per instance, for the same reason
    /// the dropped items are: six prefabs would be six things to keep in sync
    /// for what is one property.
    /// </summary>
    public sealed class SkillProjectileAuthoring : MonoBehaviour
    {
        private sealed class SkillProjectileBaker : Baker<SkillProjectileAuthoring>
        {
            public override void Bake(SkillProjectileAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new SkillProjectile
                {
                    Lifetime = 1f,
                    HitRadius = 0.7f,

                    // Said out loud because zero is a real skill index. Every
                    // field here is overwritten when one is fired, but a prefab
                    // that would cast a skill if anyone forgot is a bad prefab.
                    TriggerSkillIndex = -1
                });

                // Down until the projectile finishes, so retiring one costs no
                // structural change in the middle of a flight.
                AddComponent<ProjectileSpent>(entity);
                SetComponentEnabled<ProjectileSpent>(entity, false);

                // Down because a pooled projectile starts life idle. The pool
                // creates them all at startup and raises this to fire one.
                AddComponent<ProjectileActive>(entity);
                SetComponentEnabled<ProjectileActive>(entity, false);

                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}
