using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Loot;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Creates the starter statuses and reaction rules.
    ///
    /// Its own file for the same reason SkillContentFactory is: the set is the
    /// interesting part. Five rules chosen so that every result kind is reachable
    /// by holding a button with the gems the game already hands out — a bonus, a
    /// blast, and a status that only a combination can produce — and so that both
    /// halves of the feature are visible without authoring anything by hand.
    ///
    /// With the starter skills as they stand:
    ///   Cinder Nova ignites a crowd, and Splinter Bolt then hits it harder.
    ///   Storm Lance shocks, and fire onto a shocked body hits harder still.
    ///   A Splinter Bolt steered through a Cinder Wall arrives carrying fire and
    ///   lands as though the target were burning — the same rule, reached the
    ///   other way round. That is the whole claim of the design in one sentence.
    ///
    /// Assets are filled in only when created. Rebuilding a scene must never
    /// overwrite numbers somebody has since tuned; the point of these being
    /// assets is that they are edited by hand.
    /// </summary>
    public static class ElementContentFactory
    {
        private const string ElementFolder = "Assets/_Project/Data/Elements";
        private const string ItemFolder = "Assets/_Project/Data/Items";

        /// <summary>
        /// The reaction table, with every rule and status wired into it.
        ///
        /// The table is rebuilt from the assets each time even when it already
        /// exists, because it is a list of references rather than tuned numbers —
        /// a rule asset that exists but is not in the table is the one failure
        /// here that produces no error and no effect.
        /// </summary>
        public static ElementReactionTable CreateReactionTable()
        {
            StatusEffectDefinition ignite = Status(StatusEffectType.Ignite);
            StatusEffectDefinition shock = Status(StatusEffectType.Shock);
            StatusEffectDefinition chill = Status(StatusEffectType.Chill);

            // Only ever produced by a reaction, never by a plain hit — which is
            // what makes it worth having: a burn that only a combination can
            // light, in an element nothing else in the game deals.
            StatusEffectDefinition scorch = Status(StatusEffectType.Scorch);

            // Chaos marks with poison, which is what makes a chaos zone worth
            // standing in front of: the cloud pulses every second and a half,
            // and poison is the one burn that stacks its duration rather than
            // refreshing it, so the mark is built up by staying rather than by
            // being hit hardest. Nothing dealt chaos before the library below,
            // which is why this element had no default until now.
            StatusEffectDefinition poison = Status(StatusEffectType.Poison);

            var rules = new List<ElementReactionRule>
            {
                // The classic: burn the charge off a shocked target.
                Rule("ReactionShockedByFire", DamageType.Lightning, DamageType.Fire,
                    ElementReactionKind.BonusDamage, multiplier: 1.6f, consumes: true),

                // Steam. A blast rather than a status, because a status that
                // reduced an enemy's accuracy would have nothing to reduce —
                // enemies do not attack yet, and a rule that quietly does
                // nothing is worse than one that is honest about what the game
                // can express today.
                Rule("ReactionIgnitedByCold", DamageType.Fire, DamageType.Cold,
                    ElementReactionKind.Explosion, multiplier: 1.2f, consumes: true,
                    radius: 3.5f),

                // Chill conducts, and keeps conducting: this one does not consume,
                // so the next bolt into the same body pays out again.
                Rule("ReactionChilledByLightning", DamageType.Cold, DamageType.Lightning,
                    ElementReactionKind.BonusDamage, multiplier: 1.4f, consumes: false),

                // The one that makes the whole thing visible with no new gems:
                // ordinary physical shots hit burning bodies harder, and a shot
                // that flew through a wall of fire counts as burning even when
                // its target is not.
                Rule("ReactionIgnitedByPhysical", DamageType.Fire, DamageType.Physical,
                    ElementReactionKind.BonusDamage, multiplier: 1.5f, consumes: false),

                // The reverse of the first rule, and deliberately a different
                // outcome. A pair is ordered: what was already there and what
                // just arrived are not interchangeable.
                Rule("ReactionIgnitedByLightning", DamageType.Fire, DamageType.Lightning,
                    ElementReactionKind.ApplyStatus, multiplier: 1f, consumes: true,
                    resultStatus: scorch),

                // Fire onto poison detonates it. The blast is small on purpose —
                // a poison cloud covers a crowd, so a generous radius here would
                // be one shot into a cloud clearing the room, and the reaction
                // blast is flagged FromReaction and so cannot chain into itself.
                //
                // It is worth the asset because it is the one reaction reachable
                // from two skills nobody has to arrange: the cloud poisons a
                // crowd on its own, and every fire skill in the library sets it
                // off. TUNE: radius and multiplier both.
                Rule("ReactionPoisonedByFire", DamageType.Chaos, DamageType.Fire,
                    ElementReactionKind.Explosion, multiplier: 1.8f, consumes: true,
                    radius: 2f)
            };

            ElementReactionTable table =
                SceneBuildUtility.CreateOrLoadConfig<ElementReactionTable>(
                    "ElementReactionTable", ElementFolder, out _);

            var serialized = new SerializedObject(table);

            // Every status there is, whether or not an element leaves it and
            // whether or not a reaction makes it. This list is the whole reason
            // a stun can exist: nothing marks a target with one on its own, so
            // without being named here it would be an asset the runtime has
            // never heard of, and the skill applying it would apply nothing.
            WriteList(serialized.FindProperty("_statuses"), AllStatuses());

            WriteList(serialized.FindProperty("_defaultStatuses"),
                new Object[] { ignite, shock, chill, poison });

            SerializedProperty ruleList = serialized.FindProperty("_rules");
            ruleList.arraySize = rules.Count;

            for (int i = 0; i < rules.Count; i++)
                ruleList.GetArrayElementAtIndex(i).objectReferenceValue = rules[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return table;
        }

        /// <summary>
        /// The gem that casts the zone skill, and gets it into a loot table so it
        /// can actually be found.
        ///
        /// A gem rather than a skill handed out directly, because that is the
        /// only way a skill exists in this game: something is in a socket. The
        /// starter kit is authored balance and is left alone — this goes into the
        /// chest table beside the other gems, which is where a player would
        /// expect to find one.
        /// </summary>
        public static ItemDefinition CreateZoneGem(SkillDefinition zoneSkill, LootTable table)
            => SkillContentFactory.ActiveGem(
                "GemCinderWall", "Cinder Wall Gem", zoneSkill, ItemRarity.Uncommon, table);

        /// <summary>
        /// Every status the game has, in one array, for the table to reference.
        ///
        /// Listed rather than discovered by scanning the folder: a factory that
        /// picks up whatever assets happen to be on disk cannot be read to find
        /// out what the game contains, and an experiment somebody left behind
        /// would quietly become content.
        /// </summary>
        private static Object[] AllStatuses()
        {
            var types = new[]
            {
                StatusEffectType.Ignite,
                StatusEffectType.Shock,
                StatusEffectType.Chill,
                StatusEffectType.Scorch,
                StatusEffectType.Poison,
                StatusEffectType.Bleed,
                StatusEffectType.Stun,
                StatusEffectType.Slow,
                StatusEffectType.Root,
                StatusEffectType.Silence,
                StatusEffectType.Fear,
                StatusEffectType.Weaken,
                StatusEffectType.Vulnerable,
                StatusEffectType.Haste,
                StatusEffectType.Fortify
            };

            var assets = new Object[types.Length];

            for (int i = 0; i < types.Length; i++)
                assets[i] = Status(types[i]);

            return assets;
        }

        /// <summary>
        /// The asset for one status, created if it is not there yet.
        ///
        /// Public and create-or-load because two callers need the same assets:
        /// this file, to put them in the reaction table, and SkillContentFactory,
        /// to hand one to the skill that applies it. The same shape as
        /// CreateDefaultAttacks — naming them in both places would be two lists
        /// to keep in step, and the failure would be a skill applying a status
        /// the table has never heard of.
        ///
        /// The numbers below are the starting balance and are written only when
        /// the asset is created, so tuning survives a rebuild. The TYPE is
        /// written every time: it is the asset identity rather than a number,
        /// and an asset authored before that field existed comes back as None —
        /// which is a status nothing can name and no reaction can tell apart
        /// from the next one.
        /// </summary>
        public static StatusEffectDefinition Status(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Ignite:
                    return Status("StatusIgnite", "Ignited", type, DamageType.Fire,
                        duration: 4f, maxStacks: 3, damagePerSecond: 7f);

                case StatusEffectType.Shock:
                    return Status("StatusShock", "Shocked", type, DamageType.Lightning,
                        duration: 5f);

                case StatusEffectType.Chill:
                    return Status("StatusChill", "Chilled", type, DamageType.Cold,
                        duration: 4f);

                case StatusEffectType.Scorch:
                    return Status("StatusScorched", "Scorched", type, DamageType.Chaos,
                        duration: 4f, maxStacks: 3, damagePerSecond: 11f);

                // Stacks its duration rather than refreshing it, which is what
                // makes it a different burn from ignite with the same fields:
                // keeping something poisoned is worth more than relighting it.
                case StatusEffectType.Poison:
                    return Status("StatusPoison", "Poisoned", type, DamageType.Chaos,
                        duration: 3f, maxStacks: 5, damagePerSecond: 5f, stacksDuration: true);

                case StatusEffectType.Bleed:
                    return Status("StatusBleed", "Bleeding", type, DamageType.Physical,
                        duration: 3f, maxStacks: 3, damagePerSecond: 9f);

                // Short on purpose, and protected for twice as long as it lasts.
                // With trigger gems in the game a generous stun is not a strong
                // build, it is a crowd that stops being a fight.
                case StatusEffectType.Stun:
                    return Status("StatusStun", "Stunned", type,
                        duration: 1.2f, immunityMultiplier: 2f);

                case StatusEffectType.Root:
                    return Status("StatusRoot", "Rooted", type,
                        duration: 1.6f, immunityMultiplier: 1.5f);

                case StatusEffectType.Silence:
                    return Status("StatusSilence", "Silenced", type,
                        duration: 2.5f, immunityMultiplier: 1.5f);

                case StatusEffectType.Fear:
                    return Status("StatusFear", "Feared", type,
                        duration: 2f, immunityMultiplier: 1.5f);

                // The one control with no immunity window, because being slower
                // is still playing. That is also what makes it the right thing
                // to hang a keystone on.
                case StatusEffectType.Slow:
                    return Status("StatusSlow", "Slowed", type,
                        duration: 3f, maxStacks: 3, magnitude: 0.2f);

                case StatusEffectType.Weaken:
                    return Status("StatusWeaken", "Weakened", type,
                        duration: 4f, maxStacks: 2, magnitude: 0.2f);

                case StatusEffectType.Vulnerable:
                    return Status("StatusVulnerable", "Vulnerable", type,
                        duration: 4f, maxStacks: 3, magnitude: 0.15f);

                case StatusEffectType.Haste:
                    return Status("StatusHaste", "Hastened", type,
                        duration: 4f, magnitude: 0.3f);

                default:
                    return Status("StatusFortify", "Fortified", StatusEffectType.Fortify,
                        duration: 4f, magnitude: 0.2f);
            }
        }

        private static StatusEffectDefinition Status(
            string assetName,
            string displayName,
            StatusEffectType type,
            DamageType element = DamageType.Physical,
            float duration = 4f,
            int maxStacks = 1,
            float damagePerSecond = 0f,
            float magnitude = 0f,
            bool stacksDuration = false,
            float immunityMultiplier = 0f)
        {
            StatusEffectDefinition status =
                SceneBuildUtility.CreateOrLoadConfig<StatusEffectDefinition>(
                    assetName, ElementFolder, out bool created);

            var serialized = new SerializedObject(status);

            // Identity, written every rebuild. Everything below it is balance
            // and is left alone once the asset exists.
            serialized.FindProperty("_type").enumValueIndex = (int)type;

            if (created)
            {
                serialized.FindProperty("_displayName").stringValue = displayName;
                serialized.FindProperty("_element").enumValueIndex = (int)element;
                serialized.FindProperty("_duration").floatValue = duration;
                serialized.FindProperty("_maxStacks").intValue = maxStacks;
                serialized.FindProperty("_stacksDuration").boolValue = stacksDuration;
                serialized.FindProperty("_damagePerSecond").floatValue = damagePerSecond;
                serialized.FindProperty("_tickInterval").floatValue = 0.5f;
                serialized.FindProperty("_magnitudePerStack").floatValue = magnitude;
                serialized.FindProperty("_immunityMultiplier").floatValue = immunityMultiplier;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return status;
        }

        private static ElementReactionRule Rule(
            string assetName,
            DamageType existing,
            DamageType incoming,
            ElementReactionKind result,
            float multiplier,
            bool consumes,
            float radius = 3.5f,
            StatusEffectDefinition resultStatus = null)
        {
            ElementReactionRule rule = SceneBuildUtility.CreateOrLoadConfig<ElementReactionRule>(
                assetName, ElementFolder, out bool created);

            if (!created)
                return rule;

            var serialized = new SerializedObject(rule);
            serialized.FindProperty("_existingElement").enumValueIndex = (int)existing;
            serialized.FindProperty("_incomingElement").enumValueIndex = (int)incoming;
            serialized.FindProperty("_result").enumValueIndex = (int)result;
            serialized.FindProperty("_damageMultiplier").floatValue = multiplier;
            serialized.FindProperty("_radius").floatValue = radius;
            serialized.FindProperty("_consumesExistingStatus").boolValue = consumes;
            serialized.FindProperty("_resultingStatus").objectReferenceValue = resultStatus;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return rule;
        }

        private static void WriteList(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
