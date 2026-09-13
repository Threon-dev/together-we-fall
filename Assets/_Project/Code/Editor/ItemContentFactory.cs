using UnityEditor;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Creates the starter items and the table chests roll on.
    ///
    /// Its own file for the same reason SkillContentFactory and
    /// ElementContentFactory are, plus one the others do not have: BOTH scenes
    /// need this now. The arena needs an item database because a skill lives in
    /// a gem and a gem is an item — so the set of items that exists cannot be
    /// something the dungeon builder happens to own privately, or the two scenes
    /// would be playing with different games.
    ///
    /// Assets are filled in only when created. Rebuilding a scene must never
    /// overwrite numbers somebody has since tuned; the point of these being
    /// assets is that they are edited by hand, and a generator that silently
    /// reverts that editing is worse than no generator.
    /// </summary>
    public static class ItemContentFactory
    {
        private const string ItemFolder = "Assets/_Project/Data/Items";
        private const string SetFolder = "Assets/_Project/Data/Sets";

        /// <summary>
        /// Gives every weapon the attack it is born with.
        ///
        /// Written only where the field is still empty, never over a choice
        /// somebody made — the same additive rule as adding a gem to a loot
        /// table, and for the same reason: these assets are edited by hand, and
        /// a generator that reverts that editing is worse than no generator.
        ///
        /// It has to name the assets rather than walk a folder, because "which
        /// weapons exist" is a content decision and a folder scan would quietly
        /// weld an attack into the next sword somebody drops in there.
        /// </summary>
        public static void AssignDefaultAttacks()
        {
            SkillContentFactory.CreateDefaultAttacks(
                out SkillDefinition strike, out SkillDefinition bolt);

            // Swords, daggers and anything else swung. The melee arc is short
            // and wide, so walking into a crowd is the whole of using it.
            Weld("CrackedDagger", strike);
            Weld("FrostbiteBlade", strike);
            Weld("Dawnbringer", strike);

            // Anything that shoots. A wand and a bow fire the same bolt today;
            // giving one of them its own asset is one field, not a feature.
            Weld("HuntersBow", bolt);
            Weld("ApprenticeWand", bolt);
        }

        private static void Weld(string assetName, SkillDefinition skill)
        {
            var item = SceneBuildUtility.LoadConfig<ItemDefinition>(assetName);

            // A weapon that is not on disk yet is not an error: the sample items
            // are created on the first build, and this runs beside them.
            if (item == null || skill == null)
                return;

            var serialized = new SerializedObject(item);
            SerializedProperty innate = serialized.FindProperty("_innateSkills");

            // A list now, because a weapon rolls what it comes with out of what
            // its kind can carry. An ordinary sword carries one thing, so its
            // list is one long and the roll has one outcome — which is the same
            // weapon it was before this existed.
            if (innate.arraySize > 0)
                return;

            innate.arraySize = 1;
            innate.GetArrayElementAtIndex(0).objectReferenceValue = skill;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The table treasure chests roll on, with a starter set of items.
        ///
        /// Filled in only when the asset is created. Rebuilding the scene must
        /// not overwrite a table somebody has since tuned — the point of these
        /// assets is that they are edited by hand, and a generator that silently
        /// reverts that editing is worse than no generator.
        ///
        /// The items themselves are placeholders with no stats, because stats
        /// belong to equipment and equipment is its own step. What they do carry
        /// is a rarity, which is the only property the drop pipeline reads.
        /// </summary>
        public static LootTable CreateOrLoadTreasureTable()
        {
            LootTable table = SceneBuildUtility.CreateOrLoadConfig<LootTable>(
                "TreasureLootTable", out bool created);

            if (!created)
                return table;

            ItemDefinition[] items = CreateSampleItems();

            var serialized = new SerializedObject(table);
            SerializedProperty entries = serialized.FindProperty("_entries");
            entries.arraySize = items.Length;

            for (int i = 0; i < items.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("_item").objectReferenceValue = items[i];
                entry.FindPropertyRelative("_weight").floatValue = 1f;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        /// <summary>
        /// One set, so the mechanism exists in content and not only in code.
        ///
        /// Four pieces that the sample items already provide, and two steps on
        /// purpose: the first is plain numbers, the second adds a support that
        /// applies to every skill the wearer casts. That is the whole shape of
        /// the feature — a step that is stats, a step that is more than stats —
        /// and a set with one step would have exercised half of it.
        ///
        /// The numbers are placeholders, like every other number in these
        /// factories. Filled only when the asset is created: rebuilding a scene
        /// must never overwrite a set somebody has since tuned.
        /// </summary>
        public static ItemSetDefinition CreateOrLoadDemoSet()
        {
            ItemSetDefinition set = SceneBuildUtility.CreateOrLoadConfig<ItemSetDefinition>(
                "WarlordsRegalia", SetFolder, out bool created);

            if (!created)
                return set;

            // The pieces, by asset name. Named rather than filtered by slot,
            // because "which items are in this set" is a content decision and a
            // scan would quietly change it the day an item is added.
            var members = new[]
            {
                LoadItem("WarlordsPlate"),
                LoadItem("RustedHelm"),
                LoadItem("RunedGauntlets"),
                LoadItem("BandOfEmbers")
            };

            var serialized = new SerializedObject(set);
            serialized.FindProperty("_setId").stringValue = "WarlordsRegalia";
            serialized.FindProperty("_setName").stringValue = "Warlord's Regalia";

            SerializedProperty memberList = serialized.FindProperty("_members");
            memberList.arraySize = 0;

            for (int i = 0; i < members.Length; i++)
            {
                // A missing piece is said out loud rather than written in as a
                // hole: this asset is filled once, so a null baked in now is a
                // set that is quietly one piece short forever.
                if (members[i] == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(ItemContentFactory)}] The demo set is missing a member — " +
                        "the sample items are not on disk yet. Rebuild the scene once they are.");
                    continue;
                }

                memberList.arraySize++;
                memberList.GetArrayElementAtIndex(memberList.arraySize - 1)
                    .objectReferenceValue = members[i];
            }

            SerializedProperty thresholds = serialized.FindProperty("_thresholds");
            thresholds.arraySize = 2;

            // Two pieces: numbers only. TUNE
            SerializedProperty twoPiece = thresholds.GetArrayElementAtIndex(0);
            twoPiece.FindPropertyRelative("_requiredPieceCount").intValue = 2;
            twoPiece.FindPropertyRelative("_bonusSkillModifier").objectReferenceValue = null;
            twoPiece.FindPropertyRelative("_bonusKeystone").enumValueIndex =
                (int)KeystoneEffect.None;

            WriteAffixes(
                twoPiece.FindPropertyRelative("_bonuses"),
                new[]
                {
                    new ItemAffix(StatKind.MaxHealth, ModifierKind.Flat, 25f),
                    new ItemAffix(StatKind.Armour, ModifierKind.Increased, 15f)
                });

            // Four pieces: numbers AND a support on everything cast. TUNE
            SerializedProperty fourPiece = thresholds.GetArrayElementAtIndex(1);
            fourPiece.FindPropertyRelative("_requiredPieceCount").intValue = 4;
            fourPiece.FindPropertyRelative("_bonusSkillModifier").objectReferenceValue =
                SceneBuildUtility.LoadConfig<SkillModifier>("SupportChain");
            fourPiece.FindPropertyRelative("_bonusKeystone").enumValueIndex =
                (int)KeystoneEffect.None;

            WriteAffixes(
                fourPiece.FindPropertyRelative("_bonuses"),
                new[]
                {
                    new ItemAffix(StatKind.Damage, ModifierKind.Increased, 20f)
                });

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        /// <summary>One sample item by asset name, or null when it is not there yet.</summary>
        private static ItemDefinition LoadItem(string assetName)
            => SceneBuildUtility.LoadConfig<ItemDefinition>(assetName);

        /// <summary>
        /// The starter items.
        ///
        /// Placeholders, but not empty ones: every slot is represented, both
        /// modifier kinds appear, and the numbers climb with rarity. That is
        /// what makes the character sheet visibly move when something is
        /// equipped, which is the only way to see the stat maths running.
        /// </summary>
        private static ItemDefinition[] CreateSampleItems()
        {
            // After the slot come the inventory footprint — width, height and
            // whether the player may turn it on its side — and whether the item
            // needs both hands. They differ on purpose: eight one-by-one items
            // that all go in the same slot would exercise nothing.
            //
            // The two rings are here for the same reason. A ring is the only
            // item that fits in more than one slot, so without one the ring
            // pairing and the slot-to-slot move have nothing to act on.
            var definitions =
                new (string name, ItemRarity rarity, EquipmentSlot slot,
                     int width, int height, bool canRotate, bool twoHanded,
                     ItemStatValue[] baseStats, ItemAffix[] affixes)[]
                {
                    ("Cracked Dagger", ItemRarity.Common, EquipmentSlot.MainHand,
                        1, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 8f) },
                        new[] { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 10f) }),

                    ("Dented Buckler", ItemRarity.Common, EquipmentSlot.OffHand,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Armour, 12f) },
                        new[] { new ItemAffix(StatKind.Armour, ModifierKind.Increased, 10f) }),

                    ("Rusted Helm", ItemRarity.Common, EquipmentSlot.Helmet,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Armour, 8f) },
                        new[] { new ItemAffix(StatKind.MaxHealth, ModifierKind.Flat, 10f) }),

                    ("Hunters Bow", ItemRarity.Uncommon, EquipmentSlot.MainHand,
                        2, 3, true, true,
                        new[] { new ItemStatValue(StatKind.Damage, 14f) },
                        new[] { new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 15f) }),

                    ("Runed Gauntlets", ItemRarity.Uncommon, EquipmentSlot.Gloves,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 3f) },
                        new[] { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 12f) }),

                    ("Band of Embers", ItemRarity.Uncommon, EquipmentSlot.Ring1,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 4f) },
                        new[] { new ItemAffix(StatKind.FireResistance, ModifierKind.Flat, 12f) }),

                    ("Coil of the Deep", ItemRarity.Rare, EquipmentSlot.Ring1,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.MaxHealth, 15f) },
                        new[] { new ItemAffix(StatKind.ColdResistance, ModifierKind.Flat, 18f) }),

                    ("Frostbite Blade", ItemRarity.Rare, EquipmentSlot.MainHand,
                        1, 3, true, false,
                        new[] { new ItemStatValue(StatKind.Damage, 22f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 20f),
                            new ItemAffix(StatKind.ColdResistance, ModifierKind.Flat, 15f)
                        }),

                    ("Warlords Plate", ItemRarity.Epic, EquipmentSlot.Chest,
                        2, 3, false, false,
                        new[]
                        {
                            new ItemStatValue(StatKind.Armour, 40f),
                            new ItemStatValue(StatKind.MaxHealth, 25f)
                        },
                        new[] { new ItemAffix(StatKind.MaxHealth, ModifierKind.Increased, 18f) }),

                    ("Dawnbringer", ItemRarity.Legendary, EquipmentSlot.MainHand,
                        1, 4, true, true,
                        new[] { new ItemStatValue(StatKind.Damage, 45f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 45f),
                            new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 25f)
                        }),

                    // Glass Cannon, and it needed no keystone: "double your
                    // damage, halve your life" is two affixes the stat fold
                    // already understands, and a KeystoneEffect for it would be
                    // an enum value with no branch behind it. Worth saying out
                    // loud because it is the boundary — a keystone is for rules
                    // no stat can express, and this one is only numbers.
                    //
                    // TUNE, and loudly: the downside is invisible today. The
                    // player has no health, so this is +100% damage for free
                    // until the Downed model lands.
                    ("Glass Cannon", ItemRarity.Legendary, EquipmentSlot.Ring1,
                        1, 1, false, false,
                        new ItemStatValue[0],
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 100f),
                            new ItemAffix(StatKind.MaxHealth, ModifierKind.Increased, -50f)
                        }),

                    ("Heart of the Fall", ItemRarity.Mythic, EquipmentSlot.Amulet,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.MaxHealth, 50f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 30f),
                            new ItemAffix(StatKind.MoveSpeed, ModifierKind.Increased, 10f)
                        })
                };

            var items = new ItemDefinition[definitions.Length];

            for (int i = 0; i < definitions.Length; i++)
            {
                string assetName = definitions[i].name.Replace(" ", string.Empty);

                // By slot only: the slot cannot tell a sword from a dagger, so a
                // regenerated weapon lands in Weapons/ and is filed by hand.
                string folder = definitions[i].slot switch
                {
                    EquipmentSlot.MainHand => "Weapons",
                    EquipmentSlot.Ring1 or EquipmentSlot.Ring2 or EquipmentSlot.Amulet => "Jewellery",
                    _ => "Armour"
                };

                ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                    assetName, $"{ItemFolder}/{folder}", out bool created);

                if (created)
                {
                    var serialized = new SerializedObject(item);
                    serialized.FindProperty("_displayName").stringValue = definitions[i].name;
                    serialized.FindProperty("_rarity").enumValueIndex = (int)definitions[i].rarity;
                    serialized.FindProperty("_slot").enumValueIndex = (int)definitions[i].slot;

                    serialized.FindProperty("_gridWidth").intValue = definitions[i].width;
                    serialized.FindProperty("_gridHeight").intValue = definitions[i].height;
                    serialized.FindProperty("_canRotate").boolValue = definitions[i].canRotate;
                    serialized.FindProperty("_isTwoHanded").boolValue = definitions[i].twoHanded;

                    WriteStats(serialized.FindProperty("_baseStats"), definitions[i].baseStats);
                    WriteAffixes(serialized.FindProperty("_affixes"), definitions[i].affixes);

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                items[i] = item;
            }

            return items;
        }

        private static void WriteStats(SerializedProperty array, ItemStatValue[] values)
        {
            array.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_stat").enumValueIndex = (int)values[i].Stat;
                element.FindPropertyRelative("_value").floatValue = values[i].Value;
            }
        }

        private static void WriteAffixes(SerializedProperty array, ItemAffix[] affixes)
        {
            array.arraySize = affixes.Length;

            for (int i = 0; i < affixes.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_stat").enumValueIndex = (int)affixes[i].Stat;
                element.FindPropertyRelative("_kind").enumValueIndex = (int)affixes[i].Kind;
                element.FindPropertyRelative("_value").floatValue = affixes[i].Value;
            }
        }
    }
}
