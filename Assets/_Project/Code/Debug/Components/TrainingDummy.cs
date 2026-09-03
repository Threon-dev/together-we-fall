using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// A target that takes damage and never dies.
    ///
    /// It carries EnemyTag like anything else worth shooting at, because every
    /// piece of targeting in the game — projectile impact, area shape, chain
    /// jump — asks that one question. What it deliberately does NOT carry is
    /// movement: no MovementData, no ChaseTarget, no path. The systems that
    /// would move it query for those, so a dummy stands still without a single
    /// "if this is a dummy" anywhere in the movement pipeline.
    ///
    /// It does still occupy the crowd separation grid, which is intentional: a
    /// wave should flow around it the way it flows around anything else.
    /// </summary>
    public struct TrainingDummy : IComponentData
    {
        /// <summary>Seconds left of the hit flash. Zero when at rest.</summary>
        public float FlashRemaining;

        public float FlashDuration;

        /// <summary>
        /// The colour to return to. Captured at bake time, so the flash knows
        /// what "not being hit" looks like without anything having to remember
        /// it at runtime.
        /// </summary>
        public float4 RestColor;
    }
}
