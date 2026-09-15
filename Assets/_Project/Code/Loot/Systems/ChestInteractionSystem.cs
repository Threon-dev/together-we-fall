using Unity.Collections;
using Unity.Entities;
using TogetherWeFall.Interaction;
using TogetherWeFall.Interaction.Systems;

namespace TogetherWeFall.Loot.Systems
{
    /// <summary>
    /// Opens chests: claims the ones a player reached, then runs the opening
    /// timer and marks the chest as owing its drops.
    ///
    /// What falls out is not decided here — LootTableSystem reads the flag this
    /// one raises. The split is the same one used everywhere in this project:
    /// deciding that something happened and producing its consequences are two
    /// jobs, and keeping them apart is what lets a boss chest, a breakable urn
    /// or a reward for clearing a room all reuse the rolling half untouched.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(InteractionResolveSystem))]
    public partial struct ChestInteractionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChestTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ClaimTriggeredChests(ref state);
            AdvanceOpeningChests(ref state);
        }

        /// <summary>
        /// Takes the claims the resolver handed out.
        ///
        /// Collected first and applied afterwards on purpose: the loop below
        /// switches off the very component its query filters on, and doing that
        /// mid-iteration is exactly the kind of thing that works until the day
        /// two chests land in the same chunk.
        /// </summary>
        private void ClaimTriggeredChests(ref SystemState state)
        {
            using var claims = new NativeList<ChestClaim>(4, Allocator.Temp);

            foreach ((RefRO<ChestState> chest, RefRO<InteractionTriggered> trigger, Entity entity) in
                     SystemAPI.Query<RefRO<ChestState>, RefRO<InteractionTriggered>>()
                         .WithAll<ChestTag>()
                         .WithEntityAccess())
            {
                claims.Add(new ChestClaim
                {
                    Entity = entity,
                    PlayerId = trigger.ValueRO.ByPlayerId,
                    WasClosed = chest.ValueRO.Phase == ChestPhase.Closed
                });
            }

            for (int i = 0; i < claims.Length; i++)
            {
                ChestClaim claim = claims[i];

                // The claim is spent whatever the outcome. Pressing the button on
                // a chest somebody else already took is answered with nothing —
                // not queued until it becomes available, which it never will.
                state.EntityManager.SetComponentEnabled<InteractionTriggered>(claim.Entity, false);

                if (!claim.WasClosed)
                    continue;

                ChestState chest = state.EntityManager.GetComponentData<ChestState>(claim.Entity);
                chest.Phase = ChestPhase.Opening;
                chest.OpenTimer = 0f;
                chest.OpenedByPlayerId = claim.PlayerId;
                state.EntityManager.SetComponentData(claim.Entity, chest);

                // Off the interactable list the instant it is claimed. This is
                // what makes "whoever got there first" hold beyond the single
                // frame the race happened in.
                state.EntityManager.SetComponentEnabled<InteractableTag>(claim.Entity, false);

                UnityEngine.Debug.Log(
                    $"[ChestInteractionSystem] Player {claim.PlayerId} is opening a chest.");
            }
        }

        private void AdvanceOpeningChests(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            // WithPresent is load-bearing, not decoration. LootRollRequest is
            // disabled on every chest that has not finished opening, and without
            // it the query would only ever see chests whose flag is ALREADY up —
            // which is to say, none of the ones this loop exists to raise.
            foreach ((RefRW<ChestState> chest, EnabledRefRW<LootRollRequest> roll) in
                     SystemAPI.Query<RefRW<ChestState>, EnabledRefRW<LootRollRequest>>()
                         .WithAll<ChestTag>()
                         .WithPresent<LootRollRequest>())
            {
                if (chest.ValueRO.Phase != ChestPhase.Opening)
                    continue;

                chest.ValueRW.OpenTimer += deltaTime;

                if (chest.ValueRO.OpenTimer < chest.ValueRO.OpenDuration)
                    continue;

                chest.ValueRW.Phase = ChestPhase.Opened;
                roll.ValueRW = true;
            }
        }

        /// <summary>One chest a player reached this frame.</summary>
        private struct ChestClaim
        {
            public Entity Entity;
            public int PlayerId;

            /// <summary>False when somebody had already claimed it.</summary>
            public bool WasClosed;
        }
    }
}
