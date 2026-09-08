using Unity.Burst;
using Unity.Entities;

namespace TogetherWeFall.Equipment.Systems
{
    /// <summary>
    /// Recomputes a character sheet when, and only when, the equipment behind it
    /// changed.
    ///
    /// The query matches characters whose StatsDirty flag is UP — deliberately
    /// without WithPresent, unlike the system that raises it. So on a frame
    /// where nobody changed gear, this system iterates nothing at all. That is
    /// the caching: not a stored result that something has to remember to
    /// invalidate, but work that is only reachable through the flag that says it
    /// is needed.
    ///
    /// The maths is PoE's: final = (base + flat) * (1 + increased), with all
    /// increases to one stat summed before being applied once. Two 50% increases
    /// give 2x, not 2.25x, and that difference is the entire reason the two
    /// modifier kinds are modelled separately.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentSystem))]
    public partial struct PlayerStatsSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ItemDatabase>();
            state.RequireForUpdate<CharacterBaseStats>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ItemDatabase items = SystemAPI.GetSingleton<ItemDatabase>();
            StatBlock characterBase = SystemAPI.GetSingleton<CharacterBaseStats>().Value;

            foreach ((RefRW<PlayerStats> stats,
                      RefRW<KeystoneComponent> keystone,
                      DynamicBuffer<EquippedItem> slots,
                      EnabledRefRW<StatsDirty> dirty) in
                     SystemAPI.Query<RefRW<PlayerStats>,
                         RefRW<KeystoneComponent>,
                         DynamicBuffer<EquippedItem>,
                         EnabledRefRW<StatsDirty>>())
            {
                stats.ValueRW.Final = Compute(characterBase, slots, items);

                // On the same pass and the same flag, because it is the same
                // kind of fact: authored on items, read off what is worn, and
                // stale the moment the gear changes. A second system on a second
                // flag would be a second thing to remember to raise.
                keystone.ValueRW = ChooseKeystone(slots, items);

                // Bumped after both, so the panel's "has anything changed"
                // covers the keystone too. It is drawn beside the stats and a
                // ring swapped for another ring changes nothing else.
                stats.ValueRW.Version++;

                dirty.ValueRW = false;
            }
        }

        /// <summary>
        /// Which rule the wearer is breaking, and how many they are not.
        ///
        /// First slot in enum order wins. It is an arbitrary rule, but it is a
        /// STABLE one — two rings swapped between Ring1 and Ring2 keep the same
        /// keystone in force — and stability is the whole of what makes it
        /// explainable. Merging two keystones is a design conversation; picking
        /// whichever the loop reached first would be a coin toss the player
        /// cannot see.
        /// </summary>
        private static KeystoneComponent ChooseKeystone(
            DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            var chosen = KeystoneEffect.None;
            int ignored = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].HasItem)
                    continue;

                int index = items.IndexOf(slots[i].ItemId);
                if (index < 0)
                    continue;

                ref ItemBlob item = ref items.Value.Value.Items[index];

                if (item.Keystone == KeystoneEffect.None)
                    continue;

                if (chosen == KeystoneEffect.None)
                    chosen = item.Keystone;
                else
                    ignored++;
            }

            return new KeystoneComponent { Effect = chosen, Ignored = ignored };
        }

        private static StatBlock Compute(
            StatBlock characterBase, DynamicBuffer<EquippedItem> slots, ItemDatabase items)
        {
            // The character sheet is the starting flat pile; item base stats add
            // to the same pile, which is what makes "a sword with 22 damage"
            // mean the same thing as "22 damage on the character".
            StatBlock flat = characterBase;
            StatBlock increased = StatBlock.Zero();

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].HasItem)
                    continue;

                int index = items.IndexOf(slots[i].ItemId);
                if (index < 0)
                    continue;

                // By reference: ItemBlob holds a BlobArray, and a copy of it
                // would point its affixes at whatever sits near the copy.
                ref ItemBlob item = ref items.Value.Value.Items[index];

                Accumulate(ref flat, ref increased, ref item);
            }

            StatBlock final = StatBlock.Zero();

            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;

                // Increases are authored as percentages, so 25 means +25%.
                final.Set(stat, flat.Get(stat) * (1f + increased.Get(stat) * 0.01f));
            }

            return final;
        }

        private static void Accumulate(ref StatBlock flat, ref StatBlock increased, ref ItemBlob item)
        {
            for (int s = 0; s < StatBlock.StatCount; s++)
            {
                var stat = (StatKind)s;
                flat.Add(stat, item.BaseStats.Get(stat));
            }

            for (int a = 0; a < item.Affixes.Length; a++)
            {
                // AffixBlob is plain data with no blob array inside, so a copy is
                // safe here in a way that copying the item never is.
                AffixBlob affix = item.Affixes[a];

                if (affix.Kind == ModifierKind.Flat)
                    flat.Add(affix.Stat, affix.Value);
                else
                    increased.Add(affix.Stat, affix.Value);
            }
        }
    }
}
