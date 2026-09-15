using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TogetherWeFall.Shared;

namespace TogetherWeFall.Enemies.Systems
{
    /// <summary>
    /// Picks a target for each enemy from the player position registry.
    ///
    /// "Who do we run at" is decided here and nowhere else. The rest of the
    /// pipeline works with a ready ChaseTarget and knows nothing about the
    /// number of players or the selection rule — so the rule can change
    /// (nearest, then most aggressive, then a threat table) without touching
    /// movement.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ChaseTargetSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerPositionsSingleton>();
            state.RequireForUpdate<EnemyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<PlayerPositionElement> players =
                SystemAPI.GetSingletonBuffer<PlayerPositionElement>(isReadOnly: true);

            if (players.Length == 0)
                return;

            new SelectNearestPlayerJob
            {
                Players = players.AsNativeArray()
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct SelectNearestPlayerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<PlayerPositionElement> Players;

            private void Execute(in LocalTransform transform, ref ChaseTarget target)
            {
                float bestDistanceSq = float.MaxValue;
                int bestIndex = -1;

                for (int i = 0; i < Players.Length; i++)
                {
                    if (!Players[i].IsTargetable)
                        continue;

                    float distanceSq = math.distancesq(transform.Position, Players[i].Position);
                    if (distanceSq >= bestDistanceSq)
                        continue;

                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }

                if (bestIndex < 0)
                {
                    // No living targets — the enemy holds position rather than
                    // running towards the world origin.
                    target.HasTarget = false;
                    return;
                }

                target.Position = Players[bestIndex].Position;
                target.PlayerId = Players[bestIndex].PlayerId;
                target.HasTarget = true;
            }
        }
    }
}
