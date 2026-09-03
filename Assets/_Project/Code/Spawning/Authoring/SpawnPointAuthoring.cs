using Unity.Entities;
using UnityEngine;

namespace TogetherWeFall.Spawning.Authoring
{
    /// <summary>
    /// Spawn point marker. Placed on empty objects around the arena perimeter —
    /// the position is read from their transform, so the layout can be moved
    /// with the mouse in the scene without touching code.
    /// </summary>
    public sealed class SpawnPointAuthoring : MonoBehaviour
    {
        [Header("Editor visualisation")]
        [SerializeField] private float _gizmoRadius = 1.5f;

        private sealed class SpawnPointBaker : Baker<SpawnPointAuthoring>
        {
            public override void Bake(SpawnPointAuthoring authoring)
            {
                // Renderable is enough — a spawn point draws nothing, it only
                // needs a position.
                Entity entity = GetEntity(TransformUsageFlags.Renderable);

                // Authored points belong to no room, so they answer any order.
                // Dungeon points are created at runtime with a real room id.
                AddComponent(entity, new SpawnPoint { RoomId = SpawnPoint.AnyRoom });
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, _gizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3f);
        }
    }
}
