using Unity.Entities;
using UnityEngine;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Copies a generated floor into the ECS world.
    ///
    /// The third bridge from GameObject land into ECS, after the player position
    /// and the debug spawn key, and built to the same rule: it decides nothing.
    /// It converts a layout into components and sets the run phase — which room
    /// wakes up, when a wave spawns and when a room counts as cleared are all
    /// decided by systems that never see this class.
    ///
    /// That rule is what makes the coop story short. A client will not receive a
    /// dungeon; it will receive a seed, run the same generator, and call this
    /// same method with the identical result.
    /// </summary>
    public sealed class DungeonLayoutPublisher
    {
        private EntityManager _entityManager;
        private EntityQuery _runQuery;
        private bool _hasWorld;

        public bool Initialize()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(DungeonLayoutPublisher)}] ECS world is unavailable — " +
                    "the dungeon will be built but no system will know about it.");
                return false;
            }

            _entityManager = world.EntityManager;
            _runQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<DungeonRunState>());
            _hasWorld = true;

            return true;
        }

        /// <summary>
        /// Publishes the floor in the Generating phase. Nothing that depends on
        /// the navmesh may start yet — SetPhase(Ready) is the signal for that,
        /// and it comes only once the bake is finished.
        /// </summary>
        public void Publish(
            DungeonLayout layout, in DungeonEncounterSettings encounter, int floorIndex)
        {
            if (!_hasWorld || layout == null)
                return;

            Entity entity = GetOrCreateRunEntity();

            _entityManager.SetComponentData(entity, new DungeonRunState
            {
                Seed = layout.Seed,
                FloorIndex = floorIndex,
                Phase = DungeonRunPhase.Generating,
                IsRunActive = false,
                EndReason = DungeonRunEndReason.None
            });

            _entityManager.SetComponentData(entity, new DungeonGrid
            {
                Size = layout.GridSize,
                CellSize = layout.CellSize,
                Origin = layout.Origin,
                EntranceWorldPosition = layout.EntranceWorldPosition
            });

            _entityManager.SetComponentData(entity, encounter);

            WriteRooms(entity, layout);

            // A new floor needs new spawn points. Clearing the marker here keeps
            // that decision next to the data it belongs to, instead of in a
            // system that would have to notice the floor changed underneath it.
            if (_entityManager.HasComponent<DungeonSpawnPointsBuilt>(entity))
                _entityManager.RemoveComponent<DungeonSpawnPointsBuilt>(entity);
        }

        public void SetPhase(DungeonRunPhase phase, bool isRunActive)
        {
            if (!_hasWorld || _runQuery.IsEmptyIgnoreFilter)
                return;

            Entity entity = _runQuery.GetSingletonEntity();
            DungeonRunState state = _entityManager.GetComponentData<DungeonRunState>(entity);

            state.Phase = phase;
            state.IsRunActive = isRunActive;

            _entityManager.SetComponentData(entity, state);
        }

        private Entity GetOrCreateRunEntity()
        {
            if (!_runQuery.IsEmptyIgnoreFilter)
                return _runQuery.GetSingletonEntity();

            Entity entity = _entityManager.CreateEntity();
            _entityManager.SetName(entity, "DungeonRun");
            _entityManager.AddComponent<DungeonRunState>(entity);
            _entityManager.AddComponent<DungeonGrid>(entity);
            _entityManager.AddComponent<DungeonEncounterSettings>(entity);
            _entityManager.AddBuffer<DungeonRoomElement>(entity);

            return entity;
        }

        private void WriteRooms(Entity entity, DungeonLayout layout)
        {
            DynamicBuffer<DungeonRoomElement> rooms =
                _entityManager.GetBuffer<DungeonRoomElement>(entity);

            rooms.Clear();
            rooms.Capacity = layout.Rooms.Length;

            for (int i = 0; i < layout.Rooms.Length; i++)
            {
                DungeonRoom room = layout.Rooms[i];

                rooms.Add(new DungeonRoomElement
                {
                    RoomId = room.Id,
                    Type = room.Type,
                    Center = layout.RectCenter(room.Min, room.Size),
                    Extents = layout.RectExtents(room.Size),
                    Phase = DungeonRoomPhase.Dormant,
                    PhaseTimer = 0f,
                    PlayersInside = 0,
                    EnemiesInside = 0
                });
            }
        }
    }
}
