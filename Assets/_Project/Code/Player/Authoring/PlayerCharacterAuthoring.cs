using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Player.Authoring
{
    /// <summary>
    /// The character sheet as a ghost prefab: everything a player is that is not
    /// their position.
    ///
    /// This used to be an archetype PlayerCharacterRegistrySystem built at
    /// runtime. A ghost has to come out of a baked prefab, so the list moved
    /// here, with the same starting values; the registry now only decides who
    /// gets one and writes their id and bag.
    ///
    /// Only some of this is replicated — whatever carries [GhostField]. The
    /// request and result queues are on the client's copy too, unreplicated, and
    /// that is the point: the panels queue on the sheet they are drawing, and
    /// CharacterRequestSync carries the queue to the host.
    /// </summary>
    public sealed class PlayerCharacterAuthoring : MonoBehaviour
    {
        private sealed class PlayerCharacterBaker : Baker<PlayerCharacterAuthoring>
        {
            public override void Bake(PlayerCharacterAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                // Who, and what they add up to. Health is the same component,
                // buffer and death flag every enemy carries, so the resolver needs
                // nothing new the day something hurts a player.
                AddComponent(entity, new ComponentTypeSet(new ComponentType[]
                {
                    typeof(PlayerCharacter),
                    typeof(PlayerStats),
                    typeof(Health),
                    typeof(DamageEvent),
                    typeof(Dead),
                    typeof(DamageFeedback),
                    typeof(Mana),
                    typeof(Wallet),
                    typeof(CastCue),
                    typeof(DelayedStrike),
                    typeof(KeystoneComponent),
                    typeof(ActiveSetBonusStatus)
                }));

                // Gear, bag and the lobby's queues — on every character, so no
                // system has to ask whether the buffer exists before it can
                // answer a click.
                AddComponent(entity, new ComponentTypeSet(new ComponentType[]
                {
                    typeof(StatsDirty),
                    typeof(EquippedItem),
                    typeof(EquipRequest),
                    typeof(EquipResult),
                    typeof(SocketRequest),
                    typeof(SocketResult),
                    typeof(CarriedBag),
                    typeof(InventoryPlacementRequest),
                    typeof(InventoryPlacementResult),
                    typeof(NpcSessionOpened),
                    typeof(VendorTransactionRequest),
                    typeof(VendorTransactionResult)
                }));

                // Skills, blink and statuses.
                AddComponent(entity, new ComponentTypeSet(new ComponentType[]
                {
                    typeof(CraftRequest),
                    typeof(CraftResult),
                    typeof(SkillSlot),
                    typeof(TriggerCooldown),
                    typeof(BlinkSequence),
                    typeof(Invulnerable),
                    typeof(PlayerWarp),
                    typeof(ActiveStatusEffect),
                    typeof(CrowdControlImmunity),
                    typeof(CrowdControlResistance),
                    typeof(StatusGate),
                    typeof(StatusVisual)
                }));

                // Zero at full length, not default: a default block has length
                // zero and throws the first time a stat is written.
                SetComponent(entity, new PlayerStats { Final = StatBlock.Zero(), Version = 0 });

                // Neutral, not zero: the cast reads the damage multiplier, and a
                // scene with no reaction database never ticks the gate back to one.
                SetComponent(entity, StatusGate.Neutral);
                SetComponent(entity, CrowdControlResistance.None);

                // Enableable flags are born raised. These four must not be — a
                // character would be born dead, mid-blink and untouchable.
                SetComponentEnabled<Dead>(entity, false);
                SetComponentEnabled<DamageFeedback>(entity, false);
                SetComponentEnabled<BlinkSequence>(entity, false);
                SetComponentEnabled<Invulnerable>(entity, false);

                // Raised, so the first recompute happens without anyone having to
                // equip something to trigger it.
                SetComponentEnabled<StatsDirty>(entity, true);

                // One element per slot, in enum order, so the buffer is addressed
                // rather than searched.
                DynamicBuffer<EquippedItem> slots = SetBuffer<EquippedItem>(entity);

                for (int slot = 0; slot < EquipmentSlots.Count; slot++)
                {
                    slots.Add(new EquippedItem
                    {
                        Slot = (EquipmentSlot)slot,
                        Item = Entity.Null,
                        ItemId = EquippedItem.Empty
                    });
                }
            }
        }
    }
}
