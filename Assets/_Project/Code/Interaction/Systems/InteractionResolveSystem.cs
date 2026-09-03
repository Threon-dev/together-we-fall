using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TogetherWeFall.Interaction.Systems
{
    /// <summary>
    /// Turns "a player pressed the button here" into "this player interacts with
    /// that entity".
    ///
    /// This is the server half of the request/result split, and the only place
    /// in the project that decides what a button press touched. A client sends a
    /// position, never a target: a client that named its own target could name
    /// a chest on the other side of the floor, and no amount of validation
    /// afterwards is as simple as never accepting the claim in the first place.
    ///
    /// It also settles races. Requests are processed in queue order, and a
    /// target already claimed this frame is skipped — so when two players reach
    /// the same chest on the same frame, one of them wins, once, and the loser
    /// finds it taken rather than both being told they opened it.
    ///
    /// What being interacted with MEANS is not decided here. The systems that
    /// own chests and items read the result and do their own thing with it,
    /// which is why adding a lever or a downed ally to revive costs this file
    /// nothing.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct InteractionResolveSystem : ISystem
    {
        private EntityQuery _interactableQuery;

        public void OnCreate(ref SystemState state)
        {
            // InteractableTag is enableable, so this query holds exactly the
            // things that can still be interacted with: an opened chest or a
            // collected item drops out of it by disabling its own tag.
            _interactableQuery = SystemAPI.QueryBuilder()
                .WithAll<InteractableTag, InteractionRadius, LocalTransform>()
                .Build();

            state.RequireForUpdate<InteractionRequestsSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DynamicBuffer<InteractionRequest> requests =
                SystemAPI.GetSingletonBuffer<InteractionRequest>();

            if (requests.Length == 0)
                return;

            using NativeArray<Entity> targets = _interactableQuery.ToEntityArray(Allocator.Temp);

            if (targets.Length == 0)
            {
                requests.Clear();
                return;
            }

            using NativeArray<InteractionRadius> radii =
                _interactableQuery.ToComponentDataArray<InteractionRadius>(Allocator.Temp);

            using NativeArray<LocalTransform> transforms =
                _interactableQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int r = 0; r < requests.Length; r++)
                Resolve(ref state, requests[r], targets, radii, transforms);

            // Requests live for exactly one frame. A press that reached nothing
            // is a press that missed, not one that waits for a target to walk
            // into range.
            requests.Clear();
        }

        private void Resolve(
            ref SystemState state,
            in InteractionRequest request,
            in NativeArray<Entity> targets,
            in NativeArray<InteractionRadius> radii,
            in NativeArray<LocalTransform> transforms)
        {
            int best = -1;
            float bestDistanceSq = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                // Already claimed by an earlier request in this same queue.
                if (state.EntityManager.IsComponentEnabled<InteractionTriggered>(targets[i]))
                    continue;

                float radius = radii[i].Value;
                float distanceSq = math.distancesq(request.Position, transforms[i].Position);

                if (distanceSq > radius * radius || distanceSq >= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                best = i;
            }

            if (best < 0)
                return;

            // Through the EntityManager rather than SystemAPI: this runs in a
            // helper rather than in OnUpdate, and the manager needs no
            // generated state to reach for.
            state.EntityManager.SetComponentData(targets[best], new InteractionTriggered
            {
                ByPlayerId = request.PlayerId
            });

            state.EntityManager.SetComponentEnabled<InteractionTriggered>(targets[best], true);
        }
    }
}
