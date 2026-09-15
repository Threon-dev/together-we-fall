using UnityEditor;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Ten item sets, each built around a signature no other set has: a support
    /// that acts on every skill the wearer casts, and on the top step of five of
    /// them a keystone.
    ///
    /// Nothing here is a mechanism. A step carries what gear already carries —
    /// affixes, one support, one keystone — and only the highest step reached is
    /// in force, so a top step repeats the signature it upgrades.
    ///
    /// Assets are filled in only when created, like every factory here.
    /// EVERY NUMBER IS PROVISIONAL (TUNE).
    /// </summary>
    public static class SetContentFactory
    {
        private const string SetFolder = "Assets/_Project/Data/Sets";
        private const string PieceFolder = "Assets/_Project/Data/Items/Sets";

        /// <summary>Creates every set and its pieces, and puts the pieces where chests can roll them.</summary>
        public static void CreateSets(LootTable table)
        {
            // Chain everything, then turn bursts into chains.
            Set("ThunderlordsVestments", "Thunderlord's Vestments", ItemRarity.Epic, table,
                Flat(StatKind.LightningResistance, 10f),
                new[]
                {
                    Piece("Thunderlord Crown", EquipmentSlot.Helmet),
                    Piece("Thunderlord Hauberk", EquipmentSlot.Chest),
                    Piece("Thunderlord Grips", EquipmentSlot.Gloves),
                    Piece("Thunderlord Treads", EquipmentSlot.Boots),
                    Piece("Thunderlord Sash", EquipmentSlot.Belt),
                    Piece("Thunderlord Torc", EquipmentSlot.Amulet)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.CritChance, 5f), Flat(StatKind.LightningResistance, 20f)),
                Step(4, Bonus("ThunderlordChains", SkillModifierKind.AddedChains, 2f), KeystoneEffect.None,
                    Inc(StatKind.Damage, 10f)),
                Step(6, Bonus("ThunderlordStorm", SkillModifierKind.AddedChains, 4f), KeystoneEffect.AoeToChain,
                    Inc(StatKind.Damage, 20f), Flat(StatKind.CritChance, 5f)));

            // Every cast goes off again; at five pieces, twice more, free and slow.
            Set("ResonantRegalia", "Resonant Regalia", ItemRarity.Epic, table,
                Inc(StatKind.AttackSpeed, 4f),
                new[]
                {
                    Piece("Resonant Visor", EquipmentSlot.Helmet),
                    Piece("Resonant Cuirass", EquipmentSlot.Chest),
                    Piece("Resonant Gauntlets", EquipmentSlot.Gloves),
                    Piece("Resonant Greaves", EquipmentSlot.Boots),
                    Piece("Resonant Girdle", EquipmentSlot.Belt)
                },
                Step(2, null, KeystoneEffect.None, Inc(StatKind.AttackSpeed, 12f)),
                Step(4, Bonus("ResonantEcho", SkillModifierKind.Echo, 1f), KeystoneEffect.None,
                    Flat(StatKind.MaxMana, 20f)),
                Step(5, Bonus("ResonantChorus", SkillModifierKind.Echo, 2f), KeystoneEffect.NoManaCostDoubleCooldown,
                    Inc(StatKind.AttackSpeed, 15f)));

            // Every blow carries fire, so anything cold, shocked or bleeding reacts.
            Set("PyrelordsMantle", "Pyrelord's Mantle", ItemRarity.Epic, table,
                Flat(StatKind.FireResistance, 10f),
                new[]
                {
                    Piece("Pyrelord Cowl", EquipmentSlot.Helmet),
                    Piece("Pyrelord Robe", EquipmentSlot.Chest),
                    Piece("Pyrelord Handwraps", EquipmentSlot.Gloves),
                    Piece("Pyrelord Sandals", EquipmentSlot.Boots)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.FireResistance, 25f), Inc(StatKind.Damage, 10f)),
                Step(4, SkillContentFactory.Modifier("SupportSetPyrelordInfusion",
                        SkillModifierKind.InfuseElement, 0f, convertTo: Combat.DamageType.Fire),
                    KeystoneEffect.None, Inc(StatKind.Damage, 15f)));

            // Everything slows; at five pieces everything slowed is also vulnerable.
            SkillModifier frostbound = SkillContentFactory.Modifier("SupportSetWinterwardenSlow",
                SkillModifierKind.StatusOverride, 0f, appliedStatus: Combat.StatusEffectType.Slow);

            Set("WinterwardensAegis", "Winterwarden's Aegis", ItemRarity.Epic, table,
                Flat(StatKind.ColdResistance, 10f),
                new[]
                {
                    Piece("Winterwarden Helm", EquipmentSlot.Helmet),
                    Piece("Winterwarden Plate", EquipmentSlot.Chest),
                    Piece("Winterwarden Sabatons", EquipmentSlot.Boots),
                    Piece("Winterwarden Belt", EquipmentSlot.Belt),
                    Piece("Winterwarden Pendant", EquipmentSlot.Amulet)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.ColdResistance, 25f), Inc(StatKind.Armour, 25f)),
                Step(4, frostbound, KeystoneEffect.None, Flat(StatKind.MaxHealth, 40f)),
                Step(5, frostbound, KeystoneEffect.SlowsAlsoWeaken,
                    Flat(StatKind.MaxHealth, 40f), Inc(StatKind.Armour, 25f)));

            // Every projectile bursts where it lands.
            Set("BombardiersHarness", "Bombardier's Harness", ItemRarity.Epic, table,
                Inc(StatKind.Damage, 5f),
                new[]
                {
                    Piece("Bombardier Goggles", EquipmentSlot.Helmet),
                    Piece("Bombardier Vest", EquipmentSlot.Chest),
                    Piece("Bombardier Mitts", EquipmentSlot.Gloves),
                    Piece("Bombardier Bandolier", EquipmentSlot.Belt)
                },
                Step(2, null, KeystoneEffect.None,
                    Inc(StatKind.AttackSpeed, 8f), Inc(StatKind.MoveSpeed, 8f)),
                Step(4, Bonus("BombardierBurst", SkillModifierKind.ImpactBurst, 2.5f), KeystoneEffect.None,
                    Inc(StatKind.Damage, 10f)));

            // Every blow finishes what is nearly dead.
            Set("DeathmongersVestments", "Deathmonger's Vestments", ItemRarity.Epic, table,
                Flat(StatKind.MaxMana, 10f),
                new[]
                {
                    Piece("Deathmonger Hood", EquipmentSlot.Helmet),
                    Piece("Deathmonger Shroud", EquipmentSlot.Chest),
                    Piece("Deathmonger Boots", EquipmentSlot.Boots),
                    Piece("Deathmonger Cord", EquipmentSlot.Belt)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.MaxMana, 30f), Flat(StatKind.ManaRegen, 3f)),
                Step(4, Bonus("DeathmongerCull", SkillModifierKind.CullingStrike, 18f), KeystoneEffect.None,
                    Flat(StatKind.ManaRegen, 4f)));

            // Every pattern lays down more of itself.
            Set("LegionsWarplate", "Legion's Warplate", ItemRarity.Epic, table,
                Inc(StatKind.Armour, 8f),
                new[]
                {
                    Piece("Legion Breastplate", EquipmentSlot.Chest),
                    Piece("Legion Gauntlets", EquipmentSlot.Gloves),
                    Piece("Legion Warboots", EquipmentSlot.Boots),
                    Piece("Legion Standard", EquipmentSlot.Amulet)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.MaxHealth, 40f), Flat(StatKind.Armour, 20f)),
                Step(4, Bonus("LegionCount", SkillModifierKind.AddedCount, 4f), KeystoneEffect.None,
                    Inc(StatKind.Damage, 10f)));

            // Critical blows hit twice as hard.
            Set("NightbladeSilks", "Nightblade Silks", ItemRarity.Epic, table,
                Flat(StatKind.CritChance, 2f),
                new[]
                {
                    Piece("Nightblade Mask", EquipmentSlot.Helmet),
                    Piece("Nightblade Gloves", EquipmentSlot.Gloves),
                    Piece("Nightblade Slippers", EquipmentSlot.Boots),
                    Piece("Nightblade Wrap", EquipmentSlot.Belt)
                },
                Step(2, null, KeystoneEffect.None,
                    Flat(StatKind.CritChance, 8f), Inc(StatKind.MoveSpeed, 10f)),
                Step(4, Bonus("NightbladeCrits", SkillModifierKind.IncreasedCritMultiplier, 100f), KeystoneEffect.None,
                    Flat(StatKind.CritChance, 7f)));

            // Everything poisons; at five pieces every poison lands all at once.
            SkillModifier venom = SkillContentFactory.Modifier("SupportSetVenomweaverPoison",
                SkillModifierKind.StatusOverride, 0f, appliedStatus: Combat.StatusEffectType.Poison);

            Set("VenomweaversGarb", "Venomweaver's Garb", ItemRarity.Epic, table,
                Inc(StatKind.MaxHealth, 5f),
                new[]
                {
                    Piece("Venomweaver Veil", EquipmentSlot.Helmet),
                    Piece("Venomweaver Robe", EquipmentSlot.Chest),
                    Piece("Venomweaver Gloves", EquipmentSlot.Gloves),
                    Piece("Venomweaver Boots", EquipmentSlot.Boots),
                    Piece("Venomweaver Fang", EquipmentSlot.Amulet)
                },
                Step(2, null, KeystoneEffect.None,
                    Inc(StatKind.MaxHealth, 20f), Inc(StatKind.Damage, 8f)),
                Step(4, venom, KeystoneEffect.None, Inc(StatKind.Damage, 12f)),
                Step(5, venom, KeystoneEffect.StatusInstantResolve, Inc(StatKind.Damage, 20f)));

            // Every key stores a second press.
            Set("TwinMoons", "Twin Moons", ItemRarity.Legendary, table,
                Flat(StatKind.ManaRegen, 1f),
                new[]
                {
                    Piece("Twin Moons Locket", EquipmentSlot.Amulet),
                    Piece("Sunstone Band", EquipmentSlot.Ring1),
                    Piece("Moonstone Band", EquipmentSlot.Ring1)
                },
                Step(2, null, KeystoneEffect.None,
                    Inc(StatKind.MaxMana, 25f), Flat(StatKind.ManaRegen, 2f)),
                Step(3, Bonus("TwinMoonsCharges", SkillModifierKind.AddedCharges, 1f), KeystoneEffect.None,
                    Flat(StatKind.CritChance, 5f)));
        }

        /// <summary>
        /// Adds every piece of a set to the starting kit as worn, from the set's
        /// own inspector. Additive: pieces already listed are left alone, and a
        /// slot something else in the kit already wears sends the piece to the bag.
        /// </summary>
        [MenuItem("CONTEXT/ItemSetDefinition/Wear In Starter Kit")]
        private static void WearInStarterKit(MenuCommand command)
        {
            var set = (ItemSetDefinition)command.context;
            var kit = SceneBuildUtility.CreateOrLoadConfig<StarterKitConfig>("StarterKitConfig");

            var serialized = new SerializedObject(kit);
            SerializedProperty entries = serialized.FindProperty("_entries");
            int added = 0;

            foreach (ItemDefinition member in set.Members)
            {
                if (member == null || ArsenalContentFactory.Lists(entries, member))
                    continue;

                SceneBuildUtility.AppendKitEntry(entries, member, worn: true, count: 1);
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log($"[{nameof(SetContentFactory)}] {set.SetName}: {added} piece(s) added to {kit.name} as worn.");
        }

        // ─────────────────────────────────────────────────────────────────

        private readonly struct PieceSpec
        {
            public readonly string Name;
            public readonly EquipmentSlot Slot;

            public PieceSpec(string name, EquipmentSlot slot)
            {
                Name = name;
                Slot = slot;
            }
        }

        private readonly struct StepSpec
        {
            public readonly int Pieces;
            public readonly SkillModifier Support;
            public readonly KeystoneEffect Keystone;
            public readonly ItemAffix[] Stats;

            public StepSpec(int pieces, SkillModifier support, KeystoneEffect keystone, ItemAffix[] stats)
            {
                Pieces = pieces;
                Support = support;
                Keystone = keystone;
                Stats = stats;
            }
        }

        private static PieceSpec Piece(string name, EquipmentSlot slot) => new PieceSpec(name, slot);

        private static StepSpec Step(int pieces, SkillModifier support, KeystoneEffect keystone, params ItemAffix[] stats)
            => new StepSpec(pieces, support, keystone, stats);

        private static SkillModifier Bonus(string name, SkillModifierKind kind, float value)
            => SkillContentFactory.Modifier("SupportSet" + name, kind, value);

        private static ItemAffix Flat(StatKind stat, float value) => new ItemAffix(stat, ModifierKind.Flat, value);
        private static ItemAffix Inc(StatKind stat, float value) => new ItemAffix(stat, ModifierKind.Increased, value);

        private static void Set(
            string assetName,
            string displayName,
            ItemRarity rarity,
            LootTable table,
            ItemAffix pieceAffix,
            PieceSpec[] pieces,
            params StepSpec[] steps)
        {
            var members = new ItemDefinition[pieces.Length];

            for (int i = 0; i < pieces.Length; i++)
            {
                members[i] = CreatePiece(pieces[i], rarity, pieceAffix, $"{PieceFolder}/{assetName}");
                SceneBuildUtility.EnsureInLootTable(table, members[i]);
            }

            ItemSetDefinition set = SceneBuildUtility.CreateOrLoadConfig<ItemSetDefinition>(
                assetName, SetFolder, out bool created);

            if (!created)
                return;

            var serialized = new SerializedObject(set);
            serialized.FindProperty("_setId").stringValue = assetName;
            serialized.FindProperty("_setName").stringValue = displayName;

            SerializedProperty memberList = serialized.FindProperty("_members");
            memberList.arraySize = members.Length;

            for (int i = 0; i < members.Length; i++)
                memberList.GetArrayElementAtIndex(i).objectReferenceValue = members[i];

            SerializedProperty thresholds = serialized.FindProperty("_thresholds");
            thresholds.arraySize = steps.Length;

            for (int i = 0; i < steps.Length; i++)
            {
                SerializedProperty step = thresholds.GetArrayElementAtIndex(i);
                step.FindPropertyRelative("_requiredPieceCount").intValue = steps[i].Pieces;
                step.FindPropertyRelative("_bonusSkillModifier").objectReferenceValue = steps[i].Support;
                step.FindPropertyRelative("_bonusKeystone").enumValueIndex = (int)steps[i].Keystone;
                ItemContentFactory.WriteAffixes(step.FindPropertyRelative("_bonuses"), steps[i].Stats);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// One piece: base numbers and footprint by slot, and the set's own affix.
        /// A piece is ordinary gear — the set bonus is what makes it worth wearing.
        /// </summary>
        private static ItemDefinition CreatePiece(PieceSpec piece, ItemRarity rarity, ItemAffix affix, string folder)
        {
            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                piece.Name.Replace(" ", string.Empty), folder, out bool created);

            if (!created)
                return item;

            // TUNE
            (int width, int height, bool rotate, ItemStatValue[] stats) shape = piece.Slot switch
            {
                EquipmentSlot.Helmet => (2, 2, false, new[] { Base(StatKind.Armour, 12f), Base(StatKind.MaxHealth, 10f) }),
                EquipmentSlot.Chest => (2, 3, false, new[] { Base(StatKind.Armour, 30f), Base(StatKind.MaxHealth, 25f) }),
                EquipmentSlot.Gloves => (2, 2, false, new[] { Base(StatKind.Armour, 8f), Base(StatKind.Damage, 3f) }),
                EquipmentSlot.Boots => (2, 2, false, new[] { Base(StatKind.Armour, 10f) }),
                EquipmentSlot.Belt => (2, 1, true, new[] { Base(StatKind.MaxHealth, 15f) }),
                EquipmentSlot.Amulet => (1, 1, false, new[] { Base(StatKind.MaxHealth, 10f), Base(StatKind.Damage, 3f) }),
                _ => (1, 1, false, new[] { Base(StatKind.Damage, 3f) })
            };

            var serialized = new SerializedObject(item);
            serialized.FindProperty("_displayName").stringValue = piece.Name;
            serialized.FindProperty("_rarity").enumValueIndex = (int)rarity;
            serialized.FindProperty("_slot").enumValueIndex = (int)piece.Slot;
            serialized.FindProperty("_gridWidth").intValue = shape.width;
            serialized.FindProperty("_gridHeight").intValue = shape.height;
            serialized.FindProperty("_canRotate").boolValue = shape.rotate;

            ItemContentFactory.WriteStats(serialized.FindProperty("_baseStats"), shape.stats);
            ItemContentFactory.WriteAffixes(serialized.FindProperty("_affixes"), new[] { affix });

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static ItemStatValue Base(StatKind stat, float value) => new ItemStatValue(stat, value);
    }
}
