using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Enemies
{
    // ─────────────────────────────────────────────────────────────────────
    // AUTHORITATIVE — server-only state.
    //
    // Everything in this file stays on the host and is NOT replicated once the
    // project moves to Netcode for Entities. The split is done by file rather
    // than by folder or naming for exactly that reason: when the time comes to
    // place [GhostField] attributes, the boundary has to be visible at a
    // glance, not reconstructed from memory.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enemy marker. Needed for queries and the HUD counter.
    ///
    /// Enableable, and that carries weight: a body that is playing out its death
    /// is still an entity, but it is no longer an enemy. Switching this off is
    /// what stops it being chased, counted, targeted by a projectile or holding
    /// a room open — without a single query anywhere having to learn what a
    /// corpse is, because queries filter by enabled state already.
    /// </summary>
    public struct EnemyTag : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Movement data. DesiredVelocity is "where the enemy wants to go this
    /// frame"; steering systems fill it in (FollowPath today, with Separation
    /// adding its contribution in stage 4), and exactly one system applies it.
    /// That split removes any race over the transform.
    /// </summary>
    public struct MovementData : IComponentData
    {
        public float Speed;
        public float RotationSpeed;
        public float StoppingDistance;
        public float3 DesiredVelocity;
    }

    /// <summary>
    /// Who this enemy is chasing. Filled from the player position buffer rather
    /// than from a transform — see PlayerPositionsSingleton.
    /// </summary>
    public struct ChaseTarget : IComponentData
    {
        public float3 Position;
        public int PlayerId;
        public bool HasTarget;
    }

    /// <summary>
    /// Progress along the current path. Used from stage 3; laid down now so the
    /// enemy's data layout does not change after the measurements are taken.
    /// </summary>
    public struct PathProgress : IComponentData
    {
        public int CurrentCorner;
        public float TimeSinceRepath;
        public float3 LastTargetPosition;
    }

    /// <summary>
    /// Corners of the computed path.
    ///
    /// This buffer is deliberately NOT meant for replication: the client only
    /// needs the enemy's position and facing, while the path is an intermediate
    /// AI result that would bloat traffic by orders of magnitude with no benefit
    /// to what is drawn.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct PathPoint : IBufferElementData
    {
        public float3 Position;
    }

    /// <summary>
    /// "Needs a path recalculation" flag. Enableable rather than an ordinary
    /// component: toggling it causes no structural change, so PathfindingSystem
    /// can pick candidates without re-archetyping hundreds of entities every
    /// frame.
    /// </summary>
    public struct NeedsRepath : IComponentData, IEnableableComponent
    {
    }
}
