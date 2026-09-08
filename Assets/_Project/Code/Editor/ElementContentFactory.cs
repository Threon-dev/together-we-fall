using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
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
            StatusEffectDefinition ignite = Status(
                "StatusIgnite", "Ignited", DamageType.Fire,
                duration: 4f, maxStacks: 3, damagePerSecond: 7f);

            StatusEffectDefinition shock = Status(
                "StatusShock", "Shocked", DamageType.Lightning,
                duration: 5f, maxStacks: 1, damagePerSecond: 0f);

            StatusEffectDefinition chill = Status(
                "StatusChill", "Chilled", DamageType.Cold,
                duration: 4f, maxStacks: 1, damagePerSecond: 0f);

            // Only ever produced by a reaction, never by a plain hit — which is
            // what makes it worth having: a burn that only a combination can
            // light, in an element nothing else in the game deals.
            StatusEffectDefinition scorch = Status(
                "StatusScorched", "Scorched", DamageType.Chaos,
                duration: 4f, maxStacks: 3, damagePerSecond: 11f);

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
                    resultStatus: scorch)
            };

            ElementReactionTable table =
                SceneBuildUtility.CreateOrLoadConfig<ElementReactionTable>(
                    "ElementReactionTable", ElementFolder, out _);

            var serialized = new SerializedObject(table);

            WriteList(serialized.FindProperty("_defaultStatuses"),
                new Object[] { ignite, shock, chill });

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
        {
            ItemDefinition gem = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                "GemCinderWall", ItemFolder, out bool created);

            if (created)
            {
                var serialized = new SerializedObject(gem);
                serialized.FindProperty("_displayName").stringValue = "Cinder Wall Gem";
                serialized.FindProperty("_rarity").enumValueIndex = (int)ItemRarity.Uncommon;

                // A gem is an ordinary item: it takes a cell in the bag, it drops,
                // and it is lost on death like everything else.
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_gemKind").enumValueIndex = (int)GemKind.Active;
                serialized.FindProperty("_gemSkill").objectReferenceValue = zoneSkill;
                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            SceneBuildUtility.EnsureInLootTable(table, gem);
            return gem;
        }

        private static StatusEffectDefinition Status(
            string assetName,
            string displayName,
            DamageType element,
            float duration,
            int maxStacks,
            float damagePerSecond)
        {
            StatusEffectDefinition status =
                SceneBuildUtility.CreateOrLoadConfig<StatusEffectDefinition>(
                    assetName, ElementFolder, out bool created);

            if (!created)
                return status;

            var serialized = new SerializedObject(status);
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_element").enumValueIndex = (int)element;
            serialized.FindProperty("_duration").floatValue = duration;
            serialized.FindProperty("_maxStacks").intValue = maxStacks;
            serialized.FindProperty("_damagePerSecond").floatValue = damagePerSecond;
            serialized.FindProperty("_tickInterval").floatValue = 0.5f;
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
