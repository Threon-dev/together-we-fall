using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Skills;
using Random = Unity.Mathematics.Random;

namespace TogetherWeFall.Loot.Authoring
{
    /// <summary>
    /// Bakes every item and every loot table into blobs, alongside the prefabs
    /// and settings the loot systems need. Lives in the SubScene beside the wave
    /// spawner.
    ///
    /// Two blobs, baked together and on purpose. The item database is the single
    /// description of what an item IS — slot, stats, affixes — and the loot
    /// tables only say how likely each one is to fall out of a chest. Baking
    /// them in one place is what guarantees a table can never name an item the
    /// stat maths does not know.
    ///
    /// Blobs rather than managed references to the ScriptableObjects: a job
    /// cannot read an asset, and — the reason that matters — a host must work
    /// from data it owns rather than from whatever a client has on disk.
    /// </summary>
    public sealed class LootDatabaseAuthoring : MonoBehaviour
    {
        [SerializeField] private LootConfig _config;

        [Tooltip("Tables, in order. A chest refers to one by its index here. " +
                 "Every item used by any table also lands in the item database.")]
        [SerializeField] private LootTable[] _tables;

        [Tooltip("Item sets. Their members land in the item database too, so a " +
                 "set piece exists even when no table drops it — a vendor-only " +
                 "piece is otherwise an item the stat maths never heard of.")]
        [SerializeField] private ItemSetDefinition[] _sets = System.Array.Empty<ItemSetDefinition>();

        [SerializeField] private GameObject _chestPrefab;
        [SerializeField] private GameObject _itemPrefab;

        [Tooltip("Seed for the loot dice. Overwritten from the run seed when a " +
                 "dungeon floor is built, so a strange drop can be reproduced " +
                 "from a single number.")]
        [SerializeField] private uint _lootSeed = 7777;

        public LootConfig Config => _config;
        public LootTable[] Tables => _tables;
        public ItemSetDefinition[] Sets => _sets ?? System.Array.Empty<ItemSetDefinition>();
        public GameObject ChestPrefab => _chestPrefab;
        public GameObject ItemPrefab => _itemPrefab;
        public uint LootSeed => _lootSeed;

        private sealed class LootDatabaseBaker : Baker<LootDatabaseAuthoring>
        {
            /// <summary>Number of values in ItemRarity. Derived, so adding one cannot desync it.</summary>
            private const int RarityCount = (int)ItemRarity.Mythic + 1;

            public override void Bake(LootDatabaseAuthoring authoring)
            {
                if (authoring.Config == null || authoring.ChestPrefab == null ||
                    authoring.ItemPrefab == null || authoring.Tables == null ||
                    authoring.Tables.Length == 0)
                {
                    Debug.LogError(
                        $"[{nameof(LootDatabaseAuthoring)}] Config, prefabs or tables are not " +
                        "assigned — no chests will be placed and nothing will drop.", authoring);
                    return;
                }

                DependsOn(authoring.Config);
                DependsOnTables(authoring.Tables);
                DependsOnSets(authoring.Sets);

                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new LootPrefabs
                {
                    Chest = GetEntity(authoring.ChestPrefab, TransformUsageFlags.Dynamic),
                    Item = GetEntity(authoring.ItemPrefab, TransformUsageFlags.Dynamic)
                });

                List<ItemDefinition> items =
                    CollectItems(authoring.Tables, authoring.Sets, authoring);
                var indexById = new Dictionary<int, int>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    indexById[items[i].ItemId] = i;

                BlobAssetReference<ItemDatabaseBlob> itemDatabase = BuildItemDatabase(items);
                AddBlobAsset(ref itemDatabase, out _);
                AddComponent(entity, new ItemDatabase { Value = itemDatabase });

                // Beside the item database rather than in a bake of its own, for
                // the reason the loot tables are baked here: a set may only name
                // items the stat maths knows, and one bake is what guarantees it.
                BlobAssetReference<ItemSetDatabaseBlob> setDatabase =
                    BuildSetDatabase(authoring.Sets, authoring);
                AddBlobAsset(ref setDatabase, out _);
                AddComponent(entity, new ItemSetDatabase { Value = setDatabase });

