using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Dungeon
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — the run as the host sees it.
    //
    // None of this is replicated, and not because it is secret: the floor is a
    // pure function of the seed, so every client rebuilds it locally from the
    // one number in DungeonRunState. That is the whole reason generation is
    // deterministic. What will eventually cross the wire is room STATE — which
    // room is active, which is cleared — not room geometry.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>How far along the floor is. Systems gate on this.</summary>
    public enum DungeonRunPhase : byte
    {
        NotStarted = 0,

        /// <summary>Layout and geometry exist; the navmesh is still baking.</summary>
        Generating = 1,

        /// <summary>Navmesh is up. Enemies may spawn and move.</summary>
        Ready = 2,

        Finished = 3
    }

    /// <summary>
    /// Why a run ended. Nothing sets anything but None yet — downed players,
    /// team wipe and extraction are their own step. The field exists now so the
    /// systems that will read it do not have to reshape this component.
    /// </summary>
    public enum DungeonRunEndReason : byte
    {
        None = 0,
        TeamWipe = 1,
        AllExtracted = 2
    }

    /// <summary>What is happening inside one room.</summary>
    public enum DungeonRoomPhase : byte
    {
        /// <summary>Nobody has walked in yet.</summary>
        Dormant = 0,

        /// <summary>Entered; its encounter, if any, is running.</summary>
        Active = 1,

        /// <summary>Done with. A cleared room never re-arms.</summary>
        Cleared = 2
    }

    /// <summary>
    /// The run singleton. Holds the seed the whole floor derives from.
    ///
    /// Shared by the team rather than per player, deliberately: in coop there is
    /// one dungeon, and a run ends for everyone at once.
    /// </summary>
    public struct DungeonRunState : IComponentData
    {
        public uint Seed;
        public int FloorIndex;
        public DungeonRunPhase Phase;
        public bool IsRunActive;
        public DungeonRunEndReason EndReason;
    }

    /// <summary>
    /// The grid the floor was generated on, in world terms.
    ///
    /// Carried into ECS so systems can reason about dungeon space without
    /// reaching back into the managed layout object — which a job could not
    /// touch anyway.
    /// </summary>
    public struct DungeonGrid : IComponentData
    {
        public int2 Size;
        public float CellSize;

        /// <summary>World position of the grid corner, at floor level.</summary>
        public float3 Origin;

        /// <summary>Where players start the floor.</summary>
        public float3 EntranceWorldPosition;
    }

    /// <summary>
    /// Encounter tuning as ECS data, mirrored from DungeonGenerationConfig for
    /// the same reason as the pathfinding settings: a system reads numbers, not
    /// a managed asset.
    /// </summary>
    public struct DungeonEncounterSettings : IComponentData
    {
        public int EnemiesPerCombatRoom;
        public float BossRoomEnemyMultiplier;
        public float RoomClearGraceSeconds;
    }

    /// <summary>
    /// One room, as the simulation sees it.
    ///
    /// A buffer on the run singleton rather than an entity per room: rooms are a
    /// handful of items scanned together every frame, so one contiguous buffer
    /// beats a dozen archetype lookups. The world bounds are flattened to a
    /// centre and half-extents on the ground plane, which is exactly what an
    /// occupancy test needs and nothing more.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct DungeonRoomElement : IBufferElementData
    {
        public int RoomId;
        public DungeonRoomType Type;

        public float3 Center;
        public float2 Extents;

        public DungeonRoomPhase Phase;

        /// <summary>Seconds spent in the current phase. Guards the clear check.</summary>
        public float PhaseTimer;

        public int PlayersInside;
        public int EnemiesInside;

        public bool Contains(float3 position)
        {
            float2 delta = math.abs(position.xz - Center.xz);
            return delta.x <= Extents.x && delta.y <= Extents.y;
        }
    }

    /// <summary>
    /// Marks that the spawn points for this floor have been created.
    ///
    /// A component rather than a bool inside the system: an ISystem is a struct
    /// that may be recreated with the world, while this fact belongs to the run.
    /// </summary>
    public struct DungeonSpawnPointsBuilt : IComponentData
    {
    }

    /// <summary>
    /// Marks that this floor has had its chests placed. Separate from the spawn
    /// point marker so the two can be done by different systems, in either
    /// order, without either having to know the other exists.
    /// </summary>
    public struct DungeonChestsPlaced : IComponentData
    {
    }
}
