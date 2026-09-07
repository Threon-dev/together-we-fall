using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Skills.Authoring
{
    /// <summary>
    /// Bakes the zone prefab.
    ///
    /// Every value here is a placeholder, exactly as on the projectile: the cast
    /// writes the real ones when a zone is put on the ground. What baking buys is
    /// the archetype, so lighting a wall of fire is a handful of component writes
    /// rather than a structural change in the middle of a fight.
    ///
    /// One prefab for every element, tinted per instance. Five discs would be
    /// five things to keep in step for what is one property.
    /// </summary>
    public sealed class ElementZoneAuthoring : MonoBehaviour
    {
        private sealed class ElementZoneBaker : Baker<ElementZoneAuthoring>
        {
            public override void Bake(ElementZoneAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new ElementZone
                {
                    Element = DamageType.Fire,
                    Radius = 1f,
                    TickInterval = 0.5f
                });

                // Down because a pooled zone starts life idle, like a projectile.
                // The pool creates them all at startup and raises this to light
                // one.
                AddComponent<ZoneActive>(entity);
                SetComponentEnabled<ZoneActive>(entity, false);

                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}
