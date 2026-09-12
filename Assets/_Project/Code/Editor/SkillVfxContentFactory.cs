using UnityEditor;
using UnityEngine;
using TogetherWeFall.Config;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Creates the visual sets and points the skills at them.
    ///
    /// Its own file for the reason every other content factory is one: what
    /// exists is a content decision, and it is the kind that changes when a pack
    /// is imported rather than when code is written. The prefab paths below are
    /// the one place in the project that names an asset store folder — replace
    /// that block and nothing else knows.
    ///
    /// Assets are filled in only when created, like everywhere else here:
    /// rebuilding a scene must never overwrite a set somebody has since pointed
    /// at their own prefabs.
    /// </summary>
    public static class SkillVfxContentFactory
    {
        private const string SetFolder = "Assets/_Project/Data/Vfx";
        private const string SkillFolder = "Assets/_Project/Data/Skills";

        /// <summary>
        /// The pack this project happens to have. One constant, so a different
        /// pack is a different string rather than a different factory.
        /// </summary>
        private const string Pack = "Assets/Lana Studio/Casual RPG VFX/Prefabs";

        /// <summary>
        /// Creates the sets that do not exist yet and assigns them to the skills
        /// that name no set.
        ///
        /// Called from the scene build, beside the presenter that has to list
        /// them: a set nobody points at draws nothing, and a set the presenter
        /// has never heard of draws nothing either, so the two halves belong in
        /// one call.
        /// </summary>
        public static void CreateOrLoadSets()
        {
            // skill asset, set name, cast, projectile, hit
            //
            // Three to start with, and the three the request named: a bow shot,
            // a fireball and a frost nova. Every other skill keeps the coloured
            // line and ring until somebody authors a set for it — which is one
            // asset and one field, with no code.
            Assign(
                "WeaponBolt",
                Set("VfxWeaponBolt",
                    cast: $"{Pack}/Burst/Flash_circle.prefab",
                    projectile: $"{Pack}/Range_attack/Projectiles_wind.prefab",
                    hit: $"{Pack}/Range_attack/Hit_wind.prefab"));

            Assign(
                "Firebolt",
                Set("VfxFirebolt",
                    cast: $"{Pack}/Burst/Flash_generic.prefab",
                    projectile: $"{Pack}/Range_attack/Projectiles_fire.prefab",
                    hit: $"{Pack}/Range_attack/Hit_fire.prefab"));

            Assign(
                "FrostNova",
                Set("VfxFrostNova",
                    // An area skill has nothing that flies: the cast IS the
                    // effect, and it goes off where the blast lands rather than
                    // in the caster's hand.
                    cast: $"{Pack}/Top_down_attack/top_down_ice_circle.prefab",
                    projectile: null,
                    hit: $"{Pack}/Range_attack/Hit_frost.prefab"));

            AssetDatabase.SaveAssets();
        }

        /// <summary>Every set on disk, for the presenter's list.</summary>
        public static SkillVfxSet[] LoadAll()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(SkillVfxSet)}");
            var sets = new SkillVfxSet[guids.Length];

            for (int i = 0; i < guids.Length; i++)
            {
                sets[i] = AssetDatabase.LoadAssetAtPath<SkillVfxSet>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            return sets;
        }

        private static SkillVfxSet Set(
            string assetName, string cast, string projectile, string hit)
        {
            SkillVfxSet set = SceneBuildUtility.CreateOrLoadConfig<SkillVfxSet>(
                assetName, SetFolder, out bool created);

            if (!created)
                return set;

            var serialized = new SerializedObject(set);
            serialized.FindProperty("_cast").objectReferenceValue = Prefab(cast);
            serialized.FindProperty("_projectile").objectReferenceValue = Prefab(projectile);
            serialized.FindProperty("_hit").objectReferenceValue = Prefab(hit);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return set;
        }

        /// <summary>
        /// One prefab by path, or null with a word about it.
        ///
        /// Warned rather than thrown: a pack that is not in the project is a
        /// set with empty slots, which draws nothing and breaks nothing — and
        /// the message is what tells somebody why their fireball is invisible.
        /// </summary>
        private static GameObject Prefab(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[{nameof(SkillVfxContentFactory)}] No prefab at '{path}'. The visual " +
                    "set will have an empty slot — assign one by hand, or import the pack.");
            }

            return prefab;
        }

        /// <summary>
        /// Points a skill at a set, unless it already names one.
        ///
        /// Never overwrites, for the reason the welded attacks are not
        /// overwritten either: the field is there to be edited by hand, and a
        /// factory that reverted that would be worse than no factory.
        /// </summary>
        private static void Assign(string skillAsset, SkillVfxSet set)
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>(
                $"{SkillFolder}/{skillAsset}.asset");

            if (skill == null || set == null)
                return;

            var serialized = new SerializedObject(skill);
            SerializedProperty field = serialized.FindProperty("_vfx");

            if (field.objectReferenceValue != null)
                return;

            field.objectReferenceValue = set;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
