using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;

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

        public CharacterConfig Config => _config;

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
            }
        }
    }
}
