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
    /// about the target, and the two-handed weapon that rolls two skills of
    /// its own with six holes behind each to put them in.
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
        /// How many holes one skill gets: itself and six to change it with.
        ///
        /// The same number as GemSockets.MaxSupportsPerGroup plus the skill, and
        /// deliberately not read from it — that constant is the ceiling the cast
        /// fold will honour, this is what an author chose to cut. Equal today,
        /// and a weapon with fewer holes is ordinary content rather than a bug.
        /// </summary>
        private const int SocketsPerSkill = 7;

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
                    skill.ManaCost = 6f;   // TUNE
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
                    skill.ManaCost = 5f;   // TUNE
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
                    skill.ManaCost = 7f;   // TUNE
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
                    skill.ManaCost = 8f;   // TUNE
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
                    skill.ManaCost = 5f;   // TUNE
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
                    skill.ManaCost = 7f;   // TUNE
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
                    skill.ManaCost = 14f;   // TUNE

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
                    skill.ManaCost = 25f;   // TUNE
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
                    skill.ManaCost = 3f;   // TUNE
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
            CreateLinkedStaff(skills, table);
            CreateTestbedPair(table);
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
                SkillContentFactory.Modifier("SupportChain", SkillModifierKind.AddedChains, 2f),   // TUNE
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemFork", "Fork Support",
                SkillContentFactory.Modifier("SupportFork", SkillModifierKind.Fork, 1f),
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemMulticast", "Multicast Support",
                SkillContentFactory.Modifier("SupportMulticast", SkillModifierKind.Multicast, 1f), // TUNE
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
        /// The two-handed weapon, as the model now says a two-handed weapon is:
        /// two skills it rolled for itself, and six holes to change each of
        /// them with.
        ///
        /// Fourteen sockets in two groups of seven. The head of a group holds a
        /// skill and the six behind it are supports linked to it, so the two
        /// halves never touch: a Chain in the left seven changes the left skill
        /// and is invisible to the right. That is not a rule anybody wrote for
        /// this weapon — it is what a link group has always meant, and the whole
        /// of "a two-handed weapon has two skills" is that the layout names two
        /// groups instead of one.
        ///
        /// What it rolls from is authored here as a list, and the list IS the
        /// "suitable for this weapon" rule for now: everything cast at range and
        /// nothing swung. A kind on the skill and a kind on the weapon would be
        /// the real version, and both are concepts the project does not have —
        /// a list of seven says the same thing today and is deleted the day it
        /// does not.
        /// </summary>
        private static ItemDefinition CreateLinkedStaff(
            SkillDefinition[] skills, LootTable table)
        {
            ItemDefinition staff = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                "RiftwoodStaff", ItemFolder, out bool created);

            var serialized = new SerializedObject(staff);

            if (created)
            {
                serialized.FindProperty("_displayName").stringValue = "Riftwood Staff";
                serialized.FindProperty("_rarity").enumValueIndex = (int)ItemRarity.Epic;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 4;
                serialized.FindProperty("_canRotate").boolValue = true;

                // TUNE. A staff is worth carrying for its holes rather than its
                // numbers, so the damage is ordinary on purpose.
                WriteStat(serialized.FindProperty("_baseStats"), StatKind.Damage, 18f);
            }

            // Written every rebuild, unlike the numbers above.
            //
            // Both hands, the layout and the skill pool are STRUCTURE rather
            // than balance — they are what makes this item the shape it is, and
            // the same class of thing as the reference lists the reaction table
            // rewrites for exactly this reason. A staff authored before it held
            // two skills comes back with six holes and one group, and the
            // symptom is not a missing field: it is a weapon that quietly has
            // one skill in a game where two-handed means two.
            serialized.FindProperty("_isTwoHanded").boolValue = true;
            serialized.FindProperty("_socketCount").intValue = SocketsPerSkill * 2;

            // Two groups of seven: the head of each holds a skill the staff
            // rolled, and the six behind it are what changes that skill. The
            // whole of the two-handed rule is in this array — nothing else in
            // the pipeline had to learn that a weapon can have two skills.
            SerializedProperty groups = serialized.FindProperty("_linkGroups");
            groups.arraySize = SocketsPerSkill * 2;

            for (int i = 0; i < groups.arraySize; i++)
                groups.GetArrayElementAtIndex(i).intValue = i < SocketsPerSkill ? 0 : 1;

            // What a staff can come with: everything cast at range, and nothing
            // swung. This IS the "suitable for this weapon" rule for now — an
            // authored list per item rather than a kind on the skill, because a
            // weapon kind is a concept the project does not have yet and a list
            // is the smallest thing that means the same today.
            var pool = new[]
            {
                Named(skills, "Firebolt"),
                Named(skills, "Frost Lance"),
                Named(skills, "Arc"),
                Named(skills, "Toxic Spit"),
                Named(skills, "Frost Nova"),
                Named(skills, "Poison Cloud"),
                Named(skills, "Spark")
            };

            SerializedProperty innate = serialized.FindProperty("_innateSkills");
            innate.arraySize = pool.Length;

            for (int i = 0; i < pool.Length; i++)
                innate.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            SceneBuildUtility.EnsureInLootTable(table, staff);
            return staff;
        }

        /// <summary>
        /// The pair the emergence test is run on: holes with nothing already in
        /// them.
        ///
        /// Every weapon in the game rolls its own skills and welds one into the
        /// head of each link group, which is the model working as intended and
        /// also the reason a named combination cannot be assembled by hand —
        /// there is no free head to put a chosen active gem in. A weapon with an
        /// EMPTY skill pool gets no weld at all (ActiveSkillCount is zero, so
        /// Weld returns immediately), and every socket including the head is the
        /// player's.
        ///
        /// Two items rather than one, because the question is partly whether a
        /// link group ends at the item: the wand's four holes are one group, the
        /// focus's two are another, and a support in one must be invisible to
        /// the other. One hand each, so the bar binds them without a drag —
        /// right hand speaks LMB, left speaks RMB.
        ///
        /// A testbed rather than content: no stats, so nothing it does can be
        /// blamed on its numbers, and it is in the loot table only so the
        /// dungeon can produce one too.
        /// </summary>
        private static void CreateTestbedPair(LootTable table)
        {
            Testbed("TestbedWand", "Testbed Wand", EquipmentSlot.MainHand,
                new[] { 0, 0, 0, 0 }, table);

            Testbed("TestbedFocus", "Testbed Focus", EquipmentSlot.OffHand,
                new[] { 0, 0 }, table);
        }

        private static void Testbed(
            string assetName, string displayName, EquipmentSlot slot,
            int[] groups, LootTable table)
        {
            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                assetName, ItemFolder, out bool created);

            var serialized = new SerializedObject(item);

            if (created)
            {
                serialized.FindProperty("_displayName").stringValue = displayName;
                serialized.FindProperty("_rarity").enumValueIndex = (int)ItemRarity.Epic;
                serialized.FindProperty("_slot").enumValueIndex = (int)slot;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 2;
            }

            // Written every rebuild, for the same reason the staff's layout is:
            // the holes and the empty pool ARE what this item is, and a testbed
            // that came back from an older run with a welded skill in socket
            // zero would be a testbed that cannot hold the gem being tested.
            serialized.FindProperty("_isTwoHanded").boolValue = false;
            serialized.FindProperty("_socketCount").intValue = groups.Length;
            serialized.FindProperty("_innateSkills").arraySize = 0;

            SerializedProperty links = serialized.FindProperty("_linkGroups");
            links.arraySize = groups.Length;

            for (int i = 0; i < groups.Length; i++)
                links.GetArrayElementAtIndex(i).intValue = groups[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            SceneBuildUtility.EnsureInLootTable(table, item);
        }

        /// <summary>
        /// One skill out of the library by display name.
        ///
        /// By name rather than by index, because the pool below is a content
        /// decision and an index into CreateSkills is a thing that silently
        /// means something else the day a skill is inserted in the middle.
        /// </summary>
        private static SkillDefinition Named(SkillDefinition[] skills, string displayName)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && skills[i].DisplayName == displayName)
                    return skills[i];
            }

            return null;
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
            // into, and then every support gem in the game.
            //
            // All of them rather than a chosen few: the staff has twelve support
            // holes now, and a kit that hands out four of them is a kit that
            // decides the build. What is being tested is which combinations are
            // worth making, and that question needs the whole set on the table.
            //
            // No active gems. A weapon rolls the skills it comes with, so what
            // an active gem is for is a question this change has reopened — the
            // ones a previous run of this menu added are left where they are
            // rather than taken back, because this menu only ever adds.
            var wanted = new[]
            {
                "RiftwoodStaff",

                // The pair with free head sockets, and the active gems the
                // written test build needs in them. Everything else in this kit
                // is a support; these are the only way to choose which skill is
                // being supported, because every other weapon rolls that for
                // itself.
                "TestbedWand", "TestbedFocus", "GemSpark", "GemCinderWall",

                // Shape: how much exists and how big it is.
                "GemChain", "GemFork", "GemMulticast", "GemGreaterArea", "GemBrutality",

                // Element.
                "GemHoarfrost",

                // Conditional: the same gem twice over, depending on the target.
                "GemDevour", "GemExecute", "GemKindledChain", "GemRivenChain",

                // Trigger: two of these three have no event to fire on yet.
                "GemCastOnKill", "GemCastOnCrit", "GemCastOnLowHealth"
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
