using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Inventory;
using TogetherWeFall.Network;
using TogetherWeFall.Shared;

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
    ///
    /// Instantiated from ghost prefabs rather than built from an archetype, so
    /// every client receives every sheet: its own to draw the HUD, the bag and
    /// the shop from, and the others' to animate their bodies. What a character
    /// is made of lives in PlayerCharacterAuthoring now; this only decides who
    /// gets one.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct PlayerCharacterRegistrySystem : ISystem
    {
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
            state.RequireForUpdate<NetworkPrefabs>();
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

            NetworkPrefabs prefabs = SystemAPI.GetSingleton<NetworkPrefabs>();

            if (prefabs.Character == Entity.Null || prefabs.Bag == Entity.Null)
                return;

            using NativeArray<PlayerCharacter> existing =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            // Ids are copied out before anything is created: instantiating is a
            // structural change, and the buffer would not survive it.
            using var missing = new NativeList<int>(players.Length, Allocator.Temp);

            for (int i = 0; i < players.Length; i++)
            {
                int playerId = players[i].PlayerId;

                if (!HasCharacter(existing, playerId) && !missing.Contains(playerId))
                    missing.Add(playerId);
            }

            for (int i = 0; i < missing.Length; i++)
                CreateCharacter(ref state, prefabs, missing[i]);
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

        private void CreateCharacter(ref SystemState state, in NetworkPrefabs prefabs, int playerId)
        {
            // The bag first, because instantiating is a structural change and
            // the character's data is written further down.
            Entity bag = CreateBag(ref state, prefabs.Bag, playerId);

            Entity entity = state.EntityManager.Instantiate(prefabs.Character);
            state.EntityManager.SetName(entity, "PlayerCharacter");

            state.EntityManager.SetComponentData(entity, new CarriedBag { Container = bag });
            state.EntityManager.SetComponentData(entity, new PlayerCharacter { PlayerId = playerId });

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
        private Entity CreateBag(ref SystemState state, Entity prefab, int playerId)
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

            Entity bag = state.EntityManager.Instantiate(prefab);
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
