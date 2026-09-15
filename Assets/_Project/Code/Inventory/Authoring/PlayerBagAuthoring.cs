using Unity.Entities;
using UnityEngine;

namespace TogetherWeFall.Inventory.Authoring
{
    /// <summary>
    /// A player's bag as a ghost prefab: a grid, its cells and an owner.
    ///
    /// Sized and owned at runtime by PlayerCharacterRegistrySystem, which is
    /// where the configured bag size and the player are known. A ghost so the
    /// owner's inventory panel draws the cells the host actually holds.
    /// </summary>
    public sealed class PlayerBagAuthoring : MonoBehaviour
    {
        private sealed class PlayerBagBaker : Baker<PlayerBagAuthoring>
        {
            public override void Bake(PlayerBagAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent<InventoryGridComponent>(entity);
                AddBuffer<InventoryCell>(entity);
                AddComponent<ContainerOwner>(entity);
            }
        }
    }
}
