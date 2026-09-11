using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Skills;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The wider library of skills, supports and gems — content rather than
    /// starter balance.
    ///
    /// Its own file beside SkillContentFactory rather than inside it, because
    /// the two answer different questions. That one authors the smallest set
    /// that makes every mechanism visible by holding a button; this one authors
    /// ENOUGH of each mechanism that combinations nobody wrote down start
    /// existing — ten actives across five elements, supports that ask questions
    /// about the target, and a weapon with a four-link to put them in.
    ///
    /// Nothing here needed a line of system code, and that is the claim being
    /// tested. A poison cloud that stacks, a spit that leaves a pool where it
    /// lands, a lance that slows and a nova that catches a crowd are all the
    /// same handful of fields on a ScriptableObject.
    ///
    /// EVERY NUMBER IN THIS FILE IS PROVISIONAL — each block is marked TUNE.
    /// They were chosen so the mechanisms are visible in play within seconds of
    /// pressing a key, which is not the same thing as balance and is not trying
    /// to be: nothing in this prototype is balanced against anything else yet.
    ///
    /// Assets are filled in only when created, like everywhere else in these
    /// factories. Rebuilding a scene must never overwrite numbers somebody has
    /// since tuned by hand.
    /// </summary>
    public static class BuildLibraryFactory
    {
        private const string ItemFolder = "Assets/_Project/Data/Items";

        /// <summary>
        /// Every skill in the library, for the skill database.
        ///
        /// Returned as an array the caller appends to the starter skills,
        /// because the database IS the list of skills that exist: a skill asset
        /// that never reaches it resolves to nothing, and the symptom is a gem
        /// that sits in a socket and casts silence.
        ///
        /// Toxic Pool is in here and has no gem of its own on purpose. It is
        /// cast by the spit that lands on top of it and by nothing else, which
        /// is the shape Storm Lance already had: a skill can exist to be
        /// triggered.
        /// </summary>
        public static SkillDefinition[] CreateSkills()
        {
            // The pool has to exist before the spit that triggers it, for the
            // ordinary reason: the support holds a reference to it.
            SkillDefinition pool =
                SkillContentFactory.Skill("ToxicPool", "Toxic Pool", skill =>
                {
                    skill.Effect = SkillEffectKind.PersistentZone;
                    skill.DamageType = DamageType.Chaos;

                    // TUNE. Per pulse rather than per cast, and the interval is
                    // the interesting number: poison stacks its duration rather
                    // than refreshing it, so a pulse every second and a half
                    // turns standing in the pool into a mark that grows instead
                    // of one that merely persists.
                    skill.BaseDamage = 6f;
                    skill.Cooldown = 3f;
                    skill.Range = 16f;
                    skill.Radius = 2f;
                    skill.ZoneDuration = 4f;
                    skill.ZoneTickInterval = 1.5f;
                });

            // The support that makes the spit leave the pool behind. Its value
            // is a percentage of the POOL's damage, not the spit's.
            SkillModifier spitPool = SkillContentFactory.Modifier(
                "SupportToxicPoolOnHit", SkillModifierKind.TriggerOnHit, 80f,   // TUNE
                triggered: pool);

            SkillDefinition firebolt =
                SkillContentFactory.Skill("Firebolt", "Firebolt", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Fire;

                    // TUNE
                    skill.BaseDamage = 15f;
                    skill.Cooldown = 0.5f;
                    skill.Range = 20f;
                    skill.ProjectileSpeed = 26f;

                    // No AppliedStatus. Fire already marks what it hits with
                    // Ignite, because that is what the element does — naming the
                    // burn here as well would be the same status applied twice
                    // in one blow and a second place to change it.
                });

            SkillDefinition lance =
                SkillContentFactory.Skill("FrostLance", "Frost Lance", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Cold;

                    // TUNE. Fast and flat: a lance reads as a lance because it
                    // arrives before the enemy has moved.
                    skill.BaseDamage = 10f;
                    skill.Cooldown = 0.4f;
                    skill.Range = 22f;
                    skill.ProjectileSpeed = 34f;

                    // The nearest thing this pipeline has to piercing, and
                    // honestly not the same thing: a projectile stops at what it
                    // touches, so this is a small blast at the impact instead —
                    // several bodies hit, but at one point rather than along a
                    // line. Piercing is a projectile field that does not exist.
                    skill.Radius = 1.2f;

                    // Chill comes from the element; the slow is the skill's own,
                    // and it is what makes the lance worth firing into a crowd
                    // rather than at one body.
                    skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
                });

            SkillDefinition arc =
                SkillContentFactory.Skill("Arc", "Arc", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Lightning;

                    // TUNE. Weak per hit and chains twice on its own, so it is
                    // the skill that shows what chaining is worth before anybody
                    // owns the support.
                    skill.BaseDamage = 8f;
                    skill.Cooldown = 0.6f;
                    skill.Range = 20f;
                    skill.ProjectileSpeed = 28f;
                    skill.BaseChains = 2;
                    skill.ChainRange = 7f;
                    skill.ChainDelay = 0.07f;
                });

            SkillDefinition spit =
                SkillContentFactory.Skill("ToxicSpit", "Toxic Spit", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Chaos;

                    // TUNE. Deliberately slow: the pool is the point, and a shot
                    // that arrives instantly gives the player no reason to think
                    // about where the pool will end up.
                    skill.BaseDamage = 12f;
                    skill.Cooldown = 0.9f;
                    skill.Range = 18f;
                    skill.ProjectileSpeed = 14f;

                    skill.Modifiers = new[] { spitPool };
                });

            SkillDefinition cleave =
                SkillContentFactory.Skill("Cleave", "Cleave", skill =>
                {
                    skill.Effect = SkillEffectKind.MeleeArc;
                    skill.DamageType = DamageType.Physical;

                    // TUNE
                    skill.BaseDamage = 20f;
                    skill.Cooldown = 0.8f;
                    skill.Range = 3f;
                    skill.Radius = 2.5f;
                    skill.ArcDegrees = 90f;

                    // No Fortify on the caster. A skill marks what it HITS, and
                    // the caster is not among them — a self-buff is a second
                    // target for a status to land on, which is a field on the
                    // skill and a branch in the hit stage rather than data.
                });

            SkillDefinition frostStrike =
                SkillContentFactory.Skill("FrostStrike", "Frost Strike", skill =>
                {
                    skill.Effect = SkillEffectKind.MeleeArc;
                    skill.DamageType = DamageType.Cold;

                    // TUNE
                    skill.BaseDamage = 18f;
                    skill.Cooldown = 0.7f;
                    skill.Range = 3f;
                    skill.Radius = 2.5f;
                    skill.ArcDegrees = 45f;

                    // Stuns outright rather than on a chance against an already
                    // chilled target: a skill applies one status and applies it
                    // always — there is no chance field and no condition on a
                    // skill, only on a support. What keeps this honest is
                    // diminishing returns, which is the case they were written
                    // for: a narrow, fast swing that stuns every time still
                    // cannot hold anything still, because the second stun inside
                    // the immunity window does nothing at all.
                    skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Stun);
                });

            SkillDefinition nova =
                SkillContentFactory.Skill("FrostNova", "Frost Nova", skill =>
                {
                    skill.Effect = SkillEffectKind.AreaBurst;
                    skill.DamageType = DamageType.Cold;

                    // TUNE
                    skill.BaseDamage = 12f;
                    skill.Cooldown = 1.2f;

                    // Range is how far the burst may be placed. A nova is meant
                    // to go off on top of the caster, so it is short rather than
                    // zero — zero is not a value the asset allows.
                    skill.Range = 2f;
                    skill.Radius = 3f;

                    // Slows everything caught. The inner ring that freezes is
                    // not here: a second radius with a different outcome is two
                    // more fields on the skill, and the chance to freeze is the
                    // same missing field as on Frost Strike.
                    skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
                });

            SkillDefinition cloud =
                SkillContentFactory.Skill("PoisonCloud", "Poison Cloud", skill =>
                {
                    skill.Effect = SkillEffectKind.PersistentZone;
                    skill.DamageType = DamageType.Chaos;

                    // TUNE. The cloud is the reason chaos now marks with poison:
                    // a pulse every second and a half against a status that
                    // stacks its duration is a mark that grows while somebody
                    // stands in it, which is the whole of what a cloud is.
                    skill.BaseDamage = 5f;
                    skill.Cooldown = 5f;
                    skill.Range = 16f;
                    skill.Radius = 3.5f;
                    skill.ZoneDuration = 6f;
                    skill.ZoneTickInterval = 1.5f;
                });

            SkillDefinition spark =
                SkillContentFactory.Skill("Spark", "Spark", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Lightning;

                    // TUNE. Almost nothing on its own, and fast enough that
                    // whatever is socketed beside it happens often. It is the
                    // control in the experiment: if supports are where builds
                    // come from, this is the skill that proves it.
                    skill.BaseDamage = 4f;
                    skill.Cooldown = 0.3f;
                    skill.Range = 18f;
                    skill.ProjectileSpeed = 30f;
                });

            return new[]
            {
                firebolt, lance, arc, spit, pool, cleave, frostStrike, nova, cloud, spark
            };
        }

        /// <summary>
        /// The starter skills and the library as one list, for the database.
        ///
        /// Here rather than in the scene builders because both of them need the
        /// same answer, and "which skills exist" written twice is the kind of
        /// list that ends up differing by one asset that nobody can cast.
        /// </summary>
        public static SkillDefinition[] WithLibrary(SkillDefinition[] starter)
        {
            SkillDefinition[] library = CreateSkills();
            var all = new SkillDefinition[starter.Length + library.Length];

            starter.CopyTo(all, 0);
            library.CopyTo(all, starter.Length);

            return all;
        }

        /// <summary>
        /// A gem for everything above, the supports that ask questions, and the
        /// weapon with enough holes to ask several at once.
        ///
        /// All of it goes into the chest table, because a gem that exists and
        /// cannot be found is a gem nobody will ever combine with anything.
        /// </summary>
        public static void CreateGems(LootTable table)
        {
            SkillDefinition[] skills = CreateSkills();

            ActiveGems(skills, table);
            SupportGems(table);
            CreateLinkedStaff(table);
        }

        /// <summary>
        /// One gem per castable skill, rarer as the skill gets louder.
        ///
        /// Toxic Pool is skipped: it is cast by the spit and by nothing else, so
        /// a gem for it would be a second way to get the same effect with none
        /// of the aiming that makes it interesting.
        /// </summary>
        private static void ActiveGems(SkillDefinition[] skills, LootTable table)
        {
            // Asset name, display name and rarity, in the order CreateSkills
            // returns them. Paired positionally on purpose — the alternative is
            // naming every skill twice, and a list that can disagree with
            // itself.
            var gems = new (string asset, string display, ItemRarity rarity)[]
            {
                ("GemFirebolt", "Firebolt Gem", ItemRarity.Common),
                ("GemFrostLance", "Frost Lance Gem", ItemRarity.Common),
                ("GemArc", "Arc Gem", ItemRarity.Uncommon),
                ("GemToxicSpit", "Toxic Spit Gem", ItemRarity.Uncommon),
                (null, null, ItemRarity.Common),                 // Toxic Pool: triggered only
                ("GemCleave", "Cleave Gem", ItemRarity.Common),
                ("GemFrostStrike", "Frost Strike Gem", ItemRarity.Rare),
                ("GemFrostNova", "Frost Nova Gem", ItemRarity.Rare),
                ("GemPoisonCloud", "Poison Cloud Gem", ItemRarity.Rare),
                ("GemSpark", "Spark Gem", ItemRarity.Common)
            };

            for (int i = 0; i < skills.Length && i < gems.Length; i++)
            {
                if (gems[i].asset == null)
                    continue;

                SkillContentFactory.ActiveGem(
                    gems[i].asset, gems[i].display, skills[i], gems[i].rarity, table);
            }
        }

        /// <summary>
        /// The supports that ask a question, plus the one that existed as a
        /// modifier and never as a gem.
        ///
        /// Chain is that one, and it is the loudest omission here: Storm Lance
        /// has had a chain support welded into it from the start, and there has
        /// been no way for a player to put chain on anything else. Fork and
        /// Multicast already had gems and are only made sure of, so that every
        /// support in the game is reachable from the same table.
        /// </summary>
        private static void SupportGems(LootTable table)
        {
            // Modifier() is create-or-load, so these are the same assets the
            // starter skills already socket rather than second copies of them.
            // Two authored copies of Chain would be two things to balance and
            // one to forget.
            SkillContentFactory.SupportGem(
                "GemChain", "Chain Support",
                SkillContentFactory.Modifier("SupportChain", SkillModifierKind.AddedChains, 3f),
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemFork", "Fork Support",
                SkillContentFactory.Modifier("SupportFork", SkillModifierKind.Fork, 1f),
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemMulticast", "Multicast Support",
                SkillContentFactory.Modifier("SupportMulticast", SkillModifierKind.Multicast, 2f),
                ItemRarity.Rare, table);

            // Conversion is unconditional: it changes what the supported skill
            // deals, whatever that was. "Fire to cold" specifically would need
            // the support to know what it converts FROM, which is a second field
            // and a rule about what happens when it does not match.
            SkillContentFactory.SupportGem(
                "GemHoarfrost", "Hoarfrost Support",
                SkillContentFactory.Modifier(
                    "SupportFrostConversion", SkillModifierKind.ElementalConversion, 0f,
                    convertTo: DamageType.Cold),
                ItemRarity.Uncommon, table);

            // The two conditional damage supports. Both are all or nothing on
            // purpose — a support that paid out a fraction when its condition
            // failed would be a support nobody has to arrange anything for.
            SkillContentFactory.SupportGem(
                "GemDevour", "Devour Support",
                SkillContentFactory.Modifier(
                    "SupportDevour", SkillModifierKind.IncreasedDamage, 100f,   // TUNE
                    condition: ModifierConditionType.TargetHasStatus,

                    // Fire rather than "any element", which the condition cannot
                    // express: it asks whether a particular element is on the
                    // target, and a wildcard would be a sixth condition value.
                    requiredElement: DamageType.Fire),
                ItemRarity.Rare, table);

            SkillContentFactory.SupportGem(
                "GemExecute", "Execute Support",
                SkillContentFactory.Modifier(
                    "SupportExecute", SkillModifierKind.IncreasedDamage, 150f,  // TUNE
                    condition: ModifierConditionType.TargetLowHealth,
                    threshold: 0.25f),                                          // TUNE
                ItemRarity.Rare, table);

            // The two trigger gems whose events nothing raises yet. Authored
            // anyway, and said plainly rather than left out: the socket holds
            // them, the panel names them, and the day the player can crit or be
            // hurt they start working with no content change. Until then they
            // are the honest demonstration that a trigger is a field rather than
            // a system — and Cast on Kill beside them fires today.
            SkillContentFactory.SupportGem(
                "GemCastOnCrit", "Cast on Crit Support",
                SkillContentFactory.Modifier(
                    "SupportCastOnCrit", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnCrit,
                    triggerCooldown: 0.8f,                                      // TUNE
                    procChance: 0.6f),                                          // TUNE
                ItemRarity.Rare, table);

            SkillContentFactory.SupportGem(
                "GemCastOnLowHealth", "Cast on Low Health Support",
                SkillContentFactory.Modifier(
                    "SupportCastOnLowHealth", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnLowHealth,
                    triggerCooldown: 8f,                                        // TUNE
                    procChance: 1f,                                             // TUNE
                    threshold: 0.3f),                                           // TUNE
                ItemRarity.Rare, table);
        }

        /// <summary>
        /// A weapon with a four-link, which is the piece the library actually
        /// needed.
        ///
        /// Every combination in the design notes wants an active and two or
        /// three supports in ONE group, and the only weapon in the game links
        /// its six holes as [0,0,1,1,2,3] — a pair, a pair and two singles. So
        /// none of the interesting builds could be assembled at all, whatever
        /// gems the player owned. That is a content gap rather than a code one,
        /// and this is the content.
        ///
        /// The layout is [1,0,0,0,0,2]: the welded attack alone in its own
        /// group, four linked holes for a build, and one lone hole for a second
        /// skill meant to stay unmodified — a wall of fire to shoot through,
        /// say. Keeping the innate attack out of the four-link matters: a
        /// trigger gem in that group would otherwise take the left mouse button
        /// away from the weapon's own attack as well.
        /// </summary>
        private static ItemDefinition CreateLinkedStaff(LootTable table)
        {
            ItemDefinition staff = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                "RiftwoodStaff", ItemFolder, out bool created);

            if (created)
            {
                SkillContentFactory.CreateDefaultAttacks(out _, out SkillDefinition bolt);

                var serialized = new SerializedObject(staff);
                serialized.FindProperty("_displayName").stringValue = "Riftwood Staff";
                serialized.FindProperty("_rarity").enumValueIndex = (int)ItemRarity.Epic;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_isTwoHanded").boolValue = true;
                serialized.FindProperty("_innateSkill").objectReferenceValue = bolt;
                serialized.FindProperty("_socketCount").intValue = 6;

                SerializedProperty groups = serialized.FindProperty("_linkGroups");
                var layout = new[] { 1, 0, 0, 0, 0, 2 };
                groups.arraySize = layout.Length;

                for (int i = 0; i < layout.Length; i++)
                    groups.GetArrayElementAtIndex(i).intValue = layout[i];

                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 4;
                serialized.FindProperty("_canRotate").boolValue = true;

                // TUNE. A staff is worth carrying for its holes rather than its
                // numbers, so the damage is ordinary on purpose.
                WriteStat(serialized.FindProperty("_baseStats"), StatKind.Damage, 18f);

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, staff);
            return staff;
        }

        private static void WriteStat(SerializedProperty array, StatKind stat, float value)
        {
            array.arraySize = 1;

            SerializedProperty element = array.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("_stat").enumValueIndex = (int)stat;
            element.FindPropertyRelative("_value").floatValue = value;
        }

        /// <summary>
        /// Puts the pieces of the test build into the starting kit.
        ///
        /// A menu item of its own rather than part of a scene build, because the
        /// starter kit is authored balance and a content library has no business
        /// rewriting it silently. Run it when a combination needs checking in the
        /// arena, where there are no chests to roll one out of.
        ///
        /// Additive: items already listed are left alone and nothing is removed,
        /// so running it twice is the same as running it once.
        /// </summary>
        [MenuItem("Tools/Together We Fall/Grant Library Test Kit")]
        public static void GrantTestKit()
        {
            LootTable table = ItemContentFactory.CreateOrLoadTreasureTable();
            CreateGems(table);

            var config = SceneBuildUtility.CreateOrLoadConfig<CharacterConfig>("CharacterConfig");

            // The staff first, because it is the thing everything else goes
            // into, then one gem per hole of the build being tested plus the
            // wall to shoot through.
            var wanted = new[]
            {
                "RiftwoodStaff",
                "GemSpark", "GemChain", "GemDevour", "GemCastOnKill", "GemCastOnCrit",
                "GemCinderWall"
            };

            var serialized = new SerializedObject(config);
            SerializedProperty items = serialized.FindProperty("_starterItems");
            int added = 0;

            for (int i = 0; i < wanted.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"{ItemFolder}/{wanted[i]}.asset");

                if (item == null || Contains(items, item))
                    continue;

                items.arraySize++;
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[BuildLibraryFactory] Test kit: {added} item(s) added to the starter kit. " +
                "Rebuild the scene so the skill database contains the library.");
        }

        private static bool Contains(SerializedProperty array, Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value)
                    return true;
            }

            return false;
        }
    }
}
