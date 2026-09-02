using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Spawning
{
    /// <summary>Spawn point marker. The position comes from LocalTransform.</summary>
    public struct SpawnPoint : IComponentData
    {
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
    /// How many waves have been ordered but not yet spawned.
    ///
    /// A counter rather than a bool: if spawn is pressed twice in one frame, the
    /// second order must not silently vanish — otherwise during measurements it
    /// is unclear why there are fewer enemies than key presses.
    /// </summary>
    public struct WaveSpawnRequest : IComponentData
    {
        public int PendingWaves;
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
