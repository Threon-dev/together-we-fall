using Unity.Entities;
using Unity.Mathematics;

namespace TogetherWeFall.Enemies
{
    // ─────────────────────────────────────────────────────────────────────
    // PRESENTATION — what the client sees.
    //
    // Candidates for [GhostField] once the project moves to Netcode for
    // Entities, alongside LocalTransform. The rule for this file: only things
    // without which the client cannot draw a frame belong here. If a field is
    // needed to MAKE a decision rather than to draw, it belongs in the
    // authoritative file.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Visual state of an enemy.
    ///
    /// MoveSpeedNormalized is kept separate from MovementData.DesiredVelocity on
    /// purpose: the client needs a 0..1 scalar to blend animation, not a full
    /// velocity vector. That is both cheaper on the wire and a more honest
    /// description of what the client is entitled to know.
    /// </summary>
    public struct EnemyPresentation : IComponentData
    {
        public float3 FacingDirection;
        public float MoveSpeedNormalized;
    }
}
