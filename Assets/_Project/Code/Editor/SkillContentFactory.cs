using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;
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

        /// <summary>The skills, in loadout order: primary, secondary, and two more.</summary>
        public static SkillDefinition[] CreateStarterSkills()
        {
            SkillModifier fork = Modifier("SupportFork", SkillModifierKind.Fork, 1f);
            SkillModifier multicast = Modifier("SupportMulticast", SkillModifierKind.Multicast, 2f);
            SkillModifier area = Modifier("SupportGreaterArea", SkillModifierKind.IncreasedArea, 40f);
            SkillModifier chain = Modifier("SupportChain", SkillModifierKind.AddedChains, 3f);
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
                    skill.Range = 18f;
                    skill.Radius = 4f;
                    skill.Modifiers = new[] { area, explode };
                });

            SkillDefinition sweep =
                Skill("ReavingSweep", "Reaving Sweep", skill =>
                {
                    skill.Effect = SkillEffectKind.MeleeArc;
                    skill.DamageType = DamageType.Physical;
                    skill.BaseDamage = 34f;
                    skill.Cooldown = 0.6f;
                    skill.Range = 5f;
                    skill.Radius = 4.5f;
                    skill.ArcDegrees = 140f;
                    skill.Modifiers = new[] { brutality };
                });

            // The only skill that leaves something behind. It exists to be flown
            // through: a wall of fire is where a projectile picks an element up,
            // and without one in the game the carried half of the reaction system
            // has nothing to demonstrate itself with.
            SkillDefinition wall =
                Skill("CinderWall", "Cinder Wall", skill =>
                {
                    skill.Effect = SkillEffectKind.PersistentZone;
                    skill.DamageType = DamageType.Fire;

                    // Per pulse, not per cast. Low on purpose: what a zone is for
                    // is the ignite it keeps applying and the element it hands
                    // out, not the damage.
                    skill.BaseDamage = 9f;
                    skill.Cooldown = 4f;
                    skill.Range = 14f;
                    skill.Radius = 3f;
                    skill.ZoneDuration = 6f;
                    skill.ZoneTickInterval = 0.5f;

                    // No supports. Increased area is the one that reads on a zone
                    // and it is a gem the player can socket themselves — which is
                    // the whole point of gems being the build.
                    skill.Modifiers = System.Array.Empty<SkillModifier>();
                });

            return new[] { splinter, nova, lance, sweep, wall };
        }

        private static SkillModifier Modifier(
            string assetName,
            SkillModifierKind kind,
            float value,
            float secondary = 60f,
            DamageType convertTo = DamageType.Fire,
            SkillDefinition triggered = null)
        {
            SkillModifier modifier = SceneBuildUtility.CreateOrLoadConfig<SkillModifier>(
                assetName, SkillFolder, out bool created);

            if (!created)
                return modifier;

            var serialized = new SerializedObject(modifier);
            serialized.FindProperty("_kind").enumValueIndex = (int)kind;
            serialized.FindProperty("_value").floatValue = value;
            serialized.FindProperty("_secondaryValue").floatValue = secondary;
            serialized.FindProperty("_convertTo").enumValueIndex = (int)convertTo;
            serialized.FindProperty("_triggeredSkill").objectReferenceValue = triggered;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return modifier;
        }

        private static SkillDefinition Skill(
            string assetName, string displayName, System.Action<SkillFields> fill)
        {
            SkillDefinition skill = SceneBuildUtility.CreateOrLoadConfig<SkillDefinition>(
                assetName, SkillFolder, out bool created);

            if (!created)
                return skill;

            var fields = new SkillFields();
            fill(fields);

            var serialized = new SerializedObject(skill);
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_effect").enumValueIndex = (int)fields.Effect;
            serialized.FindProperty("_damageType").enumValueIndex = (int)fields.DamageType;
            serialized.FindProperty("_baseDamage").floatValue = fields.BaseDamage;
            serialized.FindProperty("_cooldown").floatValue = fields.Cooldown;
            serialized.FindProperty("_range").floatValue = fields.Range;
            serialized.FindProperty("_radius").floatValue = fields.Radius;
            serialized.FindProperty("_arcDegrees").floatValue = fields.ArcDegrees;
            serialized.FindProperty("_projectileSpeed").floatValue = fields.ProjectileSpeed;
            serialized.FindProperty("_zoneDuration").floatValue = fields.ZoneDuration;
            serialized.FindProperty("_zoneTickInterval").floatValue = fields.ZoneTickInterval;
            serialized.FindProperty("_baseChains").intValue = fields.BaseChains;
            serialized.FindProperty("_chainRange").floatValue = fields.ChainRange;
            serialized.FindProperty("_chainDelay").floatValue = fields.ChainDelay;

            SerializedProperty modifiers = serialized.FindProperty("_modifiers");
            modifiers.arraySize = fields.Modifiers.Length;

            for (int i = 0; i < fields.Modifiers.Length; i++)
                modifiers.GetArrayElementAtIndex(i).objectReferenceValue = fields.Modifiers[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return skill;
        }

        /// <summary>
        /// A plain bag of the values a skill asset holds.
        ///
        /// It exists so the table above reads as a table. Writing twelve
        /// SerializedProperty lines per skill inline would bury which numbers
        /// were chosen under how they are stored.
        /// </summary>
        private sealed class SkillFields
        {
            public SkillEffectKind Effect = SkillEffectKind.Projectile;
            public DamageType DamageType = DamageType.Physical;
            public float BaseDamage = 20f;
            public float Cooldown = 0.5f;
            public float Range = 20f;
            public float Radius;
            public float ArcDegrees = 360f;
            public float ProjectileSpeed = 26f;
            public float ZoneDuration = 5f;
            public float ZoneTickInterval = 0.5f;
            public int BaseChains;
            public float ChainRange = 8f;
            public float ChainDelay = 0.07f;
            public SkillModifier[] Modifiers = System.Array.Empty<SkillModifier>();
        }
    }
}
