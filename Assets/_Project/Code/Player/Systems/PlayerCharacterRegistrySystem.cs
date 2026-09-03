using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Equipment;
using TogetherWeFall.Shared;
using TogetherWeFall.Skills;

namespace TogetherWeFall.Player.Systems
{
    /// <summary>
    /// Gives every player in the position registry a character entity.
    ///
    /// Derived rather than published: the position buffer is already the list of
    /// who is playing, so a player joining a coop game gets a character without
    /// anybody having to remember to create one, and without a fifth
    /// GameObject-to-ECS bridge whose only job is to say "I exist".
    ///
    /// The character is where everything about a player that is not their
    /// position lives — equipment, inventory, stats. Keeping it off the position
    /// buffer keeps an inventory out of the hot path of every enemy on the floor.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerCharacterRegistrySystem : ISystem
    {
        private const int SlotCount = (int)EquipmentSlot.Accessory + 1;

        private EntityQuery _characterQuery;

        public void OnCreate(ref SystemState state)
        {
            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter>()
                .Build();

            state.RequireForUpdate<PlayerPositionsSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<PlayerPositionElement> players =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>(isReadOnly: true);

            if (players.Length == 0)
                return;

            // The common case by far: everybody already has a character. Costing
            // this frame a count rather than a temporary array keeps a system
            // that does nothing from allocating to discover that.
            if (_characterQuery.CalculateEntityCount() >= players.Length)
                return;

            using NativeArray<PlayerCharacter> existing =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            // Ids are copied out before anything is created: creating an entity
            // is a structural change, and the buffer would not survive it.
            using var missing = new NativeList<int>(players.Length, Allocator.Temp);

            for (int i = 0; i < players.Length; i++)
            {
                int playerId = players[i].PlayerId;

                if (!HasCharacter(existing, playerId) && !missing.Contains(playerId))
                    missing.Add(playerId);
            }

            for (int i = 0; i < missing.Length; i++)
                CreateCharacter(ref state, missing[i]);
        }

        private static bool HasCharacter(in NativeArray<PlayerCharacter> characters, int playerId)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId == playerId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates the character in one structural change, through an archetype.
        ///
        /// Adding components one at a time would be one structural change each,
        /// and every buffer taken before the next add would already be stale.
        /// </summary>
        private void CreateCharacter(ref SystemState state, int playerId)
        {
            EntityArchetype archetype = state.EntityManager.CreateArchetype(
                typeof(PlayerCharacter),
                typeof(PlayerStats),
                typeof(StatsDirty),
                typeof(EquippedItem),
                typeof(InventoryItem),
                typeof(EquipRequest),

                // Left empty here and filled by SkillLoadoutSystem: the skill
                // database rides in a SubScene, which may not have loaded yet.
                typeof(SkillSlot));

            Entity entity = state.EntityManager.CreateEntity(archetype);
            state.EntityManager.SetName(entity, "PlayerCharacter");

            state.EntityManager.SetComponentData(entity, new PlayerCharacter
            {
                PlayerId = playerId
            });

            state.EntityManager.SetComponentData(entity, new PlayerStats
            {
                Final = StatBlock.Zero(),
                Version = 0
            });

            // Dirty from birth, so the first recompute happens without anyone
            // having to equip something to trigger it.
            state.EntityManager.SetComponentEnabled<StatsDirty>(entity, true);

            DynamicBuffer<EquippedItem> slots = state.EntityManager.GetBuffer<EquippedItem>(entity);

            // One element per slot, in enum order, so the buffer is addressed
            // rather than searched.
            for (int slot = 0; slot < SlotCount; slot++)
            {
                slots.Add(new EquippedItem
                {
                    Slot = (EquipmentSlot)slot,
                    ItemId = EquippedItem.Empty
                });
            }

            UnityEngine.Debug.Log(
                $"[PlayerCharacterRegistrySystem] Character created for player {playerId}.");
        }
    }
}
