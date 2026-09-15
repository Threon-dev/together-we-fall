using Unity.Entities;
using UnityEngine;

namespace TogetherWeFall.Network.Authoring
{
    /// <summary>
    /// Carries the runtime ghost prefabs into ECS.
    ///
    /// Added to the CharacterStats object by SceneBuildUtility rather than given
    /// an object of its own: every scene already puts CharacterStats into its
    /// SubScene, so these arrive without a new manual step.
    /// </summary>
    public sealed class NetworkPrefabsAuthoring : MonoBehaviour
    {
        [SerializeField] private GameObject _avatar;
        [SerializeField] private GameObject _character;
        [SerializeField] private GameObject _bag;
        [SerializeField] private GameObject _vendorShelf;

        public GameObject Avatar => _avatar;
        public GameObject Character => _character;
        public GameObject Bag => _bag;
        public GameObject VendorShelf => _vendorShelf;

        private sealed class NetworkPrefabsBaker : Baker<NetworkPrefabsAuthoring>
        {
            public override void Bake(NetworkPrefabsAuthoring authoring)
            {
                if (authoring.Avatar == null || authoring.Character == null ||
                    authoring.Bag == null || authoring.VendorShelf == null)
                {
                    Debug.LogError(
                        $"[{nameof(NetworkPrefabsAuthoring)}] A network prefab is missing — " +
                        "rebuild the scene so players get a body, a sheet, a bag and a shop.",
                        authoring);
                    return;
                }

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new NetworkPrefabs
                {
                    Avatar = GetEntity(authoring.Avatar, TransformUsageFlags.Dynamic),
                    Character = GetEntity(authoring.Character, TransformUsageFlags.None),
                    Bag = GetEntity(authoring.Bag, TransformUsageFlags.None),
                    VendorShelf = GetEntity(authoring.VendorShelf, TransformUsageFlags.None)
                });
            }
        }
    }
}
