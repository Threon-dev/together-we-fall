using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Interaction;

namespace TogetherWeFall.Lobby.Authoring
{
    /// <summary>
    /// Bakes one lobby NPC.
    ///
    /// One authoring component for all four kinds rather than four, because
    /// three quarters of what an NPC is — interactable, a radius, a claim flag —
    /// is the same for every one of them, and the differing quarter is a handful
    /// of numbers. Four bakers would have meant four copies of the interaction
    /// wiring, which is exactly the part that must not drift.
    ///
    /// The fields that do not belong to the chosen service are simply not read.
    /// That is worth being explicit about: an inspector showing a price list on
    /// a portal is untidy, and a custom editor to hide them is a custom editor
    /// to maintain.
    /// </summary>
    public sealed class LobbyNpcAuthoring : MonoBehaviour
    {
        [SerializeField] private NpcServiceType _service = NpcServiceType.Vendor;

        [Tooltip("How close the player has to stand. Generous compared to a " +
                 "chest: an NPC is walked up to deliberately, not brushed past.")]
        [SerializeField, Min(0.5f)] private float _interactionRadius = 3f;

        [Header("Vendor")]
        [Tooltip("What this vendor sells. Fixed for the session — a stock that " +
                 "rotates is a feature on top of this one. Every entry also " +
                 "costs one entity out of the item pool for as long as it sits " +
                 "on the shelf.")]
        [SerializeField] private ItemDefinition[] _stock = Array.Empty<ItemDefinition>();

        [SerializeField, Range(1, 12)] private int _stockWidth = 6;
        [SerializeField, Range(1, 12)] private int _stockHeight = 5;

        [Tooltip("What the player pays, against the base price for the rarity.")]
        [SerializeField, Range(0.5f, 5f)] private float _buyPriceMultiplier = 1.5f;

        [Tooltip("What the player receives. Below one on purpose: buying back " +
                 "what you just sold has to cost something, or the shop is a " +
                 "place to launder cells.")]
        [SerializeField, Range(0.05f, 1f)] private float _sellPriceMultiplier = 0.4f;

        [Header("Crafting")]
        [SerializeField, Min(0)] private int _addSocketCost = 8;
        [SerializeField, Min(0)] private int _linkSocketCost = 5;
        [SerializeField, Min(0)] private int _rerollSkillsCost = 12;

        [Header("Dungeon portal")]
        [Tooltip("The scene to load once every player in the lobby is ready. " +
                 "Must also be in the build settings.")]
        [SerializeField] private string _targetScene = "Dungeon";

        public NpcServiceType Service => _service;
        public float InteractionRadius => _interactionRadius;
        public ItemDefinition[] Stock => _stock ?? Array.Empty<ItemDefinition>();
        public int StockWidth => _stockWidth;
        public int StockHeight => _stockHeight;
        public float BuyPriceMultiplier => _buyPriceMultiplier;
        public float SellPriceMultiplier => _sellPriceMultiplier;
        public int AddSocketCost => _addSocketCost;
        public int LinkSocketCost => _linkSocketCost;
        public int RerollSkillsCost => _rerollSkillsCost;
        public string TargetScene => _targetScene;

        private sealed class LobbyNpcBaker : Baker<LobbyNpcAuthoring>
        {
            public override void Bake(LobbyNpcAuthoring authoring)
            {
                // Dynamic rather than Renderable: the interaction resolver reads
                // LocalTransform to work out what is in reach, and Renderable
                // alone gives an entity only a LocalToWorld to be drawn with.
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new NpcService { Type = authoring.Service });

                AddComponent<InteractableTag>(entity);
                AddComponent(entity, new InteractionRadius { Value = authoring.InteractionRadius });

                // Down at bake time, so a claim later costs no structural
                // change. Same as the chest, and for the same reason.
                AddComponent<InteractionTriggered>(entity);
                SetComponentEnabled<InteractionTriggered>(entity, false);

                switch (authoring.Service)
                {
                    case NpcServiceType.Vendor:
                        BakeVendor(entity, authoring);
                        break;

                    case NpcServiceType.Crafting:
                        AddComponent(entity, new CraftingStation
                        {
                            AddSocketCost = authoring.AddSocketCost,
                            LinkSocketCost = authoring.LinkSocketCost,
                            RerollSkillsCost = authoring.RerollSkillsCost
                        });
                        break;

                    case NpcServiceType.DungeonPortal:
                        BakePortal(entity, authoring);
                        break;
                }
            }

            private void BakeVendor(Entity entity, LobbyNpcAuthoring authoring)
            {
                AddComponent(entity, new VendorComponent
                {
                    // Built on the first update: a container is an entity with a
                    // cell buffer sized to its grid, and a baker has no business
                    // creating a second entity to hold a runtime pool item.
                    StockContainer = Entity.Null,

                    StockWidth = authoring.StockWidth,
                    StockHeight = authoring.StockHeight,
                    BuyPriceMultiplier = authoring.BuyPriceMultiplier,
                    SellPriceMultiplier = authoring.SellPriceMultiplier
                });

                DynamicBuffer<VendorStockEntry> stock = AddBuffer<VendorStockEntry>(entity);

                ItemDefinition[] authored = authoring.Stock;

                for (int i = 0; i < authored.Length; i++)
                {
                    if (authored[i] == null)
                        continue;

                    DependsOn(authored[i]);

                    // By id, the same FNV hash everything else refers to an item
                    // by. The stock list and the item database are baked by two
                    // authoring objects that share no ordering.
                    stock.Add(new VendorStockEntry { ItemId = authored[i].ItemId });
                }

                if (stock.Length == 0)
                {
                    Debug.LogWarning(
                        $"[{nameof(LobbyNpcAuthoring)}] Vendor '{authoring.name}' has no stock — " +
                        "it will open an empty shelf. Assign items, or make it a different " +
                        "kind of NPC.", authoring);
                }
            }

            private void BakePortal(Entity entity, LobbyNpcAuthoring authoring)
            {
                var target = new FixedString32Bytes();

                if (!string.IsNullOrWhiteSpace(authoring.TargetScene))
                    target = authoring.TargetScene;

                AddComponent(entity, new DungeonPortal { TargetScene = target });

                // The ready list and the transition live on the portal rather
                // than on a lobby singleton: the portal is the thing being
                // agreed about, and a singleton would be a second entity to
                // create, find and keep in step with this one.
                AddBuffer<LobbyReadyPlayer>(entity);
                AddBuffer<PortalReadyRequest>(entity);

                AddComponent(entity, new SceneTransition
                {
                    Target = target,
                    Phase = SceneTransitionPhase.Idle
                });
            }
        }
    }
}
