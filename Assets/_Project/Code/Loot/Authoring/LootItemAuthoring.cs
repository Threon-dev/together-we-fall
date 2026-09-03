using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Interaction;

namespace TogetherWeFall.Loot.Authoring
{
    /// <summary>
    /// Bakes the dropped-item prefab.
    ///
    /// One prefab for every rarity, tinted per instance through
    /// URPMaterialPropertyBaseColor. Six prefabs would mean six things to keep
    /// in sync for what is one property; baking the override component here
    /// means the colour is a value the drop system writes, not an asset it has
    /// to pick.
    /// </summary>
    public sealed class LootItemAuthoring : MonoBehaviour
    {
        [SerializeField] private LootConfig _config;

        public LootConfig Config => _config;

        private sealed class LootItemBaker : Baker<LootItemAuthoring>
        {
            public override void Bake(LootItemAuthoring authoring)
            {
                if (authoring.Config == null)
                {
                    Debug.LogError(
                        $"[{nameof(LootItemAuthoring)}] No LootConfig assigned — dropped items " +
                        "would have no pickup radius and could never be picked up.", authoring);
                    return;
                }

                DependsOn(authoring.Config);

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Placeholder values: every field here is overwritten the moment
                // the drop system instantiates one. Born AtRisk because anything
                // in the dungeon is, whether it was found here or carried in.
                AddComponent(entity, new ItemInstance
                {
                    ItemId = 0,
                    Rarity = ItemRarity.Common,
                    RiskState = ItemRiskState.AtRisk
                });

                AddComponent(entity, new ItemDisplayName());

                // Down at bake time: every item in the world comes out of the
                // pool, and the drop system raises this when one is rolled.
                AddComponent<InteractableTag>(entity);
                SetComponentEnabled<InteractableTag>(entity, false);
                AddComponent(entity, new InteractionRadius
                {
                    Value = authoring.Config.ItemPickupRadius
                });

                AddComponent<InteractionTriggered>(entity);
                SetComponentEnabled<InteractionTriggered>(entity, false);

                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1f, 1f, 1f, 1f)
                });
            }
        }
    }
}
