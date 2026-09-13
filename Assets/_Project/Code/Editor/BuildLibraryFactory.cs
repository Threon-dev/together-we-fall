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
            ConditionalGems(skills, table);
            LeverGems(table);
            TwoSidedGems(table);
            CritJewellery(table);
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

            // The two the starter skills have always socketed and no factory
            // ever made into gems. Both assets exist on disk from an earlier
            // hand-authored run, which is why nobody noticed: a clean clone
            // would have had the modifier and no stone to put it in.
            SkillContentFactory.SupportGem(
                "GemBrutality", "Brutality Support",
                SkillContentFactory.Modifier(
                    "SupportBrutality", SkillModifierKind.IncreasedDamage, 35f),
                ItemRarity.Common, table);

            SkillContentFactory.SupportGem(
                "GemGreaterArea", "Greater Area Support",
                SkillContentFactory.Modifier(
                    "SupportGreaterArea", SkillModifierKind.IncreasedArea, 40f),
                ItemRarity.Common, table);

            // The kind that had a fold entry, a tooltip line and no stone at
            // all. A projectile speed support is dull read on its own and is
            // the difference between a toxic spit that can be dodged and one
            // that cannot — which is the only reason the spit was authored slow.
            SkillContentFactory.SupportGem(
                "GemWindrunner", "Windrunner Support",
                SkillContentFactory.Modifier(
                    "SupportWindrunner", SkillModifierKind.IncreasedProjectileSpeed, 60f),  // TUNE
                ItemRarity.Common, table);
        }

        /// <summary>
        /// The gems that ask a question, and nothing else.
        ///
        /// Every one of these is an existing modifier kind with a condition on
        /// it, which is the cheapest content in the game: no system heard about
        /// any of it, and each stone reads as a different gem depending on what
        /// is socketed beside it. That is the claim the condition axis was built
        /// for, tested at a scale where it either holds or visibly does not.
        ///
        /// The element family is the interesting half. Four gems that each help
        /// one element are four reasons NOT to socket a conversion — and
        /// Hoarfrost, which converts to cold, is also what switches Frostbite on.
        /// Neither gem has heard of the other.
        ///
        /// EVERY NUMBER HERE IS PROVISIONAL, like the rest of this file.
        /// </summary>
        private static void ConditionalGems(SkillDefinition[] skills, LootTable table)
        {
            // The cold twin of Devour. Fire has had a conditional damage gem
            // since conditions existed; cold now has one, and the pair is what
            // makes "which element is my build" a question with consequences.
            SkillContentFactory.SupportGem(
                "GemFrostbite", "Frostbite Support",
                SkillContentFactory.Modifier(
                    "SupportFrostbite", SkillModifierKind.IncreasedDamage, 100f,   // TUNE
                    condition: ModifierConditionType.TargetHasStatus,
                    requiredElement: DamageType.Cold),
                ItemRarity.Rare, table);

            // One per element, all the same shape. They ask about the SKILL
            // rather than the target, so unlike everything else here they are
            // never dead weight — they are either socketed on the right skill
            // or on the wrong one, and the player can read which from the gem.
            ElementFocus("GemEmberwright", "Emberwright Support",
                "SupportEmberwright", DamageType.Fire, table);

            ElementFocus("GemGlacialFocus", "Glacial Focus Support",
                "SupportGlacialFocus", DamageType.Cold, table);

            ElementFocus("GemGalvanicFocus", "Galvanic Focus Support",
                "SupportGalvanicFocus", DamageType.Lightning, table);

            ElementFocus("GemVirulence", "Virulence Support",
                "SupportVirulence", DamageType.Chaos, table);

            // The third chain gem, and the one that completes the set: Kindled
            // asks about fire, Riven about being slowed, this about shock. A
            // lightning build now has a conditional chain of its own, and the
            // condition is one its own element applies.
            SkillContentFactory.SupportGem(
                "GemConduit", "Conduit Support",
                SkillContentFactory.Modifier(
                    "SupportConduit", SkillModifierKind.AddedChains, 3f,           // TUNE
                    condition: ModifierConditionType.TargetHasStatusEffect,
                    requiredStatus: StatusEffectType.Shock),
                ItemRarity.Rare, table);

            // Area, but only against something already held still. It is the
            // first gem that pays a melee build for its own stun rather than
            // for an element.
            SkillContentFactory.SupportGem(
                "GemShatter", "Shatter Support",
                SkillContentFactory.Modifier(
                    "SupportShatter", SkillModifierKind.IncreasedArea, 70f,        // TUNE
                    condition: ModifierConditionType.TargetHasStatusEffect,
                    requiredStatus: StatusEffectType.Stun),
                ItemRarity.Uncommon, table);

            // Forks off the dying, which is the opposite arrangement to Fork
            // itself: it does nothing on a fresh crowd and everything on one
            // that is already falling apart.
            SkillContentFactory.SupportGem(
                "GemSplintering", "Splintering Support",
                SkillContentFactory.Modifier(
                    "SupportSplintering", SkillModifierKind.Fork, 2f,              // TUNE
                    condition: ModifierConditionType.TargetLowHealth,
                    threshold: 0.4f),                                             // TUNE
                ItemRarity.Rare, table);

            // An extra cast against the slowed. With the Frozen Hourglass ring
            // — every slow also weakens — a frost build ends up with a damage
            // multiplier and an extra projectile it never authored either of.
            SkillContentFactory.SupportGem(
                "GemCascade", "Cascade Support",
                SkillContentFactory.Modifier(
                    "SupportCascade", SkillModifierKind.Multicast, 1f,
                    condition: ModifierConditionType.TargetHasStatusEffect,
                    requiredStatus: StatusEffectType.Slow),
                ItemRarity.Rare, table);

            // A bigger burst, but only off burning bodies. Beside Ashen Signet
            // — burns resolve at once and nothing stays alight — it is the gem
            // that stops working because of a ring, which is the lesson those
            // keystones exist to teach.
            SkillContentFactory.SupportGem(
                "GemVolatile", "Volatile Support",
                SkillContentFactory.Modifier(
                    "SupportVolatile", SkillModifierKind.ExplodeOnKill, 5f,        // TUNE
                    secondary: 75f,                                               // TUNE
                    condition: ModifierConditionType.TargetHasStatus,
                    requiredElement: DamageType.Fire),
                ItemRarity.Rare, table);

            // The two that use the conditions added with this batch.
            //
            // Encircle is the answer to every area support being dead against
            // one target: it says so out loud instead of being quietly bad.
            SkillContentFactory.SupportGem(
                "GemEncircle", "Encircle Support",
                SkillContentFactory.Modifier(
                    "SupportEncircle", SkillModifierKind.IncreasedArea, 90f,       // TUNE
                    condition: ModifierConditionType.TargetCrowded,
                    requiredCount: 4),                                            // TUNE
                ItemRarity.Uncommon, table);

            // And the opener, which is Execute read backwards: it pays for the
            // first blow and stops paying the moment the fight is under way.
            SkillContentFactory.SupportGem(
                "GemFirstStrike", "First Strike Support",
                SkillContentFactory.Modifier(
                    "SupportFirstStrike", SkillModifierKind.IncreasedDamage, 90f,  // TUNE
                    condition: ModifierConditionType.TargetHighHealth,
                    threshold: 0.9f),                                             // TUNE
                ItemRarity.Uncommon, table);

            // The trigger nothing ever fired on, and the event has been raised
            // since reactions existed. An elemental build that is applying
            // statuses anyway gets a second skill for free — and pays for it by
            // giving up the hotkey, like every trigger gem.
            SkillContentFactory.SupportGem(
                "GemReactiveCascade", "Reactive Cascade Support",
                SkillContentFactory.Modifier(
                    "SupportReactiveCascade", SkillModifierKind.TriggerOnCondition, 0f,
                    triggerCondition: TriggerConditionType.OnStatusApplied,
                    triggerCooldown: 2.5f,                                        // TUNE
                    procChance: 0.5f),                                            // TUNE
                ItemRarity.Rare, table);

            // The first trigger-on-hit a PLAYER can socket. Every one before it
            // was welded into a skill by an author, which made the one support
            // that composes two skills the only one nobody could choose.
            SkillDefinition nova = Named(skills, "Frost Nova");

            if (nova != null)
            {
                SkillContentFactory.SupportGem(
                    "GemEchoNova", "Echoing Nova Support",
                    SkillContentFactory.Modifier(
                        "SupportEchoNova", SkillModifierKind.TriggerOnHit, 45f,    // TUNE
                        triggered: nova),
                    ItemRarity.Epic, table);
            }
        }

        /// <summary>
        /// One gem per element, each helping only skills authored as that
        /// element.
        ///
        /// Written once because the four differ in exactly one field, and four
        /// copies of the same block is how the third one ends up with a number
        /// nobody meant to change.
        /// </summary>
        private static void ElementFocus(
            string assetName, string displayName, string modifierName,
            DamageType element, LootTable table)
        {
            SkillContentFactory.SupportGem(
                assetName, displayName,
                SkillContentFactory.Modifier(
                    modifierName, SkillModifierKind.IncreasedDamage, 70f,          // TUNE
                    condition: ModifierConditionType.TargetElementType,
                    requiredElement: element),
                ItemRarity.Uncommon, table);
        }

        /// <summary>
        /// The gems for the levers nobody could pull: the price of a press, the
        /// cooldown, how long a zone burns, what a skill leaves on what it hits,
        /// how wide a multicast fans, what a projectile does to the body it
        /// reaches, and what a kill is worth.
        ///
        /// Each is one new modifier kind and one stone. None of them needed a
        /// system: they are numbers the fold was already producing and nothing
        /// could reach.
        /// </summary>
        private static void LeverGems(LootTable table)
        {
            SkillContentFactory.SupportGem(
                "GemSwiftcast", "Swiftcast Support",
                SkillContentFactory.Modifier(
                    "SupportSwiftcast", SkillModifierKind.ReducedCooldown, 30f),   // TUNE
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemFrugality", "Frugality Support",
                SkillContentFactory.Modifier(
                    "SupportFrugality", SkillModifierKind.IncreasedManaCost, -35f),  // TUNE
                ItemRarity.Uncommon, table);

            // Duration, at last, and only on the two zone skills — which is
            // what makes it a gem somebody chooses rather than one everybody
            // sockets.
            SkillContentFactory.SupportGem(
                "GemEverburning", "Everburning Support",
                SkillContentFactory.Modifier(
                    "SupportEverburning", SkillModifierKind.IncreasedDuration, 80f),  // TUNE
                ItemRarity.Uncommon, table);

            // The two status overrides. Root and Stun rather than a burn,
            // because a mark any element already applies would make the gem a
            // worse version of picking that element — what an override is for
            // is putting control on a skill that has none.
            SkillContentFactory.SupportGem(
                "GemRootingGrasp", "Rooting Grasp Support",
                SkillContentFactory.Modifier(
                    "SupportRootingGrasp", SkillModifierKind.StatusOverride, 0f,
                    appliedStatus: StatusEffectType.Root),
                ItemRarity.Rare, table);

            SkillContentFactory.SupportGem(
                "GemConcussive", "Concussive Support",
                SkillContentFactory.Modifier(
                    "SupportConcussive", SkillModifierKind.StatusOverride, 0f,
                    appliedStatus: StatusEffectType.Stun),
                ItemRarity.Rare, table);

            // The pair that turns Multicast into a decision. Neither does
            // anything on its own, which is the point: they are the first gems
            // whose value is entirely in what else is socketed.
            SkillContentFactory.SupportGem(
                "GemVolley", "Volley Support",
                SkillContentFactory.Modifier(
                    "SupportVolley", SkillModifierKind.IncreasedSpread, 200f),     // TUNE
                ItemRarity.Uncommon, table);

            SkillContentFactory.SupportGem(
                "GemFocusedLine", "Focused Line Support",
                SkillContentFactory.Modifier(
                    "SupportFocusedLine", SkillModifierKind.IncreasedSpread, -80f),  // TUNE
                ItemRarity.Uncommon, table);

            // The third of pierce, fork and chain — and the one that rewards
            // standing somewhere in particular rather than socketing something
            // in particular.
            SkillContentFactory.SupportGem(
                "GemPiercingShot", "Piercing Shot Support",
                SkillContentFactory.Modifier(
                    "SupportPiercingShot", SkillModifierKind.Pierce, 2f),          // TUNE
                ItemRarity.Rare, table);

            SkillContentFactory.SupportGem(
                "GemExecutionersEdge", "Executioners Edge Support",
                SkillContentFactory.Modifier(
                    "SupportExecutionersEdge", SkillModifierKind.CullingStrike, 12f),  // TUNE
                ItemRarity.Rare, table);

            // What pays for the cost gems above, and the reason they are a
            // trade rather than a tax: a build that kills keeps casting.
            SkillContentFactory.SupportGem(
                "GemSoulHarvest", "Soul Harvest Support",
                SkillContentFactory.Modifier(
                    "SupportSoulHarvest", SkillModifierKind.ManaOnKill, 4f),       // TUNE
                ItemRarity.Rare, table);

            // Worth nothing at all until something on the character sheet
            // grants crit chance, which is exactly what the two jewels below
            // are for.
            SkillContentFactory.SupportGem(
                "GemDeadlyAim", "Deadly Aim Support",
                SkillContentFactory.Modifier(
                    "SupportDeadlyAim", SkillModifierKind.IncreasedCritChance, 120f),  // TUNE
                ItemRarity.Rare, table);
        }

        /// <summary>
        /// The gems that cost something.
        ///
        /// A support gem was a pure gift until the item could carry two
        /// modifiers, which made every socketing decision the same decision:
        /// put in whatever is biggest. These are the other kind — a benefit
        /// worth more than any single-sided gem, and a price beside it on the
        /// same stone.
        ///
        /// The price is always a number the fold already had. Nothing here is a
        /// mechanism; it is the second field on an item.
        /// </summary>
        private static void TwoSidedGems(LootTable table)
        {
            // More damage than Brutality by a wide margin, and mana it has to
            // come out of. Beside Soul Harvest it is affordable; on a build
            // that misses it is empty orbs.
            SkillContentFactory.SupportGem(
                "GemOvercharge", "Overcharge Support",
                SkillContentFactory.Modifier(
                    "SupportOvercharge", SkillModifierKind.IncreasedDamage, 90f),  // TUNE
                ItemRarity.Epic, table,
                SkillContentFactory.Modifier(
                    "SupportOverchargeCost", SkillModifierKind.IncreasedManaCost, 70f));  // TUNE

            // An extra cast, paid for in cooldown rather than mana — the same
            // purchase as Multicast with the price finally attached to it.
            SkillContentFactory.SupportGem(
                "GemRecklessBarrage", "Reckless Barrage Support",
                SkillContentFactory.Modifier(
                    "SupportRecklessBarrage", SkillModifierKind.Multicast, 2f),    // TUNE
                ItemRarity.Epic, table,
                SkillContentFactory.Modifier(
                    "SupportRecklessBarrageSlow", SkillModifierKind.ReducedCooldown, -60f));  // TUNE

            // Culls deeper than Executioner's Edge and hits softer for it, which
            // is the whole build in one gem: stop caring how hard you hit and
            // start caring how many things are nearly dead.
            SkillContentFactory.SupportGem(
                "GemHemorrhage", "Hemorrhage Support",
                SkillContentFactory.Modifier(
                    "SupportHemorrhage", SkillModifierKind.CullingStrike, 20f),    // TUNE
                ItemRarity.Epic, table,
                SkillContentFactory.Modifier(
                    "SupportHemorrhageWeak", SkillModifierKind.IncreasedDamage, -35f));  // TUNE

            // Fast and expensive. The one to put on a skill that costs almost
            // nothing — Spark — and the one to keep well away from a nova.
            SkillContentFactory.SupportGem(
                "GemQuickening", "Quickening Support",
                SkillContentFactory.Modifier(
                    "SupportQuickening", SkillModifierKind.ReducedCooldown, 45f),  // TUNE
                ItemRarity.Epic, table,
                SkillContentFactory.Modifier(
                    "SupportQuickeningCost", SkillModifierKind.IncreasedManaCost, 90f));  // TUNE

            // Wide and weak, and the first gem whose two halves are the same
            // decision from both ends: it turns one blast into a crowd-clearer
            // and a single-target blast into a waste of mana.
            SkillContentFactory.SupportGem(
                "GemWidening", "Widening Support",
                SkillContentFactory.Modifier(
                    "SupportWidening", SkillModifierKind.IncreasedArea, 85f),      // TUNE
                ItemRarity.Rare, table,
                SkillContentFactory.Modifier(
                    "SupportWideningWeak", SkillModifierKind.IncreasedDamage, -30f));  // TUNE
        }

        /// <summary>
        /// The two jewels that make crit exist for a character at all.
        ///
        /// The sheet starts with a small chance and an ordinary multiplier — see
        /// SceneBuildUtility, which writes both into the character config — so
        /// every build crits occasionally and no build is built around it. These
        /// are what turn that into a decision, and what make Deadly Aim worth a
        /// hole: a support that increases a chance needs a chance to increase.
        /// </summary>
        private static void CritJewellery(LootTable table)
        {
            SkillContentFactory.Ring(
                "HuntersEye", "Hunters Eye", KeystoneEffect.None, table,
                new[] { new ItemAffix(StatKind.CritChance, ModifierKind.Flat, 15f) },  // TUNE
                ItemRarity.Rare);

            // The other half of the purchase: crit rarely, but enormously. A
            // build wearing both is choosing crit over everything else two
            // ring slots could have been.
            SkillContentFactory.Ring(
                "CruelSigil", "Cruel Sigil", KeystoneEffect.None, table,
                new[] { new ItemAffix(StatKind.CritMultiplier, ModifierKind.Flat, 0.8f) },  // TUNE
                ItemRarity.Rare);
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
                "RiftwoodStaff", $"{ItemFolder}/Weapons/Staves", out bool created);

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
            Testbed("TestbedWand", "Testbed Wand", EquipmentSlot.MainHand, "Wands",
                new[] { 0, 0, 0, 0 }, table);

            Testbed("TestbedFocus", "Testbed Focus", EquipmentSlot.OffHand, "Foci",
                new[] { 0, 0 }, table);
        }

        private static void Testbed(
            string assetName, string displayName, EquipmentSlot slot, string weaponFolder,
            int[] groups, LootTable table)
        {
            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                assetName, $"{ItemFolder}/Weapons/{weaponFolder}", out bool created);

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

            var kit = SceneBuildUtility.CreateOrLoadConfig<StarterKitConfig>("StarterKitConfig");

            // The staff first, because it is the thing everything else goes
            // into, and then every support gem in the game.
            //
            // All of them rather than a chosen few: the staff has twelve support
            // holes now, and a kit that hands out four of them is a kit that
            // decides the build. What is being tested is which combinations are
            // worth making, and that question needs the whole set on the table.
            //
            // Active gems, and the question they reopened now has an answer:
            // they are what a trigger CASTS. A weapon rolls the skill it comes
            // with and keeps it on the key; an active gem socketed behind that
            // one, with a trigger gem in the same group, is a passive that goes
            // off on its own. So the kit hands out a handful of actives worth
            // putting there — a burst, a wall, a nova and a bolt — rather than
            // none at all.
            var wanted = new[]
            {
                "RiftwoodStaff",

                // The pair with free head sockets, and the active gems the
                // written test build needs in them. Everything else in this kit
                // is a support; these are the only way to choose which skill is
                // being supported, because every other weapon rolls that for
                // itself.
                "TestbedWand", "TestbedFocus",

                // The actives: one to bind by hand on the testbed pair, and
                // three worth socketing behind a welded attack as passives.
                "GemSpark", "GemCinderWall", "GemFirebolt", "GemFrostNova",

                // Shape: how much exists and how big it is.
                "GemChain", "GemFork", "GemMulticast", "GemGreaterArea", "GemBrutality",
                "GemWindrunner", "GemPiercingShot", "GemVolley", "GemFocusedLine",

                // Element.
                "GemHoarfrost",
                "GemEmberwright", "GemGlacialFocus", "GemGalvanicFocus", "GemVirulence",

                // Conditional: the same gem twice over, depending on the target.
                "GemDevour", "GemExecute", "GemKindledChain", "GemRivenChain",
                "GemFrostbite", "GemConduit", "GemShatter", "GemSplintering",
                "GemCascade", "GemVolatile", "GemEncircle", "GemFirstStrike",

                // Levers: the cost of a press, the cooldown, the duration, the
                // status, the kill.
                "GemSwiftcast", "GemFrugality", "GemEverburning",
                "GemRootingGrasp", "GemConcussive",
                "GemExecutionersEdge", "GemSoulHarvest", "GemDeadlyAim",

                // Two-sided: the gems that cost something.
                "GemOvercharge", "GemRecklessBarrage", "GemHemorrhage",
                "GemQuickening", "GemWidening",

                // The jewels crit needs to exist at all, and the support that
                // is worthless without them.
                "HuntersEye", "CruelSigil",

                // Trigger: all four have an event now — kill, crit, status
                // applied, and the one still waiting on player health.
                "GemCastOnKill", "GemCastOnCrit", "GemCastOnLowHealth",
                "GemReactiveCascade", "GemEchoNova"
            };

            var serialized = new SerializedObject(kit);
            SerializedProperty entries = serialized.FindProperty("_entries");
            int added = 0;

            for (int i = 0; i < wanted.Length; i++)
            {
                var item = SceneBuildUtility.LoadConfig<ItemDefinition>(wanted[i]);

                if (item == null || Contains(entries, item))
                    continue;

                // In the bag, one copy each: which of these ends up worn or
                // socketed is the question the kit exists to let somebody
                // answer by hand, so it hands out the parts and no build.
                SceneBuildUtility.AppendKitEntry(entries, item, worn: false, count: 1);
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[BuildLibraryFactory] Test kit: {added} item(s) added to {kit.name}. " +
                "Edit that asset to change what a character starts with; rebuild the scene so " +
                "the skill database contains the library.");
        }

        /// <summary>
        /// Whether a kit already lists this item, whatever it asks be done with
        /// it. Each element is an entry with an item inside rather than the item
        /// itself, so the reference is one level down.
        /// </summary>
        private static bool Contains(SerializedProperty entries, Object value)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_item").objectReferenceValue == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