                BlobAssetReference<LootDatabaseBlob> lootDatabase =
                    BuildLootDatabase(authoring.Tables, indexById);
                AddBlobAsset(ref lootDatabase, out _);
                AddComponent(entity, new LootDatabase { Value = lootDatabase });

                AddComponent(entity, new LootSettings
                {
                    ChestsPerTreasureRoom = authoring.Config.ChestsPerTreasureRoom,
                    TreasureChestTableId = authoring.Config.TreasureChestTableId,
                    DropScatterRadius = authoring.Config.DropScatterRadius,
                    ItemPoolSize = authoring.Config.ItemPoolSize
                });

                AddComponent(entity, new LootRandom
                {
                    Value = Random.CreateFromIndex(authoring.LootSeed)
                });

                AddRarityColors(entity, authoring.Config);
            }

            /// <summary>
            /// Re-bake when any item changes, not just when a table does. An item
            /// whose rarity or affixes were edited belongs in a different place
            /// in the blob, and without this dependency the blob keeps the old one.
            /// </summary>
            private void DependsOnTables(LootTable[] tables)
            {
                for (int t = 0; t < tables.Length; t++)
                {
                    LootTable table = tables[t];
                    if (table == null)
                        continue;

                    DependsOn(table);

                    LootTableEntry[] entries = table.Entries;
                    if (entries == null)
                        continue;

                    for (int e = 0; e < entries.Length; e++)
                    {
                        ItemDefinition item = entries[e]?.Item;
                        if (item == null)
                            continue;

                        DependsOn(item);

                        // The welded skills too: a name is what the id is
                        // hashed from, so renaming one has to re-bake this.
                        SkillDefinition[] innate = item.InnateSkills;

                        for (int skill = 0; skill < innate.Length; skill++)
                        {
                            if (innate[skill] != null)
                                DependsOn(innate[skill]);
                        }

                        if (item.SignatureSkill != null)
                            DependsOn(item.SignatureSkill);
                    }
                }
            }

            /// <summary>
            /// Re-bake when a set or any of its members changes. Without it a
            /// threshold somebody retuned keeps the old numbers, and a renamed
            /// member keeps an id that no longer hashes from its name.
            /// </summary>
            private void DependsOnSets(ItemSetDefinition[] sets)
            {
                for (int s = 0; s < sets.Length; s++)
                {
                    ItemSetDefinition set = sets[s];
                    if (set == null)
                        continue;

                    DependsOn(set);

                    ItemDefinition[] members = set.Members;

                    for (int m = 0; m < members.Length; m++)
                    {
                        if (members[m] != null)
                            DependsOn(members[m]);
                    }

                    SetBonusThreshold[] thresholds = set.Thresholds;

                    for (int t = 0; t < thresholds.Length; t++)
                    {
                        SkillModifier modifier = thresholds[t]?.BonusSkillModifier;

                        if (modifier != null)
                            DependsOn(modifier);
                    }
                }
            }

            /// <summary>
            /// Every distinct item any table can produce or any set names,
            /// ordered by id.
            ///
            /// Sorted by id rather than by discovery order so the blob comes out
            /// identical whatever order the tables happen to be listed in — and
            /// so a lookup can binary search it.
            ///
            /// Sets are a second source rather than an assumption that a set
            /// piece also drops: a piece sold by a vendor and never rolled by a
            /// chest is a perfectly ordinary item, and leaving it out of the
            /// database would make it an item nothing can equip.
            /// </summary>
            private List<ItemDefinition> CollectItems(
                LootTable[] tables, ItemSetDefinition[] sets, Object context)
            {
                var byId = new Dictionary<int, ItemDefinition>();

                for (int t = 0; t < tables.Length; t++)
                {
                    LootTableEntry[] entries = tables[t]?.Entries;
                    if (entries == null)
                        continue;

                    for (int e = 0; e < entries.Length; e++)
                        Collect(byId, entries[e]?.Item, context);
                }

                for (int s = 0; s < sets.Length; s++)
                {
                    ItemDefinition[] members = sets[s] == null
                        ? System.Array.Empty<ItemDefinition>()
                        : sets[s].Members;

                    for (int m = 0; m < members.Length; m++)
                        Collect(byId, members[m], context);
                }

                var items = new List<ItemDefinition>(byId.Values);
                items.Sort(CompareById);
                return items;
            }

