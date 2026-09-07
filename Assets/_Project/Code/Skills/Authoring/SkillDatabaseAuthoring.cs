using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Skills.Authoring
{
    /// <summary>
    /// Bakes every skill and its supports into a blob, plus the projectile
    /// prefab and the loadout a character starts with. Lives in the SubScene
    /// beside the loot database.
    ///
    /// A blob rather than managed references, for the reason that runs through
    /// this whole project: a job cannot read a ScriptableObject, and a host must
    /// work from data it owns rather than from whatever a client has on disk.
    /// Supports are baked INTO the skill rather than listed separately, because
    /// a support only means anything in the context of what it is supporting.
    /// </summary>
    public sealed class SkillDatabaseAuthoring : MonoBehaviour
    {
        [Tooltip("Every skill in the game. A loadout slot refers to one by its " +
                 "index in this list.")]
        [SerializeField] private SkillDefinition[] _skills;

        [Tooltip("What a fresh character is armed with, in slot order. The first " +
                 "is the primary attack, the second the secondary.")]

        [SerializeField] private GameObject _projectilePrefab;

        [Tooltip("How many triggers deep a chain of skills may go. Two means a " +
                 "skill can trigger a skill that triggers a skill, and there it " +
                 "stops. The rail that keeps a pair of skills triggering each " +
                 "other from filling the frame.")]
        [SerializeField, Range(0, 4)] private int _maxTriggerDepth = 2;

        [Tooltip("Projectiles created once and reused forever. Also the ceiling " +
                 "on how many can be in the air: past it, a shot is simply not " +
                 "fired, which under a barrage nobody can see.")]
        [SerializeField, Range(16, 1024)] private int _projectilePoolSize = 256;

        [Tooltip("Area effects resolved per frame. Several players emptying area " +
                 "skills into one crowd should cost more frames, not one long one.")]
        [SerializeField, Range(1, 256)] private int _maxAreasPerFrame = 12;

        [Tooltip("Hits turned into damage per frame. A blast that caught three " +
                 "hundred enemies is three hundred of these.")]
        [SerializeField, Range(16, 4096)] private int _maxHitsPerFrame = 512;

        public SkillDefinition[] Skills => _skills;
        public GameObject ProjectilePrefab => _projectilePrefab;
        public int MaxTriggerDepth => _maxTriggerDepth;
        public int ProjectilePoolSize => _projectilePoolSize;
        public int MaxAreasPerFrame => _maxAreasPerFrame;
        public int MaxHitsPerFrame => _maxHitsPerFrame;

        private sealed class SkillDatabaseBaker : Baker<SkillDatabaseAuthoring>
        {
            public override void Bake(SkillDatabaseAuthoring authoring)
            {
                if (authoring.Skills == null || authoring.Skills.Length == 0 ||
                    authoring.ProjectilePrefab == null)
                {
                    Debug.LogError(
                        $"[{nameof(SkillDatabaseAuthoring)}] No skills or no projectile prefab " +
                        "assigned — nothing will be castable.", authoring);
                    return;
                }

                DependsOnSkills(authoring.Skills);

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new SkillPrefabs
                {
                    Projectile = GetEntity(authoring.ProjectilePrefab, TransformUsageFlags.Dynamic),
                    ProjectilePoolSize = authoring.ProjectilePoolSize
                });

                AddComponent(entity, new SkillBudgetSettings
                {
                    MaxAreasPerFrame = authoring.MaxAreasPerFrame,
                    MaxHitsPerFrame = authoring.MaxHitsPerFrame
                });

                BlobAssetReference<SkillDatabaseBlob> database =
                    BuildDatabase(authoring.Skills, authoring);
                AddBlobAsset(ref database, out _);
                AddComponent(entity, new SkillDatabase { Value = database });

                AddComponent(entity, new SkillTriggerSettings
                {
                    MaxDepth = authoring.MaxTriggerDepth
                });

            }

            /// <summary>
            /// Re-bake when a support changes, not only when a skill does. A
            /// support whose numbers were edited belongs to a different blob, and
            /// without this the edit would silently fail to reach the game.
            /// </summary>
            private void DependsOnSkills(SkillDefinition[] skills)
            {
                for (int i = 0; i < skills.Length; i++)
                {
                    SkillDefinition skill = skills[i];
                    if (skill == null)
                        continue;

                    DependsOn(skill);

                    SkillModifier[] modifiers = skill.Modifiers;
                    if (modifiers == null)
                        continue;

                    for (int m = 0; m < modifiers.Length; m++)
                    {
                        if (modifiers[m] == null)
                            continue;

                        DependsOn(modifiers[m]);

                        // The triggered skill too: its numbers are baked into
                        // the same blob, so editing it has to re-bake this.
                        if (modifiers[m].TriggeredSkill != null)
                            DependsOn(modifiers[m].TriggeredSkill);
                    }
                }
            }

            private static int IndexOf(SkillDefinition[] skills, SkillDefinition skill)
            {
                if (skill == null)
                    return -1;

                for (int i = 0; i < skills.Length; i++)
                {
                    if (skills[i] == skill)
                        return i;
                }

                return -1;
            }

            private static BlobAssetReference<SkillDatabaseBlob> BuildDatabase(
                SkillDefinition[] skills, Object context)
            {
                using var builder = new BlobBuilder(Allocator.Temp);

                ref SkillDatabaseBlob root = ref builder.ConstructRoot<SkillDatabaseBlob>();
                BlobBuilderArray<SkillBlob> blobSkills =
                    builder.Allocate(ref root.Skills, skills.Length);

                for (int i = 0; i < skills.Length; i++)
                    BuildSkill(builder, ref blobSkills[i], skills[i], skills, context);

                return builder.CreateBlobAssetReference<SkillDatabaseBlob>(Allocator.Persistent);
            }

            /// <summary>
            /// Which skill a trigger support points at, as an index.
            ///
            /// A support pointing at a skill that is not in the list has nothing
            /// to resolve to, and a support pointing at its own host is the one
            /// cycle worth catching by name — the depth budget stops it at
            /// runtime, but at bake time it is almost certainly a mistake.
            /// </summary>
            private static int ResolveTrigger(
                SkillModifier modifier,
                SkillDefinition host,
                SkillDefinition[] skills,
                Object context)
            {
                if (modifier.Kind != SkillModifierKind.TriggerOnHit)
                    return 0;

                SkillDefinition triggered = modifier.TriggeredSkill;

                if (triggered == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(SkillDatabaseAuthoring)}] Support '{modifier.name}' triggers " +
                        "nothing — no skill assigned.", context);
                    return 0;
                }

                if (triggered == host)
                {
                    Debug.LogWarning(
                        $"[{nameof(SkillDatabaseAuthoring)}] Skill '{host.DisplayName}' triggers " +
                        "itself. The depth budget will stop it, but this is almost certainly " +
                        "not what was meant.", context);
                }

                if (IndexOf(skills, triggered) < 0)
                {
                    Debug.LogError(
                        $"[{nameof(SkillDatabaseAuthoring)}] Support '{modifier.name}' triggers " +
                        $"'{triggered.DisplayName}', which is not in the skills list — nothing " +
                        "will resolve that id at runtime, so the trigger will do nothing.",
                        context);
                }

                // The id, not the index: a support can arrive from a gem baked
                // by a different authoring object, and every consumer resolves
                // ids the same way.
                return triggered.SkillId;
            }

            private static void BuildSkill(
                BlobBuilder builder,
                ref SkillBlob blob,
                SkillDefinition skill,
                SkillDefinition[] skills,
                Object context)
            {
                if (skill == null)
                {
                    // A hole in the list still needs a valid entry, or every
                    // index after it would shift and the loadout would arm the
                    // wrong skill.
                    builder.Allocate(ref blob.Modifiers, 0);
                    return;
                }

                blob.Name = ToFixedString(skill.DisplayName);
                blob.SkillId = skill.SkillId;
                blob.Effect = skill.Effect;
                blob.Type = skill.DamageType;
                blob.BaseDamage = skill.BaseDamage;
                blob.Cooldown = skill.Cooldown;
                blob.Range = skill.Range;
                blob.Radius = skill.Radius;
                blob.ArcDegrees = skill.ArcDegrees;
                blob.ProjectileSpeed = skill.ProjectileSpeed;
                blob.BaseChains = skill.BaseChains;
                blob.ChainRange = skill.ChainRange;
                blob.ChainDelay = skill.ChainDelay;

                SkillModifier[] modifiers = skill.Modifiers ?? System.Array.Empty<SkillModifier>();

                int valid = 0;
                for (int m = 0; m < modifiers.Length; m++)
                {
                    if (modifiers[m] != null)
                        valid++;
                }

                BlobBuilderArray<SkillModifierBlob> blobModifiers =
                    builder.Allocate(ref blob.Modifiers, valid);

                int cursor = 0;
                for (int m = 0; m < modifiers.Length; m++)
                {
                    if (modifiers[m] == null)
                        continue;

                    blobModifiers[cursor] = new SkillModifierBlob
                    {
                        Kind = modifiers[m].Kind,
                        Value = modifiers[m].Value,
                        SecondaryValue = modifiers[m].SecondaryValue,
                        ConvertTo = modifiers[m].ConvertTo,
                        TriggeredSkillId = ResolveTrigger(modifiers[m], skill, skills, context)
                    };

                    cursor++;
                }
            }

            private static FixedString64Bytes ToFixedString(string value)
            {
                var result = default(FixedString64Bytes);
                result.CopyFromTruncated(value ?? string.Empty);
                return result;
            }
        }
    }
}
