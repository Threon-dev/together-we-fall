using Unity.Entities;

namespace TogetherWeFall.Equipment
{
    /// <summary>
    /// Everything that reads set membership and what is currently in force.
    ///
    /// A struct of static methods, the same shape as EquipmentSlots, GridFit and
    /// GemSockets and for the same reason: the evaluation system needs these
    /// answers to rebuild the status buffer, the stat fold needs them to sum a
    /// bonus, and the panel needs them to say "2 of 4 worn". Three callers, one
    /// set of rules.
    /// </summary>
    public struct ItemSets
    {
        /// <summary>
        /// Which set an item belongs to, or -1.
        ///
        /// ponytail: linear over sets, linear over their members. A set holds a
        /// handful of pieces and a game holds a handful of sets, so this is
        /// cheaper than the index that would have to be kept in step with the
        /// blob — and it is only ever asked when equipment changed or a tooltip
        /// opened. An id map belongs here the day set count is measured in
        /// hundreds.
        ///
        /// The FIRST set that names the item wins, so an item listed by two sets
        /// counts for one of them rather than for both. That is the MVP rule:
        /// hybrid membership is a data model question, not a missing branch.
        /// </summary>
        public static int SetIndexOf(ItemSetDatabase sets, int itemId)
        {
            if (!sets.IsCreated || itemId == 0)
                return -1;

            ref ItemSetDatabaseBlob blob = ref sets.Value.Value;

            for (int s = 0; s < blob.Sets.Length; s++)
            {
                // By reference: ItemSetBlob carries blob arrays, and a copy of
                // it would point them at whatever sits near the copy.
                ref ItemSetBlob set = ref blob.Sets[s];

                for (int m = 0; m < set.MemberItemIds.Length; m++)
                {
                    if (set.MemberItemIds[m] == itemId)
                        return s;
                }
            }

            return -1;
        }

        /// <summary>
        /// Rebuilds what a character's sets are worth from what they are wearing.
        ///
        /// Whole, from nothing, every time: the buffer is a cache of the gear and
        /// the gear is the only truth. One entry per set the wearer has ANY
        /// piece of — including the ones that reach no step yet, because "1 of 4"
        /// is exactly what the tooltip has to be able to say.
        /// </summary>
        public static void Evaluate(
            ItemSetDatabase sets,
            DynamicBuffer<EquippedItem> slots,
            DynamicBuffer<ActiveSetBonusStatus> active)
        {
            active.Clear();

            if (!sets.IsCreated)
                return;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].HasItem)
                    continue;

                int setIndex = SetIndexOf(sets, slots[i].ItemId);
                if (setIndex < 0)
                    continue;

                // Two pieces means two slots, whether or not they are the same
                // item: a pair of identical rings from one set is two pieces,
                // which is the rule everywhere this genre counts them.
                int entry = IndexOfSet(active, setIndex);

                if (entry >= 0)
                {
                    ActiveSetBonusStatus counted = active[entry];
                    counted.CurrentEquippedCount++;
                    active[entry] = counted;
                    continue;
                }

                ref ItemSetBlob set = ref sets.Value.Value.Sets[setIndex];

                active.Add(new ActiveSetBonusStatus
                {
                    SetId = set.SetId,
                    SetIndex = setIndex,
                    CurrentEquippedCount = 1,
                    HighestActiveThreshold = 0,
                    FlatBonuses = StatBlock.Zero(),
                    IncreasedBonuses = StatBlock.Zero(),
                    BonusKeystone = KeystoneEffect.None
                });
            }

            // The rewards in a second pass, because the counts are only final
            // once every slot has been walked.
            for (int e = 0; e < active.Length; e++)
            {
                ActiveSetBonusStatus status = active[e];
                ref ItemSetBlob set = ref sets.Value.Value.Sets[status.SetIndex];

                if (TryHighestReached(ref set, status.CurrentEquippedCount, out int step))
                {
                    SetThresholdBlob threshold = set.Thresholds[step];

                    status.HighestActiveThreshold = threshold.RequiredPieceCount;
                    status.FlatBonuses = threshold.FlatBonuses;
                    status.IncreasedBonuses = threshold.IncreasedBonuses;
                    status.BonusSupport = threshold.BonusSupport;
                    status.HasBonusSupport = threshold.HasBonusSupport;
                    status.BonusKeystone = threshold.BonusKeystone;
                }

                active[e] = status;
            }
        }

        /// <summary>
        /// The highest step this many pieces reaches, as an index into the set's
        /// thresholds, or false when none is reached.
        ///
        /// Highest wins outright. Three pieces with steps at two and four is the
        /// two-piece bonus and nothing else — the four-piece is not partially
        /// paid out, and the two-piece is not added to it later.
        /// </summary>
        public static bool TryHighestReached(ref ItemSetBlob set, int equipped, out int step)
        {
            step = -1;
            int best = 0;

            for (int t = 0; t < set.Thresholds.Length; t++)
            {
                int required = set.Thresholds[t].RequiredPieceCount;

                if (required > equipped || required <= best)
                    continue;

                best = required;
                step = t;
            }

            return step >= 0;
        }

        /// <summary>Where this set sits in the status buffer, or -1.</summary>
        public static int IndexOfSet(DynamicBuffer<ActiveSetBonusStatus> active, int setIndex)
        {
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i].SetIndex == setIndex)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// How many pieces of this item's set the character is wearing.
        ///
        /// The panel's question, and answered here rather than there so the
        /// tooltip and the character sheet cannot count differently. Zero when
        /// the item is in no set, when nothing of it is worn, or when the
        /// character has no status buffer at all.
        /// </summary>
        public static int EquippedCount(
            EntityManager entityManager, Entity character, ItemSetDatabase sets, int itemId)
        {
            int setIndex = SetIndexOf(sets, itemId);

            if (setIndex < 0 || character == Entity.Null ||
                !entityManager.Exists(character) ||
                !entityManager.HasBuffer<ActiveSetBonusStatus>(character))
            {
                return 0;
            }

            DynamicBuffer<ActiveSetBonusStatus> active =
                entityManager.GetBuffer<ActiveSetBonusStatus>(character, isReadOnly: true);

            int entry = IndexOfSet(active, setIndex);
            return entry >= 0 ? active[entry].CurrentEquippedCount : 0;
        }
    }
}
