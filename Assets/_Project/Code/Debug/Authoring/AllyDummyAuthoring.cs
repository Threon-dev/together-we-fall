using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using TogetherWeFall.Combat;

namespace TogetherWeFall.DebugTools.Authoring
{
    /// <summary>
    /// A capsule that plays a wounded second player, for testing heals and
    /// buffs. See AllyDummy.
    ///
    /// Not an enemy: no EnemyTag, so no skill aims at it and no room counts it.
    /// It IS a player as far as the enemies are concerned, so a wave will chase
    /// it — exactly as it would chase a partner.
    /// </summary>
    public sealed class AllyDummyAuthoring : MonoBehaviour
    {
        [Tooltip("The player id it plays. Must differ from the real player's (0) " +
                 "and from any other dummy.")]
        [SerializeField, Min(1)] private int _playerId = 1;

        [Tooltip("Share of its life it starts at and drains back to, so there is " +
                 "always something to heal.")]
        [SerializeField, Range(0.05f, 1f)] private float _woundedFraction = 0.35f;

        [Tooltip("Share of its maximum life lost per second while above the " +
                 "wounded share. How long a heal stays visible.")]
        [SerializeField, Range(0f, 0.5f)] private float _drainPerSecond = 0.04f;

        private sealed class AllyDummyBaker : Baker<AllyDummyAuthoring>
        {
            public override void Bake(AllyDummyAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                float4 restColor = ReadBodyColor(authoring);

                AddComponent(entity, new AllyDummy
                {
                    PlayerId = authoring._playerId,
                    WoundedFraction = authoring._woundedFraction,
                    DrainPerSecond = authoring._drainPerSecond,
                    RestColor = restColor
                });

                AddComponent(entity, new URPMaterialPropertyBaseColor { Value = restColor });

                // The character's numbers are mirrored here, because a character
                // has no position to draw them at.
                AddComponent<DamageFeedback>(entity);
                SetComponentEnabled<DamageFeedback>(entity, false);
            }

            private float4 ReadBodyColor(AllyDummyAuthoring authoring)
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