            /// <summary>
            /// Adds one item to the set being baked, and refuses a collision.
            ///
            /// Two assets with the same display name hash to the same id, and
            /// the inventory would not be able to tell them apart. Better a loud
            /// warning at bake time than an item that equips as a different one.
            /// </summary>
            private void Collect(
                Dictionary<int, ItemDefinition> byId, ItemDefinition item, Object context)
            {
                if (item == null)
                    return;

                int id = item.ItemId;

                if (byId.TryGetValue(id, out ItemDefinition existing) && existing != item)
                {
                    Debug.LogError(
                        $"[{nameof(LootDatabaseAuthoring)}] Items '{existing.DisplayName}' " +
                        $"and '{item.DisplayName}' share the id {id}. Give them distinct " +
                        "display names.", context);
                    return;
                }

                byId[id] = item;
            }

            private static int CompareById(ItemDefinition a, ItemDefinition b)
                => a.ItemId.CompareTo(b.ItemId);

            private void AddRarityColors(Entity entity, LootConfig config)
            {
                DynamicBuffer<RarityColor> colors = AddBuffer<RarityColor>(entity);

                for (int r = 0; r < RarityCount; r++)
                {
                    Color color = config.ColorFor((ItemRarity)r);
                    colors.Add(new RarityColor
                    {
                        Value = new float4(color.r, color.g, color.b, color.a)
                    });
                }
            }

            // ─────────────────────────────────────────────────────────────
            // Item database
            // ─────────────────────────────────────────────────────────────

            private static BlobAssetReference<ItemDatabaseBlob> BuildItemDatabase(
                List<ItemDefinition> items)
            {
                using var builder = new BlobBuilder(Allocator.Temp);

                ref ItemDatabaseBlob root = ref builder.ConstructRoot<ItemDatabaseBlob>();
                BlobBuilderArray<ItemBlob> blobItems = builder.Allocate(ref root.Items, items.Count);

                for (int i = 0; i < items.Count; i++)
                    BuildItem(builder, ref blobItems[i], items[i]);

                return builder.CreateBlobAssetReference<ItemDatabaseBlob>(Allocator.Persistent);
            }

            /// <summary>
            /// What a support gem carries, as flat numbers.
            ///
            /// The triggered skill travels as an id for the same reason the
            /// gem's own skill does: this baker has no idea what order the skill
            /// database put things in, and does not need to.
            /// </summary>
            private static SkillModifierBlob BuildSupport(SkillModifier modifier)
            {
                if (modifier == null)
                    return default;

                return new SkillModifierBlob
                {
                    Kind = modifier.Kind,
                    Value = modifier.Value,
                    SecondaryValue = modifier.SecondaryValue,
                    ConvertTo = modifier.ConvertTo,
                    TriggeredSkillId = modifier.TriggeredSkill != null
                        ? ItemDefinition.ComputeId(modifier.TriggeredSkill.DisplayName)
                        : 0,

                    // Read through the properties, which clamp a cooldown away
                    // from zero and a chance into range. The blob is the only
                    // copy the host ever sees, so it is the copy that has to be
                    // sane rather than the asset.
                    TriggerCondition = modifier.TriggerCondition,
                    TriggerCooldown = modifier.TriggerCooldown,
                    ProcChance = modifier.ProcChance,
                    Condition = modifier.Condition,
                    RequiredElement = modifier.RequiredElement,
                    RequiredStatus = modifier.RequiredStatus,
                    RequiredCount = modifier.RequiredCount,
                    AppliedStatus = modifier.AppliedStatusType,
                    Threshold = modifier.Threshold
                };
            }

