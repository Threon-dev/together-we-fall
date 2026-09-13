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

        /// <summary>
        /// The pack this project happens to have. One constant, so a different
        /// pack is a different string rather than a different factory.
        /// </summary>
        internal const string Pack = "Assets/Lana Studio/Casual RPG VFX/Prefabs";

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
            // Only the first three are generated. The sets for every other
            // skill were authored straight into Data/Vfx, which is how a new
            // one is added too: one asset and one field, with no code.
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

            // The arrow in flight is the arrow model itself, which the weapon
            // factory writes before this runs. No flash at the bow, and no hit
            // effect: the arrow stays where it struck for the release fade,
            // which says "hit" better than a puff would.
            Assign(
                "WeaponArrow",
                Set("VfxWeaponArrow",
                    cast: null,
                    projectile: WeaponContentFactory.ArrowModelPath,
                    hit: null));

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

        internal static SkillVfxSet Set(
            string assetName, string cast, string projectile, string hit,
            float scale = 1f, float lift = 0f, float yaw = 0f)
        {
            SkillVfxSet set = SceneBuildUtility.CreateOrLoadConfig<SkillVfxSet>(
                assetName, SetFolder, out bool created);

            if (!created)
                return set;

            var serialized = new SerializedObject(set);
            serialized.FindProperty("_cast").objectReferenceValue = Prefab(cast);
            serialized.FindProperty("_projectile").objectReferenceValue = Prefab(projectile);
            serialized.FindProperty("_hit").objectReferenceValue = Prefab(hit);
            serialized.FindProperty("_scale").floatValue = scale;
            serialized.FindProperty("_lift").floatValue = lift;
            serialized.FindProperty("_yaw").floatValue = yaw;
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
            var skill = SceneBuildUtility.LoadConfig<SkillDefinition>(skillAsset);

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
