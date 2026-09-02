using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Constrains movement to the navmesh surface.
    ///
    /// Until this system existed, nothing held enemies on the navmesh at all.
    /// Following a path only looked like obstacle avoidance because the corners
    /// happen to lie on walkable ground — it was a helpful direction, never a
    /// limit. Separation exposed that: the moment a sideways push was added to
    /// the path direction, the sum left the corridor and there was nothing to
    /// stop it, so crowds squeezed through walls.
    ///
    /// Rather than write the transform itself, this system CORRECTS
    /// DesiredVelocity to whatever displacement the navmesh actually permits.
    /// That keeps the pipeline's one rule intact — steering stages only ever
    /// accumulate velocity, and exactly one system downstream moves the entity.
    /// It also means being pressed into a wall produces a slide along it rather
    /// than a stop, because MoveLocation walks the surface up to the edge.
    ///
    /// Single-threaded on purpose: one NavMeshQuery cannot be shared across
    /// worker threads. Stepping from a known polygon is cheap, so one Burst
    /// thread covers a large crowd; if this ever shows up in a profile, the
    /// upgrade is a per-thread query pool, not a redesign.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SeparationSystem))]
    [UpdateBefore(typeof(MovementApplySystem))]
    public partial struct NavMeshProjectionSystem : ISystem
    {
        /// <summary>
        /// MoveLocation and MapLocation walk polygons directly and never run a
        /// path search, so the node pool only has to be non-trivial, not large.
        /// </summary>
        private const int PathNodePoolSize = 128;

        private NavMeshQuery _query;
        private bool _queryCreated;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PathfindingSettings>();
            state.RequireForUpdate<EnemyTag>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_queryCreated)
                _query.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            NavMeshWorld world = NavMeshWorld.GetDefaultWorld();
            if (!world.IsValid())
                return;

            if (!_queryCreated)
            {
                _query = new NavMeshQuery(world, Allocator.Persistent, PathNodePoolSize);
                _queryCreated = true;
            }

            float deltaTime = SystemAPI.Time.DeltaTime;
            if (deltaTime <= 0f)
                return;

            PathfindingSettings settings = SystemAPI.GetSingleton<PathfindingSettings>();
            float extent = settings.NavMeshSampleDistance;

            state.Dependency = new ProjectOntoNavMeshJob
            {
                Query = _query,
                DeltaTime = deltaTime,
                MapExtents = new float3(extent, extent, extent),
                AreaMask = NavMesh.AllAreas
            }.Schedule(state.Dependency);

            // Tell the navmesh it must not be rebuilt while the job reads it.
            // Skipping this is the kind of race that surfaces as a rare crash
            // during a runtime rebake, long after anyone would connect it here.
            world.AddDependency(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(EnemyTag))]
        private partial struct ProjectOntoNavMeshJob : IJobEntity
        {
            public NavMeshQuery Query;
            public float DeltaTime;
            public float3 MapExtents;
            public int AreaMask;

            private void Execute(
                in LocalTransform transform,
                ref MovementData movement,
                ref NavMeshAgentLocation agent)
            {
                if (!EnsureMapped(transform.Position, ref agent))
                {
                    // Genuinely off the navmesh and too far to snap back. Leave
                    // the velocity untouched so the enemy can walk itself out of
                    // the dead zone instead of being frozen there permanently.
                    return;
                }

                float3 desiredPosition = transform.Position + movement.DesiredVelocity * DeltaTime;

                NavMeshLocation moved = Query.MoveLocation(agent.Location, desiredPosition, AreaMask);
                agent.Location = moved;

                // Report back only the motion the surface allowed. Downstream
                // uses this for facing and animation speed too, so an enemy
                // pinned against a wall correctly reads as barely moving.
                movement.DesiredVelocity = ((float3)moved.position - transform.Position) / DeltaTime;
            }

            private bool EnsureMapped(float3 position, ref NavMeshAgentLocation agent)
            {
                if (agent.IsMapped && Query.IsValid(agent.Location))
                    return true;

                agent.Location = Query.MapLocation(position, MapExtents, 0, AreaMask);
                agent.IsMapped = Query.IsValid(agent.Location);

                return agent.IsMapped;
            }
        }
    }
}
