using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Computes navmesh paths for a bounded number of enemies per frame.
    ///
    /// A SystemBase rather than an ISystem because NavMesh.CalculatePath is a
    /// managed, main-thread API: it cannot be Bursted and it cannot run in a
    /// job. That constraint is the reason this system exists in this shape at
    /// all — the work is fenced behind a per-frame budget so its cost stays
    /// flat whether there are 100 enemies or 500, and the rest of the pipeline
    /// stays in parallel Burst jobs.
    ///
    /// If this budget ever becomes the bottleneck, the replacement is
    /// NavMeshQuery inside Burst jobs. Everything around this system —
    /// NeedsRepath as input, the PathPoint buffer as output — is designed so
    /// that swap touches only this file.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RepathRequestSystem))]
    public partial class PathfindingSystem : SystemBase
    {
        private EntityQuery _repathQuery;
        private NavMeshPath _path;
        private Vector3[] _cornerBuffer;

        protected override void OnCreate()
        {
            _repathQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EnemyTag, NeedsRepath, ChaseTarget>()
                .WithAll<PathProgress, LocalTransform, PathPoint>()
                .Build(this);

            RequireForUpdate<PathfindingSettings>();
            RequireForUpdate(_repathQuery);

            // One reusable NavMeshPath for the whole system. Allocating one per
            // query would hand the GC a few hundred objects per second.
            _path = new NavMeshPath();
        }

        protected override void OnUpdate()
        {
            PathfindingSettings settings = SystemAPI.GetSingleton<PathfindingSettings>();
            EnsureCornerBuffer(settings.MaxCorners);

            // About to touch entity data straight from the main thread, so the
            // jobs scheduled earlier this frame have to be finished first.
            CompleteDependency();

            using NativeArray<Entity> candidates = _repathQuery.ToEntityArray(Allocator.Temp);
            int budget = math.min(settings.MaxRequestsPerFrame, candidates.Length);

            for (int i = 0; i < budget; i++)
                ProcessRequest(candidates[i], settings);
        }

        private void ProcessRequest(Entity entity, in PathfindingSettings settings)
        {
            ChaseTarget target = EntityManager.GetComponentData<ChaseTarget>(entity);

            // Clear the request first, whatever the outcome. A failed query that
            // stayed queued would be retried immediately and forever.
            EntityManager.SetComponentEnabled<NeedsRepath>(entity, false);

            PathProgress progress = EntityManager.GetComponentData<PathProgress>(entity);
            progress.TimeSinceRepath = 0f;
            progress.CurrentCorner = 0;

            if (!target.HasTarget)
            {
                EntityManager.SetComponentData(entity, progress);
                return;
            }

            progress.LastTargetPosition = target.Position;

            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(entity);
            DynamicBuffer<PathPoint> buffer = EntityManager.GetBuffer<PathPoint>(entity);
            buffer.Clear();

            if (TryCalculatePath(transform.Position, target.Position, settings, out int cornerCount))
            {
                // Corner 0 is the start position itself. Keeping it would make
                // the enemy steer at the spot it already stands on and stall for
                // a frame before moving.
                int limit = math.min(cornerCount, settings.MaxCorners);
                for (int c = 1; c < limit; c++)
                    buffer.Add(new PathPoint { Position = _cornerBuffer[c] });
            }

            EntityManager.SetComponentData(entity, progress);
        }

        private bool TryCalculatePath(
            float3 from, float3 to, in PathfindingSettings settings, out int cornerCount)
        {
            cornerCount = 0;

            // Both ends are snapped onto the surface first: enemies spawn a
            // little above the ground and the player capsule sits at its own
            // centre, so raw positions routinely miss the navmesh and the query
            // fails for reasons that have nothing to do with reachability.
            if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit,
                    settings.NavMeshSampleDistance, NavMesh.AllAreas))
                return false;

            if (!NavMesh.SamplePosition(to, out NavMeshHit toHit,
                    settings.NavMeshSampleDistance, NavMesh.AllAreas))
                return false;

            if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _path))
                return false;

            cornerCount = _path.GetCornersNonAlloc(_cornerBuffer);
            return cornerCount > 0;
        }

        private void EnsureCornerBuffer(int maxCorners)
        {
            int size = math.max(2, maxCorners);
            if (_cornerBuffer == null || _cornerBuffer.Length < size)
                _cornerBuffer = new Vector3[size];
        }
    }
}
