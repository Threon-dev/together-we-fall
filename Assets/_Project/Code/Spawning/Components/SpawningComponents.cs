using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Spawning
{
    /// <summary>
    /// Spawn point marker. The position comes from LocalTransform.
    ///
    /// RoomId is what lets one spawner serve a whole dungeon: an order aimed at
    /// a room only ever picks points belonging to that room, so a fight starts
    /// around the players who walked in rather than across the floor. Points
    /// authored by hand, as in the arena scene, carry -1 and answer any order.
    /// </summary>
    public struct SpawnPoint : IComponentData
    {
        public int RoomId;

        /// <summary>Points that belong to no room and serve any order.</summary>
        public const int AnyRoom = -1;
    }

    /// <summary>Spawner settings, baked from SpawnConfig.</summary>
    public struct WaveSpawnerConfig : IComponentData
    {
        public Entity EnemyPrefab;
        public int WaveSize;
        public int MaxAlive;
        public float JitterRadius;
    }

    /// <summary>
    /// A queued wave.
    ///
    /// A buffer rather than a counter: with rooms in the picture, "spawn a wave"
    /// is no longer one thing. Two rooms can activate in the same frame, and
    /// each order has to remember where it was aimed and how big it was. A
    /// counter would collapse them into "two waves, somewhere".
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct WaveSpawnOrder : IBufferElementData
    {
        /// <summary>Room to spawn in, or SpawnPoint.AnyRoom for any point on the map.</summary>
        public int RoomId;

        /// <summary>Enemies to spawn, or 0 to use the spawner default.</summary>
        public int Count;
    }

    /// <summary>
    /// Spawner state. Random lives here instead of being recreated per wave: a
    /// sequence from one seed is reproducible, and that is the very property
    /// needed later, when the enemy scatter has to match across all players from
    /// a shared seed.
    /// </summary>
    public struct WaveSpawnerState : IComponentData
    {
        public Random Random;
        public int WavesSpawned;
        public int TotalSpawned;
    }
}
