using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Counts how much of each set a character is wearing, and what that is
    /// currently worth.
    ///
    /// On the same flag as the stat maths, deliberately: a set bonus is stale
    /// under exactly the conditions a stat is stale, and a second dirty flag
    /// would be a second thing to remember to raise. The query matches
    /// characters whose StatsDirty is UP and — unlike PlayerStatsSystem — does
    /// not lower it. That is the whole of the ordering: this runs first, fills
    /// the buffer, and the stat system folds the buffer in and then clears the
    /// flag for both.
    ///
    /// A scene with no sets baked never runs this at all, and a character then
    /// carries an empty buffer that every reader treats as "no set bonuses" —
    /// which is what it is.
    /// </summary>
    /// <remarks>
    /// Ordered after BOTH systems that raise StatsDirty and before the one that
    /// lowers it, which is the whole of the correctness argument: a raise that
    /// landed between this system and the stat fold would be a set bonus that
    /// stayed missing until the next time the player touched their gear, and
    /// that is precisely the bug a flag nobody owns produces. Two raisers today
    /// — the equip transaction and the starter kit — and a third has to be added
    /// here too.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentSystem))]
    [UpdateAfter(typeof(TogetherWeFall.Skills.Systems.StarterKitSystem))]
    [UpdateBefore(typeof(PlayerStatsSystem))]
    public partial struct SetBonusEvaluationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemSetDatabase>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ItemSetDatabase sets = SystemAPI.GetSingleton<ItemSetDatabase>();

            foreach ((DynamicBuffer<EquippedItem> slots,
                      DynamicBuffer<ActiveSetBonusStatus> active) in
                     SystemAPI.Query<DynamicBuffer<EquippedItem>,
                         DynamicBuffer<ActiveSetBonusStatus>>()
                         .WithAll<StatsDirty>())
            {
                ItemSets.Evaluate(sets, slots, active);
            }
        }
    }
}
