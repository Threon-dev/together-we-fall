using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Interaction;

namespace TogetherWeFall.Loot.Authoring
{
    /// <summary>
    /// Bakes the chest prefab.
    ///
    /// The loot table id is left at zero here and written when the chest is
    /// placed: the same prefab serves a treasure room today and a boss reward
    /// tomorrow, and which table it rolls on is a property of where it stands,
    /// not of what it is made of.
    /// </summary>
    public sealed class ChestAuthoring : MonoBehaviour
    {
        [SerializeField] private LootConfig _config;

        public LootConfig Config => _config;

        private sealed class ChestBaker : Baker<ChestAuthoring>
        {
            public override void Bake(ChestAuthoring authoring)
            {
                if (authoring.Config == null)
                {
                    Debug.LogError(
                        $"[{nameof(ChestAuthoring)}] No LootConfig assigned — the chest would " +
                        "have no interaction radius and could never be opened.", authoring);
                    return;
                }

                DependsOn(authoring.Config);

                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<ChestTag>(entity);

                AddComponent(entity, new ChestState
                {
                    Phase = ChestPhase.Closed,
                    OpenTimer = 0f,
                    OpenDuration = authoring.Config.ChestOpenSeconds,

                    // Nobody has won the race yet, and -1 is not a player id.
                    OpenedByPlayerId = -1
                });

                AddComponent(entity, new LootTableId { Value = 0 });

                AddComponent<InteractableTag>(entity);
                AddComponent(entity, new InteractionRadius
                {
                    Value = authoring.Config.ChestInteractionRadius
                });

                // Both flags start down: nothing has claimed the chest and it
                // owes nothing yet. Adding them at bake time means claiming and
                // rolling later cost no structural change.
                AddComponent<InteractionTriggered>(entity);
                SetComponentEnabled<InteractionTriggered>(entity, false);

                AddComponent<LootRollRequest>(entity);
                SetComponentEnabled<LootRollRequest>(entity, false);
            }
        }
    }
}
