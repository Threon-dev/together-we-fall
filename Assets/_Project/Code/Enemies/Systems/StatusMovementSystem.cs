using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using TogetherWeFall.Combat;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// What the statuses on a body do to where it was going.
    ///
    /// One more contribution to DesiredVelocity, in the slot between crowd
    /// separation and the navmesh projection that trims the result. That is the
    /// whole of it, and it is the reason there is one system here rather than
    /// the three the specification asked for — a stun system, a root system and
    /// a slow system would be three things writing one field, and the project's
    /// oldest rule is that every stage only ever adds to DesiredVelocity while
    /// exactly one system at the end moves anything.
    ///
    /// Stun and Root arrive here as the same field for the same reason. They
    /// differ in what they do to casting, not to walking, and that difference is
    /// already settled — by StatusEffects, a frame earlier, in the pass that
    /// reduced every status on the body to one gate.
    ///
    /// After separation on purpose. A rooted body still has to be shoved out of
    /// a crowd that is piling into it, or a hundred enemies would stack inside
    /// one another around whatever was rooted first; zeroing the velocity before
    /// separation ran would have looked identical for one enemy and wrong for a
    /// wave.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SeparationSystem))]
    [UpdateAfter(typeof(TogetherWeFall.Combat.Systems.StatusTickSystem))]
    [UpdateBefore(typeof(NavMeshProjectionSystem))]
    public partial struct StatusMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ApplyStatusMovementJob().ScheduleParallel();
        }

        /// <summary>
        /// WithAll on the enemy tag so a body playing out its death is not still
        /// being slowed — it is no longer an enemy, and the tag being enableable
        /// is what says so.
        /// </summary>
        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct ApplyStatusMovementJob : IJobEntity
        {
            private void Execute(ref MovementData movement, in StatusGate gate)
            {
                if (gate.BlocksMovement)
                {
                    movement.DesiredVelocity = float3.zero;
                    return;
                }

                // Turned round rather than redirected. A fear that pathfound
                // away from the player would be the complex enemy behaviour this
                // project has deliberately not started; a sign flip sends the
                // body back down the corridor it arrived through, and the
                // navmesh projection immediately after this is what keeps it out
                // of the walls.
                if (gate.Flees)
                    movement.DesiredVelocity = -movement.DesiredVelocity;

                // Scaling the velocity rather than MovementData.Speed, and the
                // difference matters: Speed is what this body is, and a slow
                // that wrote to it would need something to remember what the
                // number used to be. The velocity is this frame's intent and is
                // rebuilt from scratch every frame by the path follower, so
                // scaling it cannot accumulate and cannot be forgotten.
                //
                // MoveSpeedNormalized falls out of it for free one stage later,
                // so a slowed enemy also animates slower without anything here
                // knowing that animation exists.
                if (gate.MoveSpeedMultiplier != 1f)
                    movement.DesiredVelocity *= gate.MoveSpeedMultiplier;
            }
        }
    }
}
