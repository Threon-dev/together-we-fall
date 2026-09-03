using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Interaction;

namespace TogetherWeFall.Loot
{
    /// <summary>One item waiting to be dropped into the world.</summary>
    public struct ItemDrop
    {
        public float3 Position;
        public int ItemId;
        public ItemRarity Rarity;
        public FixedString64Bytes Name;
        public float4 Color;
    }

    /// <summary>
    /// Takes dropped items out of the pool and puts them back.
    ///
    /// Less churn than enemies or projectiles — a chest drops a handful — but
    /// the same shape of cost, and a treasure room opened in a hurry is a burst
    /// of creates followed by a burst of destroys. Pooling it costs one file and
    /// removes the category of problem entirely rather than arguing about where
    /// the threshold is.
    ///
    /// InteractableTag being enableable is what makes an idle item free: it
    /// already meant "this can be picked up right now", so a pooled item is a
    /// state the interaction resolver was written to ignore.
    /// </summary>
    public struct LootItemPool
    {
        /// <summary>How far below the world an idle item waits, out of the frustum.</summary>
        public const float ParkDepth = -1000f;

        public static int Activate(
            EntityManager entityManager,
            in NativeArray<Entity> free,
            NativeList<ItemDrop> drops)
        {
            int count = math.min(free.Length, drops.Length);

            for (int i = 0; i < count; i++)
            {
                Entity item = free[i];

                // Read, move, write: the scale was baked and is none of this
                // method's business.
                LocalTransform transform = entityManager.GetComponentData<LocalTransform>(item);
                transform.Position = drops[i].Position;
                entityManager.SetComponentData(item, transform);

                entityManager.SetComponentData(item, new ItemInstance
                {
                    ItemId = drops[i].ItemId,
                    Rarity = drops[i].Rarity,

                    // Everything in the dungeon is at risk, found or carried in.
                    RiskState = ItemRiskState.AtRisk
                });

                entityManager.SetComponentData(
                    item, new ItemDisplayName { Value = drops[i].Name });

                entityManager.SetComponentData(
                    item, new URPMaterialPropertyBaseColor { Value = drops[i].Color });

                entityManager.SetComponentEnabled<InteractionTriggered>(item, false);

                // Last, for the same reason as everywhere else: the moment this
                // goes up the resolver can hand it to somebody.
                entityManager.SetComponentEnabled<InteractableTag>(item, true);
            }

            return count;
        }

        public static void Release(EntityManager entityManager, in NativeArray<Entity> taken)
        {
            for (int i = 0; i < taken.Length; i++)
            {
                LocalTransform transform =
                    entityManager.GetComponentData<LocalTransform>(taken[i]);

                transform.Position = new float3(0f, ParkDepth, 0f);
                entityManager.SetComponentData(taken[i], transform);

                entityManager.SetComponentEnabled<InteractableTag>(taken[i], false);
                entityManager.SetComponentEnabled<InteractionTriggered>(taken[i], false);
            }
        }
    }
}
