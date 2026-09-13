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
    /// Creates the starter skills and supports.
    ///
    /// Its own file because the set is the interesting part: four skills chosen
    /// so that each base effect and every support is visible in play. A
    /// projectile that forks and multicasts, a burst that makes corpses
    /// explode, a bolt that chains and changes element, and a swing that hits an
    /// arc — between them nothing in the pipeline is untested by simply holding
    /// a button.
    ///
    /// Assets are filled in only when created. Rebuilding the scene must not
    /// overwrite numbers somebody has since tuned; the point of these being
    /// assets is that they are edited by hand.
    /// </summary>
    public static class SkillContentFactory
    {
        private const string SkillFolder = "Assets/_Project/Data/Skills";
        private const string ItemFolder = "Assets/_Project/Data/Items";

        /// <summary>The skills, in loadout order: primary, secondary, and two more.</summary>
        public static SkillDefinition[] CreateStarterSkills()
        {
            SkillModifier fork = Modifier("SupportFork", SkillModifierKind.Fork, 1f);
            SkillModifier multicast = Modifier("SupportMulticast", SkillModifierKind.Multicast, 1f);  // TUNE
            SkillModifier area = Modifier("SupportGreaterArea", SkillModifierKind.IncreasedArea, 40f);
            SkillModifier chain = Modifier("SupportChain", SkillModifierKind.AddedChains, 2f);  // TUNE
            SkillModifier brutality =
                Modifier("SupportBrutality", SkillModifierKind.IncreasedDamage, 35f);

            SkillModifier explode = Modifier(
                "SupportExplodeOnKill", SkillModifierKind.ExplodeOnKill, 3.5f, secondary: 60f);

            SkillModifier lightning = Modifier(
                "SupportLightningConversion", SkillModifierKind.ElementalConversion, 0f,
                convertTo: DamageType.Lightning);

            // Storm Lance first, because the support that triggers it needs
            // something to point at, and the skill that sockets that support
            // needs the support. The returned order below is the loadout, not
            // this one.
            SkillDefinition lance =
                Skill("StormLance", "Storm Lance", skill =>
                {
                    skill.Effect = SkillEffectKind.ChainBolt;

                    // Authored physical and converted by a support, so the
                    // conversion is visible: the bolt comes out yellow.
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 28f;
                    skill.Cooldown = 0.9f;
                    skill.ManaCost = 10f;   // TUNE
                    skill.Range = 20f;
                    skill.BaseChains = 2;
                    skill.ChainRange = 9f;
                    skill.ChainDelay = 0.07f;
                    skill.Modifiers = new[] { chain, lightning };
                });

            // The support that composes two skills into one. Everything else in
            // this file tunes a skill; this one changes what the skill does when
            // it lands.
            SkillModifier triggerLance = Modifier(
                "SupportTriggerStormLance", SkillModifierKind.TriggerOnHit, 60f,
                triggered: lance);

            SkillDefinition splinter =
                Skill("SplinterBolt", "Splinter Bolt", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 16f;
                    skill.Cooldown = 0.3f;
                    skill.ManaCost = 4f;   // TUNE
                    skill.Range = 22f;
                    skill.ProjectileSpeed = 30f;

                    // Multicast and Fork decide how many projectiles exist;
                    // the trigger decides what each one does on landing. Three
                    // copies, one fork each, nine impacts, nine chain bolts —
                    // loud on purpose, and the first thing to pull back out if
                    // it is too much.
                    skill.Modifiers = new[] { fork, multicast, triggerLance };
                });

            SkillDefinition nova =
                Skill("CinderNova", "Cinder Nova", skill =>
                {
                    skill.Effect = SkillEffectKind.AreaBurst;
                    skill.DamageType = DamageType.Fire;
                    skill.BaseDamage = 42f;
                    skill.Cooldown = 1.5f;
                    skill.ManaCost = 18f;   // TUNE
                    skill.Range = 18f;
                    skill.Radius = 4f;
                    skill.Modifiers = new[] { area, explode };

                    // A crowd that is burning and slowed, from one button. It is
                    // what makes the Frozen Hourglass worth wearing and what
                    // Riven Chain is conditional on — three separate pieces of
                    // content that only meet in a build the player assembles.
                    skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Slow);
                });

            SkillDefinition sweep =
                Skill("ReavingSweep", "Reaving Sweep", skill =>
                {
                    skill.Effect = SkillEffectKind.MeleeArc;
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 34f;
                    skill.Cooldown = 0.6f;
                    skill.ManaCost = 5f;   // TUNE
                    skill.Range = 5f;
                    skill.Radius = 4.5f;
                    skill.ArcDegrees = 140f;
                    skill.Modifiers = new[] { brutality };

                    // The one starter skill that controls rather than only
                    // hurting. A swing is the right place for it: it is short
                    // ranged and on a fast cooldown, so the diminishing-returns
                    // window is something the player meets within seconds rather
                    // than a rule they read about.
                    skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Stun);
                });

            SkillDefinition wall = CreateZoneSkill();

            // The two a weapon is born with. Created here rather than beside
            // the gems because they are the same kind of thing — a skill asset —
            // and the database below must contain them or a welded weapon
            // resolves to nothing.
            CreateDefaultAttacks(out SkillDefinition strike, out SkillDefinition bolt);
            SkillDefinition arrow = CreateArrowAttack();
            SkillDefinition blink = CreateBlinkStrike();

            return new[]
            {
                splinter, nova, lance, sweep, wall, strike, bolt, arrow, blink,
                CreateMendingWord(), CreateBattleHymn()
            };
        }

        /// <summary>
        /// The first team spell: a heal aimed at a partner. Its damage is the
        /// heal, and weapon damage does not add to it.
        /// </summary>
        public static SkillDefinition CreateMendingWord()
            => Skill("MendingWord", "Mending Word", skill =>
            {
                skill.Effect = SkillEffectKind.AllyTarget;
                skill.DamageType = DamageType.Physical;
                skill.BaseDamage = 60f;     // TUNE: health back
                skill.Cooldown = 1.2f;      // TUNE
                skill.ManaCost = 14f;       // TUNE
                skill.Range = 16f;          // TUNE
                skill.Modifiers = System.Array.Empty<SkillModifier>();
            });

        /// <summary>
        /// The second: every ally around you hits harder for a while. No heal —
        /// the buff is the whole spell.
        /// </summary>
        public static SkillDefinition CreateBattleHymn()
            => Skill("BattleHymn", "Battle Hymn", skill =>
            {
                skill.Effect = SkillEffectKind.AllyAura;
                skill.DamageType = DamageType.Physical;
                skill.BaseDamage = 0f;
                skill.Cooldown = 8f;        // TUNE
                skill.ManaCost = 25f;       // TUNE
                skill.Range = 8f;
                skill.Radius = 8f;          // TUNE
                skill.Modifiers = System.Array.Empty<SkillModifier>();
                skill.AppliedStatus = ElementContentFactory.Status(StatusEffectType.Empower);
            });

        /// <summary>
        /// The blink every sword carries on its second key.
        ///
        /// No damage and no cost: it lands the primary key's skill on each body,
        /// so the numbers here are only where and how often. Three steps before
        /// any chain gem, and the cooldown starts when the last one lands.
        /// Create-or-load and in the starter list, for the reason the welded
        /// attacks are: an id the database has never heard of is a dead key.
        /// </summary>
        public static SkillDefinition CreateBlinkStrike()
            => Skill("ShadowStep", "Shadow Step", skill =>
            {
                skill.Effect = SkillEffectKind.BlinkStrike;
                skill.DamageType = DamageType.Physical;
                skill.BaseDamage = 0f;
                skill.Cooldown = 4f;        // TUNE
                skill.Range = 12f;          // TUNE: reach for the first body
                skill.BaseChains = 2;       // TUNE: three steps
                skill.ChainRange = 9f;      // TUNE: reach from one landing to the next body
                skill.ChainDelay = 0.2f;    // TUNE: seconds between steps
                skill.Modifiers = System.Array.Empty<SkillModifier>();
            });


        /// <summary>
        /// The attacks welded into weapons: a swing and a shot.
        ///
        /// Public and create-or-load, because two callers need the same two
        /// assets — this file, to put them in the skill database, and the item
        /// factory, to weld them into weapons. Naming them in both places would
        /// be two lists to keep in step, and the failure would be a weapon
        /// welded to a skill the database has never heard of.
        /// </summary>
        public static void CreateDefaultAttacks(
            out SkillDefinition strike, out SkillDefinition bolt)
        {
            // The two the player never chooses and never loses. Everything above
            // is a gem somebody has to find and socket; these are welded into
            // weapons, so they are what a character can do the moment they pick
            // anything up.
            //
            // Their base damage is deliberately tiny. A default attack is the
            // floor, not a build — and it is the one skill whose damage comes
            // almost entirely from the weapon holding it, which is exactly what
            // makes swapping a dagger for a Dawnbringer felt rather than read.
            // Both stay free — ManaCost is left at its zero default, and that is
            // a rule rather than an omission. A built-in attack is what the
            // player falls back on when the pool is empty, so charging for it
            // would make running out of mana mean standing still.
            strike =
                Skill("WeaponStrike", "Weapon Strike", skill =>
                {
                    skill.Effect = SkillEffectKind.MeleeArc;
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 6f;
                    skill.Cooldown = 0.45f;
                    skill.Range = 3.5f;
                    skill.Radius = 3f;
                    skill.ArcDegrees = 120f;
                    skill.Modifiers = System.Array.Empty<SkillModifier>();
                });

            bolt =
                Skill("WeaponBolt", "Weapon Bolt", skill =>
                {
                    skill.Effect = SkillEffectKind.Projectile;
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 5f;
                    skill.Cooldown = 0.5f;
                    skill.Range = 20f;
                    skill.ProjectileSpeed = 28f;
                    skill.Modifiers = System.Array.Empty<SkillModifier>();
                });

            // Both go in the database like any other skill. A welded id that
            // resolves to nothing is a weapon that cannot attack, and the
            // failure is silent — the socket looks full and the key does nothing.
        }

        /// <summary>
        /// The shot a bow is born with: the weapon bolt's numbers, a little
        /// longer and faster, flying as an arrow.
        ///
        /// Its own skill rather than the bolt with another look, because the
        /// look belongs to the skill — and a wand firing the same bolt should
        /// not start firing arrows. Create-or-load, and in the starter list, for
        /// the reason the other two are: a welded id the database has never
        /// heard of is a bow that cannot shoot.
        /// </summary>
        public static SkillDefinition CreateArrowAttack()
            => Skill("WeaponArrow", "Weapon Arrow", skill =>
            {
                skill.Effect = SkillEffectKind.Projectile;
                skill.DamageType = DamageType.Physical;
                skill.BaseDamage = 6f;
                skill.Cooldown = 0.55f;
                skill.Range = 24f;
                skill.ProjectileSpeed = 32f;
                skill.Modifiers = System.Array.Empty<SkillModifier>();
            });

        /// <summary>
        /// The zone skill, create-or-load, named rather than counted.
        ///
        /// Public for the same reason CreateDefaultAttacks is: another caller
        /// needs this exact asset — the gem factory, which has to point a gem at
        /// it. It used to reach for it by position in the array above, and the
        /// array has since grown two welded attacks on the end, so "the last
        /// skill" quietly became the weapon bolt. A name cannot drift like that.
        /// </summary>
        public static SkillDefinition CreateZoneSkill()
        {
            // The only skill that leaves something behind. It exists to be flown
            // through: a wall of fire is where a projectile picks an element up,
            // and without one in the game the carried half of the reaction system
            // has nothing to demonstrate itself with.
            return Skill("CinderWall", "Cinder Wall", skill =>
            {
                skill.Effect = SkillEffectKind.PersistentZone;
                skill.DamageType = DamageType.Fire;

                // Per pulse, not per cast. Low on purpose: what a zone is for
                // is the ignite it keeps applying and the element it hands
                // out, not the damage.
                skill.BaseDamage = 9f;
                skill.Cooldown = 4f;
                    skill.ManaCost = 20f;   // TUNE
                skill.Range = 14f;
                skill.Radius = 3f;
                skill.ZoneDuration = 6f;
                skill.ZoneTickInterval = 0.5f;

                // No supports. Increased area is the one that reads on a zone
                // and it is a gem the player can socket themselves — which is
                // the whole point of gems being the build.
                skill.Modifiers = System.Array.Empty<SkillModifier>();
            });
        }

        /// <summary>
        /// The two gems and the two rings that make the newer half of the model
        /// reachable without authoring anything by hand.
        ///
        /// Deliberately the smallest set that demonstrates all three mechanisms
        /// AND their interaction, rather than one of each:
        ///
        ///   Cast on Kill, socketed beside Cinder Nova, turns the burst into
        ///   something that goes off when a wave starts falling apart — and
        ///   takes the key away, which is the rule worth seeing enforced.
        ///
        ///   Kindled Chain adds jumps only against burning bodies, so the same
        ///   gem is dead weight on a fresh crowd and the payoff for having lit
        ///   one. It reads as a different gem depending on what else is
        ///   socketed, which is the entire claim.
        ///
        ///   Stormcaller's Coil turns Cinder Nova from a circle into a chain,
        ///   which is what makes Kindled Chain matter to an area skill at all —
        ///   and neither the keystone nor the support has heard of the other.
        ///
        ///   Ashen Signet is the honest downside: burns resolve at once, so
        ///   nothing stays alight and the conditional support beside it stops
        ///   finding its condition. Two keystones that pull against each other
        ///   are how a player learns that only one is ever in force.
        ///
        /// They go into the chest table rather than the starter kit. The kit is
        /// authored balance and is left alone; these are things to find.
        /// </summary>
        /// <remarks>
        /// Takes no skills, unlike the zone gem factory beside it, and that is
        /// the point rather than an oversight: a condition trigger names no
        /// skill and a conditional support tunes whatever it is linked to. Both
        /// are about the group they end up in, which is the player's business.
        /// </remarks>
        public static void CreateBuildContent(LootTable table)
        {
            SkillModifier castOnKill = Modifier(
                "SupportCastOnKill", SkillModifierKind.TriggerOnCondition, 0f,
                triggerCondition: TriggerConditionType.OnKill,
                triggerCooldown: 1.5f,                                          // TUNE
                procChance: 1f);                                                // TUNE

            SkillModifier kindledChain = Modifier(
                "SupportKindledChain", SkillModifierKind.AddedChains, 3f,
                condition: ModifierConditionType.TargetHasStatus,
                requiredElement: DamageType.Fire);

            SupportGem(
                "GemCastOnKill", "Cast on Kill Support", castOnKill, ItemRarity.Rare, table);

            SupportGem(
                "GemKindledChain", "Kindled Chain Support", kindledChain, ItemRarity.Rare, table);

            // The same mechanism as Kindled Chain, asking about the wider set.
            // Together they are the argument for two condition values rather
            // than one: fire is something a target carries AND something a shot
            // can fly through, while being slowed is only ever a state a body is
            // in.
            SkillModifier rivenChain = Modifier(
                "SupportRivenChain", SkillModifierKind.AddedChains, 3f,
                condition: ModifierConditionType.TargetHasStatusEffect,
                requiredStatus: StatusEffectType.Slow);

            SupportGem(
                "GemRivenChain", "Riven Chain Support", rivenChain, ItemRarity.Rare, table);

            Ring(
                "StormcallersCoil", "Stormcallers Coil", KeystoneEffect.AoeToChain, table);

            Ring(
                "AshenSignet", "Ashen Signet", KeystoneEffect.StatusInstantResolve, table);

            // The third ring, and the one that ties control to the rest. Cinder
            // Nova already slows, so wearing it turns a burst somebody chose for
            // its damage into a party-wide damage multiplier — and it pulls
            // against Ashen Signet in the usual way, since a build under one is
            // never under the other.
            Ring(
                "FrozenHourglass", "Frozen Hourglass", KeystoneEffect.SlowsAlsoWeaken, table);

            // The fourth keystone, and the only one of the implemented set that
            // had no item to reach it by. It spends the mark that fed a reaction
            // every time, whatever the rule says — so the chill that used to pay
            // out on every bolt now pays once, and a build that lives on
            // reactions has to keep re-marking instead of standing still.
            //
            // It is the natural enemy of the conditional supports: a gem that
            // only acts against burning bodies finds fewer of them when every
            // reaction eats the burn.
            Ring(
                "GluttonsMark", "Gluttons Mark", KeystoneEffect.ReactionsAlwaysConsume, table);

            // The fifth, and the one that only became wearable when it was
            // allowed to carry an affix. There is still no mana, so the
            // keystone half is pure downside — cooldowns double — and the
            // increased damage beside it is what pays for that. It is the one
            // place the "no stats on a keystone" rule is deliberately broken,
            // and the reason is the rule's own: a keystone should be chosen for
            // what it does to the rules, and an effect with no upside at all is
            // not a choice, it is an item nobody picks up.
            //
            // The day casting costs something, the affix comes back off.
            Ring(
                "BloodwroughtBand", "Bloodwrought Band",
                KeystoneEffect.NoManaCostDoubleCooldown, table,
                new[] { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 30f) });  // TUNE
        }

        /// <summary>
        /// An active gem: an ordinary item that happens to carry a skill.
        ///
        /// The counterpart of SupportGem below, and written once here rather
        /// than per skill because "what a gem is" must not differ between one
        /// skill and the next — a gem authored two cells wide, or landing in
        /// the wrong slot mask, would be a build rule invented by accident.
        ///
        /// Rarity is the caller's, because it is the only thing about a gem
        /// that is content rather than shape: a gem that casts a wall of fire
        /// is not the same find as one that casts a spark.
        /// </summary>
        internal static ItemDefinition ActiveGem(
            string assetName,
            string displayName,
            SkillDefinition skill,
            ItemRarity rarity,
            LootTable table)
        {
            ItemDefinition gem = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                assetName, $"{ItemFolder}/Gems/Active", out bool created);

            if (created)
            {
                var serialized = new SerializedObject(gem);
                serialized.FindProperty("_displayName").stringValue = displayName;
                serialized.FindProperty("_rarity").enumValueIndex = (int)rarity;

                // Main hand, like every gem: the slot mask is what stops a gem
                // being worn as a helmet, and a gem is never worn at all.
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_gemKind").enumValueIndex = (int)GemKind.Active;
                serialized.FindProperty("_gemSkill").objectReferenceValue = skill;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, gem);
            return gem;
        }

        /// <summary>
        /// A support gem: an ordinary item that happens to carry a modifier.
        ///
        /// One cell, like every other gem, because a gem competes for bag space
        /// with the loot it is meant to help you take.
        /// </summary>
        /// <remarks>
        /// The optional second modifier is what makes a gem two-sided: the
        /// benefit and its price on one stone. Nothing about the item changes —
        /// still one cell, still the main-hand mask — because a gem that costs
        /// you something is not a different kind of object, only a different
        /// decision.
        /// </remarks>
        internal static ItemDefinition SupportGem(
            string assetName,
            string displayName,
            SkillModifier support,
            ItemRarity rarity,
            LootTable table,
            SkillModifier second = null)
        {
            ItemDefinition gem = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                assetName, $"{ItemFolder}/Gems/Support", out bool created);

            if (created)
            {
                var serialized = new SerializedObject(gem);
                serialized.FindProperty("_displayName").stringValue = displayName;
                serialized.FindProperty("_rarity").enumValueIndex = (int)rarity;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_gemKind").enumValueIndex = (int)GemKind.Support;
                serialized.FindProperty("_gemSupport").objectReferenceValue = support;
                serialized.FindProperty("_gemSupportSecond").objectReferenceValue = second;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, gem);
            return gem;
        }

        /// <summary>
        /// A ring that breaks a rule.
        ///
        /// A ring rather than a weapon on purpose: rings are the one item that
        /// fits in two slots, so two keystones can be worn at once without any
        /// contrivance — which is exactly the case the "only the first applies"
        /// rule exists for, and the one a player will find on their own.
        ///
        /// No stats, for a keystone. A keystone should be chosen for what it
        /// does to the rules, not carried because it also happens to add damage
        /// — the one exception is named where it is made.
        ///
        /// Called with KeystoneEffect.None it is simply "a ring with affixes",
        /// which is what it was always doing underneath: a jewel that breaks a
        /// rule and a jewel that carries a stat are the same asset written the
        /// same way, and a second copy of this method for the second case would
        /// be two places to change the footprint of a ring.
        /// </summary>
        internal static ItemDefinition Ring(
            string assetName, string displayName, KeystoneEffect effect, LootTable table,
            ItemAffix[] affixes = null, ItemRarity rarity = ItemRarity.Legendary)
        {
            ItemDefinition ring = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                assetName, $"{ItemFolder}/Jewellery", out bool created);

            if (created)
            {
                var serialized = new SerializedObject(ring);
                serialized.FindProperty("_displayName").stringValue = displayName;
                serialized.FindProperty("_rarity").enumValueIndex = (int)rarity;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.Ring1;
                serialized.FindProperty("_keystone").enumValueIndex = (int)effect;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 1;

                SerializedProperty array = serialized.FindProperty("_affixes");
                array.arraySize = affixes == null ? 0 : affixes.Length;

                for (int i = 0; affixes != null && i < affixes.Length; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("_stat").enumValueIndex = (int)affixes[i].Stat;
                    element.FindPropertyRelative("_kind").enumValueIndex = (int)affixes[i].Kind;
                    element.FindPropertyRelative("_value").floatValue = affixes[i].Value;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, ring);
            return ring;
        }

        internal static SkillModifier Modifier(
            string assetName,
            SkillModifierKind kind,
            float value,
            float secondary = 60f,
            DamageType convertTo = DamageType.Fire,
            SkillDefinition triggered = null,
            TriggerConditionType triggerCondition = TriggerConditionType.OnKill,
            float triggerCooldown = 3f,
            float procChance = 0.35f,
            ModifierConditionType condition = ModifierConditionType.None,
            DamageType requiredElement = DamageType.Fire,
            StatusEffectType requiredStatus = StatusEffectType.Stun,
            float threshold = 0.35f,
            int requiredCount = 3,
            StatusEffectType appliedStatus = StatusEffectType.None)
        {
            SkillModifier modifier = SceneBuildUtility.CreateOrLoadConfig<SkillModifier>(
                assetName, $"{SkillFolder}/Supports", out bool created);

            if (!created)
                return modifier;

            var serialized = new SerializedObject(modifier);
            serialized.FindProperty("_kind").enumValueIndex = (int)kind;
            serialized.FindProperty("_value").floatValue = value;
            serialized.FindProperty("_secondaryValue").floatValue = secondary;
            serialized.FindProperty("_convertTo").enumValueIndex = (int)convertTo;
            serialized.FindProperty("_triggeredSkill").objectReferenceValue = triggered;

            serialized.FindProperty("_triggerCondition").enumValueIndex = (int)triggerCondition;
            serialized.FindProperty("_triggerCooldown").floatValue = triggerCooldown;
            serialized.FindProperty("_procChance").floatValue = procChance;

            serialized.FindProperty("_condition").enumValueIndex = (int)condition;
            serialized.FindProperty("_requiredElement").enumValueIndex = (int)requiredElement;
            serialized.FindProperty("_requiredStatus").enumValueIndex = (int)requiredStatus;
            serialized.FindProperty("_threshold").floatValue = threshold;
            serialized.FindProperty("_requiredCount").intValue = requiredCount;

            // The asset, not the enum: a status override names a definition so
            // the baker can depend on it, exactly as a skill does. None means
            // no reference at all rather than a reference to nothing.
            serialized.FindProperty("_appliedStatus").objectReferenceValue =
                appliedStatus == StatusEffectType.None
                    ? null
                    : ElementContentFactory.Status(appliedStatus);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return modifier;
        }

        internal static SkillDefinition Skill(
            string assetName, string displayName, System.Action<SkillFields> fill)
        {
            SkillDefinition skill = SceneBuildUtility.CreateOrLoadConfig<SkillDefinition>(
                assetName, $"{SkillFolder}/Active", out bool created);

            var fields = new SkillFields();
            fill(fields);

            if (!created)
            {
                // The one field written into an asset that already exists.
                //
                // It is a reference rather than a number — the same class of
                // thing as the reaction table lists, which are rebuilt for the
                // same reason. A skill authored before statuses existed comes
                // back with none, and the symptom would not be a missing field:
                // it would be a stun that never lands, on a swing that looks
                // exactly as it always did, in a build where nothing says why.
                WriteAppliedStatus(skill, fields.AppliedStatus);
                return skill;
            }

            var serialized = new SerializedObject(skill);
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_effect").enumValueIndex = (int)fields.Effect;
            serialized.FindProperty("_damageType").enumValueIndex = (int)fields.DamageType;
            serialized.FindProperty("_baseDamage").floatValue = fields.BaseDamage;
            serialized.FindProperty("_cooldown").floatValue = fields.Cooldown;
            serialized.FindProperty("_manaCost").floatValue = fields.ManaCost;
            serialized.FindProperty("_range").floatValue = fields.Range;
            serialized.FindProperty("_radius").floatValue = fields.Radius;
            serialized.FindProperty("_arcDegrees").floatValue = fields.ArcDegrees;
            serialized.FindProperty("_projectileSpeed").floatValue = fields.ProjectileSpeed;
            serialized.FindProperty("_zoneDuration").floatValue = fields.ZoneDuration;
            serialized.FindProperty("_zoneTickInterval").floatValue = fields.ZoneTickInterval;
            serialized.FindProperty("_baseChains").intValue = fields.BaseChains;
            serialized.FindProperty("_chainRange").floatValue = fields.ChainRange;
            serialized.FindProperty("_chainDelay").floatValue = fields.ChainDelay;
            serialized.FindProperty("_count").intValue = fields.Count;
            serialized.FindProperty("_interval").floatValue = fields.Interval;
            serialized.FindProperty("_scatter").floatValue = fields.Scatter;

            SerializedProperty modifiers = serialized.FindProperty("_modifiers");
            modifiers.arraySize = fields.Modifiers.Length;

            for (int i = 0; i < fields.Modifiers.Length; i++)
                modifiers.GetArrayElementAtIndex(i).objectReferenceValue = fields.Modifiers[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            WriteAppliedStatus(skill, fields.AppliedStatus);
            return skill;
        }

        private static void WriteAppliedStatus(
            SkillDefinition skill, StatusEffectDefinition status)
        {
            if (status == null)
                return;

            var serialized = new SerializedObject(skill);
            serialized.FindProperty("_appliedStatus").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A plain bag of the values a skill asset holds.
        ///
        /// It exists so the table above reads as a table. Writing twelve
        /// SerializedProperty lines per skill inline would bury which numbers
        /// were chosen under how they are stored.
        /// </summary>
        internal sealed class SkillFields
        {
            public SkillEffectKind Effect = SkillEffectKind.Projectile;
            public DamageType DamageType = DamageType.Physical;
            public float BaseDamage = 20f;
            public float Cooldown = 0.5f;

            /// <summary>
            /// Mana the press costs. Zero by default, which is the right default
            /// for the one skill that must never cost anything — a weapon's
            /// built-in attack, where an empty pool would mean no attack at all.
            /// </summary>
            public float ManaCost;

            public float Range = 20f;
            public float Radius;
            public float ArcDegrees = 360f;
            public float ProjectileSpeed = 26f;
            public float ZoneDuration = 5f;
            public float ZoneTickInterval = 0.5f;
            public int BaseChains;
            public float ChainRange = 8f;
            public float ChainDelay = 0.07f;

            /// <summary>Elements in a pattern: projectiles, blasts or pulses.</summary>
            public int Count = 1;
            public float Interval = 0.1f;
            public float Scatter = 4f;

            public SkillModifier[] Modifiers = System.Array.Empty<SkillModifier>();

            /// <summary>A status the skill marks everything it hits with, or null.</summary>
            public StatusEffectDefinition AppliedStatus;
        }
    }
}
