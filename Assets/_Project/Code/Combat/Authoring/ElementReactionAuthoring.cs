using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.Combat.Authoring
{
    /// <summary>
    /// Bakes the reaction table into a blob. Lives in the SubScene beside the
    /// skill and loot databases.
    ///
    /// A blob rather than managed references for the reason that runs through
    /// the whole project: a job cannot read a ScriptableObject, and reactions are
    /// resolved inside one. The other half of the reason is the networked build —
    /// what two elements do together has to be the data the host owns, not
    /// whatever each client happens to have on disk.
    ///
    /// Without this object in the scene there is no database, and both element
    /// systems require it — so a scene that has not been rebuilt plays exactly as
    /// it did before, with no statuses and no reactions. That is the intended
    /// failure: quiet, complete, and obvious the moment anyone goes looking.
    /// </summary>
    public sealed class ElementReactionAuthoring : MonoBehaviour
    {
        [SerializeField] private ElementReactionTable _table;

        public ElementReactionTable Table => _table;

        private sealed class ElementReactionBaker : Baker<ElementReactionAuthoring>
        {
            public override void Bake(ElementReactionAuthoring authoring)
            {
                if (authoring.Table == null)
                {
                    Debug.LogError(
                        $"[{nameof(ElementReactionAuthoring)}] No reaction table assigned — " +
                        "nothing will be ignited, shocked or chilled.", authoring);
                    return;
                }

                DependsOnTable(authoring.Table);

                Entity entity = GetEntity(TransformUsageFlags.None);

                List<StatusEffectDefinition> statuses = CollectStatuses(authoring.Table);

                BlobAssetReference<ElementReactionBlob> blob =
                    Build(authoring.Table, statuses, authoring);

                AddBlobAsset(ref blob, out _);
                AddComponent(entity, new ElementReactionDatabase { Value = blob });
            }

            /// <summary>
            /// Re-bake when a rule or a status changes, not only when the table
            /// itself does. Their numbers are baked into this blob, and without
            /// this an edit would silently fail to reach the game.
            /// </summary>
            private void DependsOnTable(ElementReactionTable table)
            {
                DependsOn(table);

                StatusEffectDefinition[] all = table.Statuses;
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null)
                            DependsOn(all[i]);
                    }
                }

                StatusEffectDefinition[] defaults = table.DefaultStatuses;
                if (defaults != null)
                {
                    for (int i = 0; i < defaults.Length; i++)
                    {
                        if (defaults[i] != null)
                            DependsOn(defaults[i]);
                    }
                }

                ElementReactionRule[] rules = table.Rules;
                if (rules == null)
                    return;

                for (int i = 0; i < rules.Length; i++)
                {
                    if (rules[i] == null)
                        continue;

                    DependsOn(rules[i]);

                    if (rules[i].ResultingStatus != null)
                        DependsOn(rules[i].ResultingStatus);
                }
            }

            /// <summary>
            /// Every status named anywhere in the table, once each.
            ///
            /// Gathered from all three places a status can be named — the flat
            /// list, what an element leaves on its own, and what a reaction
            /// produces — so that a status used in only one of them still has an
            /// index to be referred to by. The flat list is what control and
            /// debuff statuses arrive through: nothing leaves a Stun on its own
            /// and no pair of elements produces one, so without it a skill that
            /// stuns would name a status the runtime has never heard of.
            /// </summary>
            private static List<StatusEffectDefinition> CollectStatuses(ElementReactionTable table)
            {
                var statuses = new List<StatusEffectDefinition>();

                StatusEffectDefinition[] all = table.Statuses;
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        AddStatus(statuses, all[i]);
                }

                StatusEffectDefinition[] defaults = table.DefaultStatuses;
                if (defaults != null)
                {
                    for (int i = 0; i < defaults.Length; i++)
                        AddStatus(statuses, defaults[i]);
                }

                ElementReactionRule[] rules = table.Rules;
                if (rules == null)
                    return statuses;

                for (int i = 0; i < rules.Length; i++)
                {
                    if (rules[i] != null)
                        AddStatus(statuses, rules[i].ResultingStatus);
                }

                return statuses;
            }

            private static void AddStatus(
                List<StatusEffectDefinition> statuses, StatusEffectDefinition status)
            {
                if (status != null && !statuses.Contains(status))
                    statuses.Add(status);
            }

            private static BlobAssetReference<ElementReactionBlob> Build(
                ElementReactionTable table, List<StatusEffectDefinition> statuses, Object context)
            {
                using var builder = new BlobBuilder(Allocator.Temp);

                ref ElementReactionBlob root = ref builder.ConstructRoot<ElementReactionBlob>();

                BuildStatuses(builder, ref root, statuses);
                BuildStatusesByType(builder, ref root, statuses, context);
                BuildDefaults(builder, ref root, table, statuses, context);
                BuildRules(builder, ref root, table, statuses, context);

                return builder.CreateBlobAssetReference<ElementReactionBlob>(Allocator.Persistent);
            }

            private static void BuildStatuses(
                BlobBuilder builder,
                ref ElementReactionBlob root,
                List<StatusEffectDefinition> statuses)
            {
                BlobBuilderArray<StatusBlob> blobStatuses =
                    builder.Allocate(ref root.Statuses, statuses.Count);

                for (int i = 0; i < statuses.Count; i++)
                {
                    blobStatuses[i] = new StatusBlob
                    {
                        Name = ToFixedString(statuses[i].DisplayName),
                        Type = statuses[i].Type,
                        Element = statuses[i].Element,
                        Duration = statuses[i].Duration,
                        MaxStacks = statuses[i].MaxStacks,
                        DamagePerSecond = statuses[i].DamagePerSecond,
                        TickInterval = statuses[i].TickInterval,
                        MagnitudePerStack = statuses[i].MagnitudePerStack,
                        StacksDuration = statuses[i].StacksDuration,
                        ImmunityMultiplier = statuses[i].ImmunityMultiplier
                    };
                }
            }

            /// <summary>
            /// Which asset answers to each status type.
            ///
            /// The one index a skill can reach a status through, since the skill
            /// database is baked elsewhere and shares no ordering with this one.
            /// Two assets claiming the same type is an error rather than a
            /// warning: the loser would be authored, wired in, visible in the
            /// table and never once applied, which is the worst kind of quiet.
            ///
            /// A status left at None is the shape an asset written before the
            /// type existed comes back as. It is reported for the same reason —
            /// it has no identity, so nothing can name it and no reaction can
            /// tell it apart from the next one.
            /// </summary>
            private static void BuildStatusesByType(
                BlobBuilder builder,
                ref ElementReactionBlob root,
                List<StatusEffectDefinition> statuses,
                Object context)
            {
                BlobBuilderArray<int> byType =
                    builder.Allocate(ref root.StatusByType, StatusMask.Count);

                for (int i = 0; i < StatusMask.Count; i++)
                    byType[i] = -1;

                for (int i = 0; i < statuses.Count; i++)
                {
                    StatusEffectType type = statuses[i].Type;

                    if (type == StatusEffectType.None)
                    {
                        Debug.LogError(
                            $"[{nameof(ElementReactionAuthoring)}] Status " +
                            $"{statuses[i].DisplayName} has no type. Nothing can apply it by " +
                            "name and it cannot be told apart from any other — set its Type.",
                            context);
                        continue;
                    }

                    int slot = (int)type;
                    if (slot < 0 || slot >= StatusMask.Count)
                        continue;

                    if (byType[slot] >= 0)
                    {
                        Debug.LogError(
                            $"[{nameof(ElementReactionAuthoring)}] Two statuses claim {type}. " +
                            $"{statuses[i].DisplayName} is ignored — a type has one asset.",
                            context);
                        continue;
                    }

                    byType[slot] = i;
                }
            }

            /// <summary>
            /// What each element leaves behind, indexed by the element itself.
            ///
            /// Which element a default status belongs to is read off the status
            /// rather than authored beside it in the list. A second field would
            /// be a second answer to a question the status already answers, and
            /// the two would eventually disagree.
            /// </summary>
            private static void BuildDefaults(
                BlobBuilder builder,
                ref ElementReactionBlob root,
                ElementReactionTable table,
                List<StatusEffectDefinition> statuses,
                Object context)
            {
                BlobBuilderArray<int> defaults =
                    builder.Allocate(ref root.DefaultStatus, ElementMask.Count);

                for (int i = 0; i < ElementMask.Count; i++)
                    defaults[i] = -1;

                StatusEffectDefinition[] authored = table.DefaultStatuses;
                if (authored == null)
                    return;

                for (int i = 0; i < authored.Length; i++)
                {
                    if (authored[i] == null)
                        continue;

                    // A stun is not what fire leaves behind. Without this the
                    // corrected Element property hands back Physical for every
                    // status that carries none, and the first one listed would
                    // quietly become what a physical blow marks with.
                    if (!authored[i].CarriesElement)
                    {
                        Debug.LogWarning(
                            $"[{nameof(ElementReactionAuthoring)}] {authored[i].DisplayName} is " +
                            $"a {authored[i].Category} status and carries no element, so no " +
                            "element can leave it behind. Listed as a default and ignored.",
                            context);
                        continue;
                    }

                    int element = (int)authored[i].Element;
                    if (element < 0 || element >= ElementMask.Count)
                        continue;

                    if (defaults[element] >= 0)
                    {
                        Debug.LogWarning(
                            $"[{nameof(ElementReactionAuthoring)}] Two default statuses for " +
                            $"{authored[i].Element}. {authored[i].DisplayName} is ignored — an " +
                            "element leaves exactly one mark.", context);
                        continue;
                    }

                    defaults[element] = statuses.IndexOf(authored[i]);
                }
            }

            private static void BuildRules(
                BlobBuilder builder,
                ref ElementReactionBlob root,
                ElementReactionTable table,
                List<StatusEffectDefinition> statuses,
                Object context)
            {
                BlobBuilderArray<ElementReactionRuleBlob> rules =
                    builder.Allocate(ref root.Rules, ElementMask.Count * ElementMask.Count);

                for (int i = 0; i < rules.Length; i++)
                    rules[i] = new ElementReactionRuleBlob { ResultStatus = -1 };

                ElementReactionRule[] authored = table.Rules;
                if (authored == null)
                    return;

                for (int i = 0; i < authored.Length; i++)
                {
                    ElementReactionRule rule = authored[i];
                    if (rule == null)
                        continue;

                    // An element does not combine with more of itself, so a rule
                    // saying it does can never fire. Caught here rather than left
                    // to be puzzled over in play.
                    if (rule.ExistingElement == rule.IncomingElement)
                    {
                        Debug.LogWarning(
                            $"[{nameof(ElementReactionAuthoring)}] Rule {rule.name} reacts " +
                            $"{rule.ExistingElement} with itself, which never happens.", context);
                        continue;
                    }

                    int index = (int)rule.ExistingElement * ElementMask.Count +
                                (int)rule.IncomingElement;

                    if (rules[index].Kind != ElementReactionKind.None)
                    {
                        Debug.LogError(
                            $"[{nameof(ElementReactionAuthoring)}] Two rules claim " +
                            $"{rule.ExistingElement} + {rule.IncomingElement}. {rule.name} is " +
                            "ignored — a pair has one outcome.", context);
                        continue;
                    }

                    int resultStatus = rule.ResultingStatus != null
                        ? statuses.IndexOf(rule.ResultingStatus)
                        : -1;

                    if (rule.Result == ElementReactionKind.ApplyStatus && resultStatus < 0)
                    {
                        Debug.LogError(
                            $"[{nameof(ElementReactionAuthoring)}] Rule {rule.name} applies a " +
                            "status but names none — it will do nothing.", context);
                    }

                    rules[index] = new ElementReactionRuleBlob
                    {
                        Kind = rule.Result,
                        DamageMultiplier = rule.DamageMultiplier,
                        ResultStatus = resultStatus,
                        ConsumesExisting = rule.ConsumesExistingStatus,
                        Radius = rule.Radius
                    };
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