            private static void BuildItem(BlobBuilder builder, ref ItemBlob blob, ItemDefinition item)
            {
                blob.ItemId = item.ItemId;
                blob.Rarity = item.Rarity;
                blob.Slot = item.Slot;
                blob.Name = ToFixedString(item.DisplayName);

                // Read through the properties: they derive the ring pairing and
                // refuse two-handedness anywhere but the main hand, and the blob
                // is the only copy the host ever sees.
                blob.AllowedSlots = item.AllowedSlots;
                blob.IsTwoHanded = item.IsTwoHanded;

                blob.GemKind = item.GemType;
                blob.GemSkillId = item.GemSkillId;
                blob.GemSupport = BuildSupport(item.GemSupportModifier);

                // The second half of a two-sided gem, and the flag that says it
                // is there at all — a default blob folds to nothing but would
                // still take a place in the group's list.
                blob.HasSupportSecond = item.GemSupportModifierSecond != null;
                blob.GemSupportSecond = BuildSupport(item.GemSupportModifierSecond);

                // Ids rather than indices, like every other cross-database
                // reference here: the item database and the skill database are
                // baked by two authoring objects that share no ordering.
                blob.InnateSkillIds = new FixedList64Bytes<int>();

                for (int candidate = 0;
                     candidate < item.InnateSkillCandidateCount &&
                     blob.InnateSkillIds.Length < blob.InnateSkillIds.Capacity;
                     candidate++)
                {
                    int skillId = item.InnateSkillIdAt(candidate);

                    if (skillId != 0)
                        blob.InnateSkillIds.Add(skillId);
                }

                // Through the property, which caps it by how many candidates
                // were actually authored: a two-handed weapon with one named
                // skill rolls one, rather than welding the same attack twice.
                blob.ActiveSkillCount =
                    Mathf.Min(item.ActiveSkillCount, blob.InnateSkillIds.Length);

                blob.SignatureSkillId = item.SignatureSkillId;

                blob.SocketCount = item.SocketCount;
                blob.LinkGroups = new FixedList32Bytes<byte>();

                for (int socket = 0; socket < item.SocketCount; socket++)
                {
                    // Clamped to a byte because a link group is a small label,
                    // not a number anybody does arithmetic on.
                    blob.LinkGroups.Add((byte)Mathf.Clamp(item.LinkGroupOf(socket), 0, 255));
                }

                // Read through the properties rather than the fields: they clamp
                // the size and refuse rotation on square items, and the blob is
                // the only copy the host ever sees.
                blob.GridWidth = item.GridWidth;
                blob.GridHeight = item.GridHeight;
                blob.CanRotate = item.CanRotate;

                // Through the property too: it refuses a value on a gem.
                blob.CurrencyValue = item.CurrencyValue;

                // Through the property too: it refuses a keystone on a gem, and
                // a gem is the one item that can never be worn.
                blob.Keystone = item.Keystone;

                blob.BaseStats = StatBlock.Zero();

                ItemStatValue[] baseStats = item.BaseStats;
                if (baseStats != null)
                {
                    for (int s = 0; s < baseStats.Length; s++)
                    {
                        if (baseStats[s] != null)
                            blob.BaseStats.Add(baseStats[s].Stat, baseStats[s].Value);
                    }
                }

                ItemAffix[] affixes = item.Affixes ?? System.Array.Empty<ItemAffix>();
                int valid = 0;
                for (int a = 0; a < affixes.Length; a++)
                {
                    if (affixes[a] != null)
                        valid++;
                }

                BlobBuilderArray<AffixBlob> blobAffixes = builder.Allocate(ref blob.Affixes, valid);

                int cursor = 0;
                for (int a = 0; a < affixes.Length; a++)
                {
                    if (affixes[a] == null)
                        continue;

                    blobAffixes[cursor] = new AffixBlob
                    {
                        Stat = affixes[a].Stat,
                        Kind = affixes[a].Kind,
                        Value = affixes[a].Value
                    };

                    cursor++;
                }
            }

            // ─────────────────────────────────────────────────────────────
            // Item sets
            // ─────────────────────────────────────────────────────────────

