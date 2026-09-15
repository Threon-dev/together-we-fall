using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Inventory;

namespace TogetherWeFall.Lobby.Authoring
{
    /// <summary>
    /// A vendor's shelf as a ghost prefab: a grid and its cells, and — on
    /// purpose — no ContainerOwner (see VendorStockSystem). Tagged, because the
    /// vendor NPC is not a ghost and a client cannot learn the shelf through it.
    /// </summary>
    public sealed class VendorShelfAuthoring : MonoBehaviour
    {
        private sealed class VendorShelfBaker : Baker<VendorShelfAuthoring>
        {
            public override void Bake(VendorShelfAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent<InventoryGridComponent>(entity);
                AddBuffer<InventoryCell>(entity);
                AddComponent<VendorShelf>(entity);
            }
        }
    }
}
