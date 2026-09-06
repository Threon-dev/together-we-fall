using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using TogetherWeFall.Interaction;
using TogetherWeFall.Inventory;

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
    ///
    /// Since the grid inventory, a carried item is the same entity as the drop
    /// it came from — picking something up no longer returns it here. So free
    /// now means two flags down rather than one: not on the floor
    /// (InteractableTag) and not owned by anybody (ItemStored). The pool size is
    /// therefore the ceiling on every item that exists at once, in the world and
    /// in every bag, which is the same bargain the enemy pool makes with
    /// MaxAlive.
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

                entityManager.SetComponentData(item, new ItemGridPlacement
                {
                    ContainerEntity = Entity.Null
                });

                // Last, for the same reason as everywhere else: the moment this
                // goes up the resolver can hand it to somebody.
                entityManager.SetComponentEnabled<InteractableTag>(item, true);
            }

            return count;
        }

        /// <summary>
        /// Takes an item off the floor and into somebody's keeping.
        ///
        /// Not a release: the entity is still very much in use, it just has no
        /// presence in the world any more. Parked rather than scaled away for
        /// the same reason as everything else in a pool — a degenerate mesh is
        /// still submitted, and a kilometre below the floor is culled.
        ///
        /// Deliberately does NOT write the placement. Where an item sits in a
        /// grid is GridFit's business, and this is called after that has already
        /// been decided.
        /// </summary>
        public static void Store(EntityManager entityManager, Entity item)
        {
            Park(entityManager, item);

            entityManager.SetComponentEnabled<InteractableTag>(item, false);
            entityManager.SetComponentEnabled<InteractionTriggered>(item, false);
            entityManager.SetComponentEnabled<ItemStored>(item, true);
        }

        public static void Release(EntityManager entityManager, in NativeArray<Entity> taken)
        {
            for (int i = 0; i < taken.Length; i++)
            {
                Park(entityManager, taken[i]);

                entityManager.SetComponentEnabled<InteractableTag>(taken[i], false);
                entityManager.SetComponentEnabled<InteractionTriggered>(taken[i], false);

                // Both flags, or the pool query would never see it again and the
                // item would leak out of a fixed pool one drop at a time.
                entityManager.SetComponentEnabled<ItemStored>(taken[i], false);

                entityManager.SetComponentData(taken[i], new ItemGridPlacement
                {
                    ContainerEntity = Entity.Null
                });
            }
        }

        private static void Park(EntityManager entityManager, Entity item)
        {
            // Read, move, write: the scale was baked and is none of this
            // method's business.
            LocalTransform transform = entityManager.GetComponentData<LocalTransform>(item);

            transform.Position = new float3(0f, ParkDepth, 0f);
            entityManager.SetComponentData(item, transform);
        }
    }
}