            /// <summary>
            /// Writes every set, members sorted and steps ascending.
            ///
            /// The affix list of each step is folded into a flat block and an
            /// increased block right here, which is the same split the stat
            /// maths does at the other end — so the host reads a set bonus with
            /// the code it already had, and nothing downstream walks an affix
            /// list a second time.
            /// </summary>
            private BlobAssetReference<ItemSetDatabaseBlob> BuildSetDatabase(
                ItemSetDefinition[] sets, Object context)
            {
                var valid = new List<ItemSetDefinition>(sets.Length);
                var claimedBy = new Dictionary<int, ItemSetDefinition>();

                for (int s = 0; s < sets.Length; s++)
                {
                    if (sets[s] != null)
                        valid.Add(sets[s]);
                }

                using var builder = new BlobBuilder(Allocator.Temp);

                ref ItemSetDatabaseBlob root = ref builder.ConstructRoot<ItemSetDatabaseBlob>();
                BlobBuilderArray<ItemSetBlob> blobSets =
                    builder.Allocate(ref root.Sets, valid.Count);

                for (int s = 0; s < valid.Count; s++)
                    BuildSet(builder, ref blobSets[s], valid[s], claimedBy, context);

                return builder.CreateBlobAssetReference<ItemSetDatabaseBlob>(Allocator.Persistent);
            }

            private void BuildSet(
                BlobBuilder builder,
                ref ItemSetBlob blob,
                ItemSetDefinition set,
                Dictionary<int, ItemSetDefinition> claimedBy,
                Object context)
            {
                blob.SetId = ToFixedString(set.SetId);
                blob.SetName = ToFixedString(set.SetName);

                // Distinct and sorted: an id listed twice would count one worn
                // piece twice, and sorted output means the blob is the same
                // whatever order the inspector happens to show the members in.
                var memberIds = new List<int>();

                ItemDefinition[] members = set.Members;

                for (int m = 0; m < members.Length; m++)
                {
                    if (members[m] == null)
                        continue;

                    int id = members[m].ItemId;

                    // One item, one set. The second set that claims it is told
                    // so rather than quietly granting both bonuses — hybrid
                    // membership is a data model decision, and this is the
                    // warning that says it has not been made.
                    if (claimedBy.TryGetValue(id, out ItemSetDefinition owner) && owner != set)
                    {
                        Debug.LogError(
                            $"[{nameof(LootDatabaseAuthoring)}] '{members[m].DisplayName}' is " +
                            $"named by both '{owner.SetName}' and '{set.SetName}'. An item " +
                            "belongs to one set; it will count for the first only.", context);
                        continue;
                    }

                    claimedBy[id] = set;

                    if (!memberIds.Contains(id))
                        memberIds.Add(id);
                }

                memberIds.Sort();

                BlobBuilderArray<int> blobMembers =
                    builder.Allocate(ref blob.MemberItemIds, memberIds.Count);

                for (int m = 0; m < memberIds.Count; m++)
                    blobMembers[m] = memberIds[m];

                var steps = new List<SetBonusThreshold>();
                SetBonusThreshold[] thresholds = set.Thresholds;

                for (int t = 0; t < thresholds.Length; t++)
                {
                    if (thresholds[t] == null)
                        continue;

                    // A step nobody can reach is authored by mistake, every
                    // time: a six-piece bonus on a four-piece set is a reward
                    // the player can see in the tooltip and never earn.
                    if (thresholds[t].RequiredPieceCount > memberIds.Count)
                    {
                        Debug.LogWarning(
                            $"[{nameof(LootDatabaseAuthoring)}] '{set.SetName}' has a " +
                            $"{thresholds[t].RequiredPieceCount}-piece bonus but only " +
                            $"{memberIds.Count} members — it can never be reached.", context);
                    }

                    steps.Add(thresholds[t]);
                }

                steps.Sort(CompareByRequiredPieces);

                BlobBuilderArray<SetThresholdBlob> blobSteps =
                    builder.Allocate(ref blob.Thresholds, steps.Count);

                for (int t = 0; t < steps.Count; t++)
                {
                    SetBonusThreshold step = steps[t];

                    var threshold = new SetThresholdBlob
                    {
                        RequiredPieceCount = step.RequiredPieceCount,
                        FlatBonuses = StatBlock.Zero(),
                        IncreasedBonuses = StatBlock.Zero(),
                        BonusKeystone = step.BonusKeystone,
                        HasBonusSupport = step.BonusSkillModifier != null,
                        BonusSupport = BuildSupport(step.BonusSkillModifier)
                    };

                    ItemAffix[] bonuses = step.Bonuses;

                    for (int a = 0; a < bonuses.Length; a++)
                    {
                        if (bonuses[a] == null)
                            continue;

                        if (bonuses[a].Kind == ModifierKind.Flat)
                            threshold.FlatBonuses.Add(bonuses[a].Stat, bonuses[a].Value);
                        else
                            threshold.IncreasedBonuses.Add(bonuses[a].Stat, bonuses[a].Value);
                    }

                    blobSteps[t] = threshold;
                }
            }

