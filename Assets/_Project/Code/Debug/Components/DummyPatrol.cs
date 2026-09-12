using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// A dummy that will not stand still.
    ///
    /// The smallest possible moving target: back and forth along one axis, so a
    /// player can see whether a projectile skill actually leads its target and
    /// whether a chain keeps up. Nothing about it is AI — a patrolling enemy is
    /// a different feature, and this is a metronome.
    ///
    /// It writes LocalTransform directly, which is the one exception to "one
    /// system moves things". The rule is about the enemy movement pipeline,
    /// where half a dozen stages accumulate into DesiredVelocity and exactly one
    /// applies it; a dummy has no MovementData at all, so no stage of that
    /// pipeline can see it and there is nothing to contend with.
    /// </summary>
    public struct DummyPatrol : IComponentData
    {
        /// <summary>
        /// Where it started. Captured at bake time, because the midpoint of the
        /// sweep has to be a fixed thing — derived from the current position it
        /// would drift a little further every frame.
        /// </summary>
        public float3 Origin;

        /// <summary>Direction of the sweep, normalised at bake time.</summary>
        public float3 Axis;

        /// <summary>Half the length of the sweep, in metres.</summary>
        public float Distance;

        /// <summary>Full sweeps per second.</summary>
        public float Frequency;
    }
}
