using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Enemies;

namespace TogetherWeFall.Spawning.Systems
{
    /// <summary>
    /// Creates every enemy the game will ever have, once.
    ///
    /// Enemies are the heaviest churn in the project by a wide margin — a
    /// hundred arrive per wave and every one of them eventually dies — and both
    /// halves were structural changes in frames that are already the busiest.
    ///
    /// The pool is exactly MaxAlive bodies, which makes the cap and the pool the
    /// same thing rather than two numbers that can disagree. A wave that asks
    /// for more than is free gets what is free, which is what the cap always
    /// meant anyway.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct EnemyPoolSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // The spawner lives in a SubScene, so this waits for it rather than
            // assuming the world is ready on frame one.
            state.RequireForUpdate<WaveSpawnerConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            WaveSpawnerConfig config = SystemAPI.GetSingleton<WaveSpawnerConfig>();

            if (config.EnemyPrefab == Entity.Null || config.MaxAlive <= 0)
            {
                state.Enabled = false;
                return;
            }

            using NativeArray<Entity> pooled = state.EntityManager.Instantiate(
                config.EnemyPrefab, config.MaxAlive, Allocator.Temp);

            // The prefab is baked with EnemyTag down, so these are already
            // invisible to every enemy system. All that is left is to put them
            // somewhere the camera will not find them.
            EnemyPool.Release(state.EntityManager, pooled);

            UnityEngine.Debug.Log(
                $"[EnemyPoolSystem] {config.MaxAlive} enemies pooled. Waves now wake bodies " +
                "rather than create them.");

            state.Enabled = false;
        }
    }
}
