using Unity.Entities;
using UnityEngine;

namespace TogetherWeFall.Network.Authoring
{
    /// <summary>
    /// The avatar ghost prefab. Sits beside a GhostAuthoringComponent with an
    /// owner, and adds what makes it a player rather than any owned ghost: the
    /// command buffer the client fills and the host reads.
    /// </summary>
    public sealed class PlayerAvatarAuthoring : MonoBehaviour
    {
        private sealed class PlayerAvatarBaker : Baker<PlayerAvatarAuthoring>
        {
            public override void Bake(PlayerAvatarAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<PlayerAvatar>(entity);
                AddBuffer<PlayerCommand>(entity);
            }
        }
    }
}
