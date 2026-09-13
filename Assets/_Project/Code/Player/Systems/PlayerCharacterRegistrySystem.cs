using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Inventory;
using TogetherWeFall.Lobby;
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
        // Not a copy of the number: EquipmentSlots.Count is derived from the
        // enum, so adding a slot is one line there and nothing here.
        private const int SlotCount = EquipmentSlots.Count;

        /// <summary>
        /// Used when no CharacterConfig has been baked. Twelve by five is the
        /// PoE bag, and a scene without the SubScene should still be playable
        /// rather than leave the player with nowhere to put anything.
        /// </summary>
        private const int DefaultBagWidth = 12;
        private const int DefaultBagHeight = 5;

        private EntityQuery _characterQuery;
        private EntityQuery _bagSizeQuery;

        public void OnCreate(ref SystemState state)
        {
            _characterQuery = SystemAPI.QueryBuilder()
                .WithAll<PlayerCharacter>()
                .Build();

            // A query rather than SystemAPI.TryGetSingleton, because this is
            // read from a helper method rather than from OnUpdate, and a plain
            // query needs no source generation to get there.
            _bagSizeQuery = SystemAPI.QueryBuilder()
                .WithAll<CharacterInventorySize>()
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
            // The bag first, because creating an entity is a structural change
            // and the character's buffers are taken further down. Doing it the
            // other way round would invalidate them between the taking and the
            // filling.
            Entity bag = CreateBag(ref state, playerId);

            EntityArchetype archetype = state.EntityManager.CreateArchetype(
                typeof(PlayerCharacter),
                typeof(PlayerStats),

                // The same Health every enemy carries, and the same buffer the
                // damage resolver reads. Nothing hurts a player yet — enemies
                // do not attack — so today this is a pool that only ever goes
                // down by being spent. It is here in this shape rather than as
                // a number of its own so that the day something does write a
                // DamageEvent at a player, no system has to learn anything.
                //
                // Dead and DamageFeedback come along because the resolver's job
                // declares them present. The death systems cannot reach a
                // character: they query LocalTransform and a sheet is not a body
                // in the world. So Dead on a player is a raised flag nothing
                // consumes — which is precisely the Downed seam, waiting.
                typeof(Health),
                typeof(DamageEvent),
                typeof(Dead),
                typeof(DamageFeedback),

                // Current alone. Its ceiling and its refill rate are stats.
                typeof(Mana),

                // What the model's arms read. Zero at birth: nothing cast yet.
                typeof(CastCue),

                // Swings pressed but not yet landed. See SkillCastSystem.
                typeof(DelayedStrike),

                // Money, which used to be a pile of items in the bag. Zero at
                // birth and filled by picking coins up off the floor.
                typeof(Wallet),

                // Beside the stats because it is derived exactly as they are:
                // read off the gear by PlayerStatsSystem on the same dirty flag.
                typeof(KeystoneComponent),

                // Beside the keystone for the same reason, and filled by a
                // system on the same flag: how much of each set is worn is
                // derived from the gear and stale the moment it changes. Empty
                // for a character wearing no set piece, which is most of them.
                typeof(ActiveSetBonusStatus),
                typeof(StatsDirty),
                typeof(EquippedItem),
                typeof(EquipRequest),
                typeof(EquipResult),
                typeof(SocketRequest),
                typeof(SocketResult),
                typeof(CarriedBag),
                typeof(InventoryPlacementRequest),
                typeof(InventoryPlacementResult),

                // The lobby queues. On every character rather than only on one
                // standing in a lobby, because "does this character have the
                // buffer yet" is a question no system should have to ask before
                // it can answer a click — and five empty buffers cost a
                // character nothing in a scene with no NPCs in it.
                typeof(NpcSessionOpened),
                typeof(VendorTransactionRequest),
                typeof(VendorTransactionResult),
                typeof(CraftRequest),
                typeof(CraftResult),

                // Left empty here and filled by SkillLoadoutSystem: the skill
                // database rides in a SubScene, which may not have loaded yet.
                typeof(SkillSlot),

                // Empty for a character with no trigger gems, which is every
                // character until one is socketed: entries appear when a trigger
                // fires and are dropped when they run out.
                typeof(TriggerCooldown));

            Entity entity = state.EntityManager.CreateEntity(archetype);
            state.EntityManager.SetName(entity, "PlayerCharacter");

            state.EntityManager.SetComponentData(entity, new CarriedBag { Container = bag });

            state.EntityManager.SetComponentData(entity, new PlayerCharacter
            {
                PlayerId = playerId
            });

            state.EntityManager.SetComponentData(entity, new PlayerStats
            {
                Final = StatBlock.Zero(),
                Version = 0
            });

            // Zero, not full. Nobody knows how big these are until the sheet is
            // computed, and PlayerResourceSystem fills them the first frame it
            // reads a real one. Guessing a hundred here would be a second place
            // that has an opinion about how much life a character has.
            state.EntityManager.SetComponentData(entity, new Health { Current = 0f, Max = 0f });
            state.EntityManager.SetComponentData(entity, new Mana { Current = 0f });

            // Down from birth. Dead is enableable and an archetype creates it
            // raised, so a character would otherwise be born dead — invisible
            // today because nothing reads it, and a mystery on the day
            // something does.
            state.EntityManager.SetComponentEnabled<Dead>(entity, false);
            state.EntityManager.SetComponentEnabled<DamageFeedback>(entity, false);

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
                    Item = Entity.Null,
                    ItemId = EquippedItem.Empty
                });
            }

            UnityEngine.Debug.Log(
                $"[PlayerCharacterRegistrySystem] Character created for player {playerId}.");
        }

        /// <summary>
        /// Creates the container a character carries things in.
        ///
        /// Its own entity rather than cells on the character, so that the bag,
        /// a stash and a chest on the floor are one kind of thing described
        /// once. The owner is written here and checked on every request: a
        /// container without one is a shared container, and there are none yet.
        /// </summary>
        private Entity CreateBag(ref SystemState state, int playerId)
        {
            int width = DefaultBagWidth;
            int height = DefaultBagHeight;

            if (!_bagSizeQuery.IsEmptyIgnoreFilter)
            {
                CharacterInventorySize authored =
                    _bagSizeQuery.GetSingleton<CharacterInventorySize>();

                if (authored.Width > 0 && authored.Height > 0)
                {
                    width = authored.Width;
                    height = authored.Height;
                }
            }

            EntityArchetype archetype = state.EntityManager.CreateArchetype(
                typeof(InventoryGridComponent),
                typeof(InventoryCell),
                typeof(ContainerOwner));

            Entity bag = state.EntityManager.CreateEntity(archetype);
            state.EntityManager.SetName(bag, "PlayerBag");

            var grid = new InventoryGridComponent { Width = width, Height = height };

            state.EntityManager.SetComponentData(bag, grid);
            state.EntityManager.SetComponentData(bag, new ContainerOwner { PlayerId = playerId });

            // Filled with empty cells up front: a buffer shorter than
            // Width x Height would index out of bounds on the first placement.
            GridFit.Reset(state.EntityManager.GetBuffer<InventoryCell>(bag), grid);

            UnityEngine.Debug.Log(
                $"[PlayerCharacterRegistrySystem] Bag created for player {playerId} " +
                $"({width}x{height}).");

            return bag;
        }
    }
}
