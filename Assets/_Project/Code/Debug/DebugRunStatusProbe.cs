using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Combat;
using TogetherWeFall.Dungeon;
using TogetherWeFall.Equipment;
using TogetherWeFall.Interaction;
using TogetherWeFall.Inventory;
using TogetherWeFall.Loot;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// Reads the state of a run out of ECS and turns it into text.
    ///
    /// It exists so the HUD can stay a HUD. DebugHud knows about delegates and
    /// nothing else; this knows about entities and produces a string; and
    /// GameBootstrap only introduces them. Without it, either the HUD grows
    /// queries or the bootstrap grows a formatting routine, and both are worse
    /// places for this to live than a file named after what it does.
    ///
    /// The dungeon line appears only where there is a dungeon. The combat line
    /// appears everywhere, because the arena is the better place to watch a
    /// chain reaction with three hundred enemies in it.
    /// </summary>
    public sealed class DebugRunStatusProbe
    {
        private EntityManager _entityManager;
        private EntityQuery _runQuery;
        private EntityQuery _chestQuery;
        private EntityQuery _droppedItemQuery;
        private EntityQuery _inventoryQuery;
        private EntityQuery _tallyQuery;
        private EntityQuery _projectileQuery;
        private bool _ready;

        private string _cached = string.Empty;
        private int _cachedSignature;

        public bool Initialize()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            _entityManager = world.EntityManager;

            _runQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonRunState>());
            _chestQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChestState>());
            // InteractableTag, not just ItemInstance: the pool is full of items
            // that exist and are not on the floor.
            _droppedItemQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemInstance>(),
                ComponentType.ReadOnly<InteractableTag>());
            // Counted as items rather than summed out of a buffer: since the
            // grid inventory a carried item is an entity, and ItemStored is
            // exactly the flag that says somebody owns it — in a bag or worn.
            _inventoryQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemStored>());
            _tallyQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatTally>());
            _projectileQuery =
                _entityManager.CreateEntityQuery(ComponentType.ReadOnly<SkillProjectile>());

            _ready = true;
            return true;
        }

        public string Describe()
        {
            if (!_ready)
                return string.Empty;

            CombatTally tally = _tallyQuery.IsEmptyIgnoreFilter
                ? default
                : _tallyQuery.GetSingleton<CombatTally>();

            int projectiles = _projectileQuery.CalculateEntityCount();

            bool hasRun = !_runQuery.IsEmptyIgnoreFilter;
            DungeonRunState state = default;

            int rooms = 0;
            int active = 0;
            int cleared = 0;
            int chests = 0;
            int openedChests = 0;
            int onFloor = 0;
            int carried = 0;

            if (hasRun)
            {
                Entity run = _runQuery.GetSingletonEntity();
                state = _entityManager.GetComponentData<DungeonRunState>(run);

                CountRooms(run, out rooms, out active, out cleared);
                CountChests(out chests, out openedChests);

                onFloor = _droppedItemQuery.CalculateEntityCount();
                carried = CountCarried();
            }

            // Rebuild the string only when something moved. This runs every
            // frame while the point of the HUD is to measure frames, and a
            // fresh string per frame is exactly the kind of noise that ends up
            // in the profile of the thing being profiled.
            int signature = Signature(state, hasRun, rooms, active, cleared, chests, openedChests,
                onFloor, carried, tally, projectiles);

            if (signature == _cachedSignature)
                return _cached;

            _cachedSignature = signature;

            string combat =
                $"Kills {tally.Kills}   Damage {tally.DamageDealt:0}   Projectiles {projectiles}";

            _cached = hasRun
                ? $"Dungeon {state.Seed} [{state.Phase}] — rooms {rooms}, " +
                  $"active {active}, cleared {cleared}\n" +
                  $"Chests {openedChests}/{chests} opened   " +
                  $"Loot on floor {onFloor}   Carried {carried}\n" +
                  combat
                : combat;

            return _cached;
        }

        private void CountRooms(Entity run, out int total, out int active, out int cleared)
        {
            total = 0;
            active = 0;
            cleared = 0;

            if (!_entityManager.HasBuffer<DungeonRoomElement>(run))
                return;

            DynamicBuffer<DungeonRoomElement> rooms =
                _entityManager.GetBuffer<DungeonRoomElement>(run, isReadOnly: true);

            total = rooms.Length;

            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i].Phase == DungeonRoomPhase.Active)
                    active++;
                else if (rooms[i].Phase == DungeonRoomPhase.Cleared)
                    cleared++;
            }
        }

        private void CountChests(out int total, out int opened)
        {
            opened = 0;

            using NativeArray<ChestState> chests =
                _chestQuery.ToComponentDataArray<ChestState>(Allocator.Temp);

            total = chests.Length;

            for (int i = 0; i < chests.Length; i++)
            {
                if (chests[i].Phase == ChestPhase.Opened)
                    opened++;
            }
        }

        /// <summary>
        /// Everything every player owns, in a bag or worn.
        ///
        /// The query filters on the enabled state of ItemStored by itself, so
        /// this is a count of items that are somebody's — the rest of the pool
        /// is invisible to it without anybody having to subtract.
        /// </summary>
        private int CountCarried()
        {
            return _inventoryQuery.CalculateEntityCount();
        }

        private static int Signature(
            in DungeonRunState state,
            bool hasRun,
            int rooms, int active, int cleared,
            int chests, int openedChests,
            int onFloor, int carried,
            in CombatTally tally,
            int projectiles)
        {
            unchecked
            {
                int hash = hasRun ? (int)state.Seed : 0;
                hash = hash * 31 + (int)state.Phase;
                hash = hash * 31 + rooms;
                hash = hash * 31 + active;
                hash = hash * 31 + cleared;
                hash = hash * 31 + chests;
                hash = hash * 31 + openedChests;
                hash = hash * 31 + onFloor;
                hash = hash * 31 + carried;
                hash = hash * 31 + tally.Kills;
                hash = hash * 31 + (int)tally.DamageDealt;
                hash = hash * 31 + projectiles;
                return hash;
            }
        }

        public void Dispose()
        {
            if (!_ready)
                return;

            _runQuery.Dispose();
            _chestQuery.Dispose();
            _droppedItemQuery.Dispose();
            _inventoryQuery.Dispose();
            _tallyQuery.Dispose();
            _projectileQuery.Dispose();
            _ready = false;
        }
    }
}
