using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Inventory;
using TogetherWeFall.Skills.Systems;

namespace TogetherWeFall.Equipment.Authoring
{
    /// <summary>
    /// Bakes the character sheet everyone starts from. Lives in the SubScene
    /// beside the wave spawner and the loot database.
    ///
    /// Its own authoring object rather than a field on the simulation settings:
    /// those numbers are performance knobs tuned together during a profiling
    /// pass, and these are balance. Mixing them would mean one of the two is
    /// always in the wrong place when you go looking for it.
    /// </summary>
    public sealed class CharacterStatsAuthoring : MonoBehaviour
    {
        [SerializeField] private CharacterConfig _config;

        [Tooltip("What a new character is handed. Its own asset because it is a " +
                 "test dial rather than balance: duplicate it and point a scene " +
                 "at the copy to try a loadout without touching the sheet " +
                 "everybody plays with. Leave it empty for a character that " +
                 "starts with nothing.")]
        [SerializeField] private StarterKitConfig _starterKit;

        public CharacterConfig Config => _config;
        public StarterKitConfig StarterKit => _starterKit;

        private sealed class CharacterStatsBaker : Baker<CharacterStatsAuthoring>
        {
            public override void Bake(CharacterStatsAuthoring authoring)
            {
                if (authoring.Config == null)
                {
                    Debug.LogError(
                        $"[{nameof(CharacterStatsAuthoring)}] No CharacterConfig assigned — " +
                        "characters would have no base stats and the stat system will not run.",
                        authoring);
                    return;
                }

                DependsOn(authoring.Config);

                Entity entity = GetEntity(TransformUsageFlags.None);

                StatBlock stats = StatBlock.Zero();
                ItemStatValue[] baseStats = authoring.Config.BaseStats;

                if (baseStats != null)
                {
                    // Added rather than assigned, so listing a stat twice reads
                    // as "and also", which is the only sensible meaning.
                    for (int i = 0; i < baseStats.Length; i++)
                    {
                        if (baseStats[i] != null)
                            stats.Add(baseStats[i].Stat, baseStats[i].Value);
                    }
                }

                AddComponent(entity, new CharacterBaseStats { Value = stats });

                // On the same entity as the stats, because it is the same kind
                // of number: what a character is, authored, rather than how fast
                // the simulation should run.
                AddComponent(entity, new CharacterInventorySize
                {
                    Width = authoring.Config.BagWidth,
                    Height = authoring.Config.BagHeight
                });

                // Item ids rather than references: a system cannot hold a
                // managed asset, and the id is the same one the item database
                // is addressed by.
                //
                // The buffer is added even with no kit assigned, because it is
                // what StarterKitSystem requires to run at all — an empty one is
                // a character who starts with nothing, which is a legitimate
                // way to test.
                DynamicBuffer<StarterItem> kit = AddBuffer<StarterItem>(entity);

                if (authoring.StarterKit == null)
                    return;

                DependsOn(authoring.StarterKit);

                StarterKitEntry[] entries = authoring.StarterKit.Entries;

                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i]?.Item == null)
                        continue;

                    DependsOn(entries[i].Item);

                    // A count is flattened here rather than carried into the
                    // simulation: the kit hands out one entity per copy, so ten
                    // coins are ten items whichever end of the pipe counts them,
                    // and the system stays a loop with no arithmetic in it.
                    var granted = new StarterItem
                    {
                        ItemId = entries[i].Item.ItemId,
                        Worn = entries[i].Placement == StarterKitPlacement.Worn
                    };

                    for (int copy = 0; copy < entries[i].Count; copy++)
                        kit.Add(granted);
                }
            }
        }
    }
}