            private static int CompareByRequiredPieces(SetBonusThreshold a, SetBonusThreshold b)
                => a.RequiredPieceCount.CompareTo(b.RequiredPieceCount);

            // ─────────────────────────────────────────────────────────────
            // Loot tables
            // ─────────────────────────────────────────────────────────────

            private static BlobAssetReference<LootDatabaseBlob> BuildLootDatabase(
                LootTable[] tables, Dictionary<int, int> indexById)
            {
                using var builder = new BlobBuilder(Allocator.Temp);

                ref LootDatabaseBlob root = ref builder.ConstructRoot<LootDatabaseBlob>();
                BlobBuilderArray<LootTableBlob> blobTables =
                    builder.Allocate(ref root.Tables, tables.Length);

                for (int t = 0; t < tables.Length; t++)
                    BuildTable(builder, ref blobTables[t], tables[t], indexById);

                return builder.CreateBlobAssetReference<LootDatabaseBlob>(Allocator.Persistent);
            }

            /// <summary>
            /// Writes one table, entries grouped by rarity.
            ///
            /// Grouping is what turns the second stage of a roll into a slice
            /// instead of a scan, and it is also where a rarity with no items in
            /// the table gets its weight zeroed — otherwise a table could roll
            /// "Mythic" and then have nothing to hand over.
            /// </summary>
            private static void BuildTable(
                BlobBuilder builder,
                ref LootTableBlob blob,
                LootTable table,
                Dictionary<int, int> indexById)
            {
                blob.MinRolls = table.MinRolls;
                blob.MaxRolls = table.MaxRolls;

                LootTableEntry[] entries = table.Entries ?? System.Array.Empty<LootTableEntry>();

                var counts = new int[RarityCount];
                int total = 0;

                for (int e = 0; e < entries.Length; e++)
                {
                    if (entries[e]?.Item == null)
                        continue;

                    counts[(int)entries[e].Item.Rarity]++;
                    total++;
                }

                BlobBuilderArray<float> weights = builder.Allocate(ref blob.RarityWeights, RarityCount);
                BlobBuilderArray<int2> ranges = builder.Allocate(ref blob.RarityRanges, RarityCount);
                BlobBuilderArray<LootEntryBlob> blobEntries = builder.Allocate(ref blob.Entries, total);

                int cursor = 0;

                for (int r = 0; r < RarityCount; r++)
                {
                    var rarity = (ItemRarity)r;

                    weights[r] = counts[r] > 0 ? table.WeightFor(rarity) : 0f;
                    ranges[r] = new int2(cursor, counts[r]);

                    for (int e = 0; e < entries.Length; e++)
                    {
                        ItemDefinition item = entries[e]?.Item;
                        if (item == null || item.Rarity != rarity)
                            continue;

                        blobEntries[cursor] = new LootEntryBlob
                        {
                            ItemIndex = indexById.TryGetValue(item.ItemId, out int index) ? index : 0,
                            Weight = math.max(0f, entries[e].Weight)
                        };

                        cursor++;
                    }
                }
            }

            private static FixedString64Bytes ToFixedString(string value)
            {
                var result = default(FixedString64Bytes);

                // Truncating rather than throwing: an over-long display name is a
                // cosmetic problem, and a baker that throws over one takes the
                // whole SubScene down with it.
                result.CopyFromTruncated(value ?? string.Empty);
                return result;
            }
        }
    }
}
