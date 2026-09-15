using UnityEditor;
using UnityEngine;
using TogetherWeFall.Combat;
using TogetherWeFall.Config;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Creates the visual sets and points the skills at them.
    ///
    /// Its own file for the reason every other content factory is one: what
    /// exists is a content decision, and it is the kind that changes when a pack
    /// is imported rather than when code is written. The two constants below are
    /// the one place in the project that names the effect pack's folders — the
    /// arsenal builds its paths from them too, so a different pack is two
    /// strings and the paths that follow them.
    ///
    /// Assets are filled in only when created, like everywhere else here:
    /// rebuilding a scene must never overwrite a set somebody has since pointed
    /// at their own prefabs.
    /// </summary>
    public static class SkillVfxContentFactory
    {
        private const string SetFolder = "Assets/_Project/Data/Vfx";

        /// <summary>The effect pack this project happens to have.</summary>
        internal const string Pack = "Assets/Retro Arsenal/Prefabs";

        /// <summary>
        /// The same pack's loose clips. Its missiles and explosions carry their
        /// own sound, and a set uses that by default; these are for the prefabs
        /// that carry none — a muzzle, a nova, a slash — and for the generic cues.
        /// </summary>
        internal const string Sounds = "Assets/Retro Arsenal/Sound";

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
            // Only these are generated here, and the arsenal's in its own
            // factory. The sets for every other skill were authored straight
            // into Data/Vfx, which is how a new one is added too: one asset and
            // one field, with no code.
            Assign(
                "WeaponBolt",
                Set("VfxWeaponBolt",
                    cast: $"{Pack}/Combat/Muzzleflash/Sparkle/SparkleMuzzleBlue.prefab",
                    projectile: $"{Pack}/Combat/Missiles/BasicTiny/BasicTinyMissileBlue.prefab",
                    hit: $"{Pack}/Combat/Explosions/BasicTiny/BasicTinyExplosionBlue.prefab",
                    castSound: $"{Sounds}/Shoot/retro_shoot_magic.wav",
                    hitSound: $"{Sounds}/Explosion/retro_explosion_small.wav"));

            // The missile and the explosion carry the launch and the bang.
            Assign(
                "Firebolt",
                Set("VfxFirebolt",
                    cast: $"{Pack}/Combat/Muzzleflash/Fire/FireMuzzle.prefab",
                    projectile: $"{Pack}/Combat/Missiles/Fireball/FireballMissile.prefab",
                    hit: $"{Pack}/Combat/Explosions/Fire/FireExplosion.prefab",
                    scale: 1.2f));

            Assign(
                "FrostNova",
                Set("VfxFrostNova",
                    // An area skill has nothing that flies: the cast IS the
                    // effect, and it goes off where the blast lands rather than
                    // in the caster's hand. The pack's rings are about a metre
                    // across, hence the scale.
                    cast: $"{Pack}/Combat/Magic/Nova/MagicNovaBlue.prefab",
                    projectile: null,
                    hit: $"{Pack}/Combat/Sword/SwordHit/SwordHitBlue.prefab",
                    scale: 3f,
                    castSound: $"{Sounds}/Explosion/retro_explosion_ice.wav"));

            // The arrow in flight is the arrow model itself, which the weapon
            // factory writes before this runs. No flash at the bow, and no hit
            // effect: the arrow stays where it struck for the release fade,
            // which says "hit" better than a puff would. Sounds only.
            Assign(
                "WeaponArrow",
                Set("VfxWeaponArrow",
                    cast: null,
                    projectile: WeaponContentFactory.ArrowModelPath,
                    hit: null,
                    castSound: $"{Sounds}/Shoot/retro_shoot_arrow.wav",
                    hitSound: $"{Sounds}/Explosion/retro_explosion_arrow.wav"));

            // A puff of dust where the dash leaves from. The blow at the end is
            // the primary skill's, and draws its own.
            Assign(
                "SteelRush",
                Set("VfxSteelRush",
                    cast: $"{Pack}/Interactive/Movement/JumpCloud.prefab",
                    projectile: null,
                    hit: null,
                    castSound: $"{Sounds}/Misc/retro_poof.wav"));

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Fills the config's per-element effects — chain beams, reaction bursts,
        /// blasts no skill authored — when its table is empty. Once it has rows
        /// the table is the designer's, the same rule as every set.
        /// </summary>
        public static void FillElementEffects(VfxConfig config)
        {
            if (config == null)
                return;

            var serialized = new SerializedObject(config);
            SerializedProperty elements = serialized.FindProperty("_elements");

            if (elements.arraySize > 0)
                return;

            // element, beam, reaction, blast. The colour variant follows
            // DamageTypePalette; the pack has no grey beam, so physical gets a laser.
            var table = new (DamageType element, string beam, string reaction, string blast)[]
            {
                (DamageType.Physical, "Combat/Beams/Laser/Setup/Beam/LaserBeamYellow",
                    "Combat/Explosions/- Misc/BasicSparkExplosion", "Combat/Explosions/Earth/EarthExplosion"),
                (DamageType.Fire, "Combat/Beams/Lightning/Setup/Beam/LightningBeamRed",
                    "Combat/Nova/Basic/NovaRed", "Combat/Explosions/Fire/FireExplosion"),
                (DamageType.Cold, "Combat/Beams/Lightning/Setup/Beam/LightningBeamBlue",
                    "Combat/Nova/Basic/NovaBlue", "Combat/Explosions/Frost/FrostExplosion"),
                (DamageType.Lightning, "Combat/Beams/Lightning/Setup/Beam/LightningBeamYellow",
                    "Combat/Nova/Basic/NovaYellow", "Combat/Explosions/Lightning/LightningExplosionYellow"),
                (DamageType.Chaos, "Combat/Beams/Lightning/Setup/Beam/LightningBeamPurple",
                    "Combat/Nova/Basic/NovaPurple", "Combat/Explosions/Poison/PoisonExplosionPurple")
            };

            elements.arraySize = table.Length;

            for (int i = 0; i < table.Length; i++)
            {
                SerializedProperty row = elements.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("_element").enumValueIndex = (int)table[i].element;
                row.FindPropertyRelative("_beam").objectReferenceValue = Load<GameObject>($"{Pack}/{table[i].beam}.prefab");
                row.FindPropertyRelative("_reaction").objectReferenceValue = Load<GameObject>($"{Pack}/{table[i].reaction}.prefab");
                row.FindPropertyRelative("_blast").objectReferenceValue = Load<GameObject>($"{Pack}/{table[i].blast}.prefab");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
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
            float scale = 1f, float lift = 0f, float yaw = 0f,
            string castSound = null, string hitSound = null)
        {
            SkillVfxSet set = SceneBuildUtility.CreateOrLoadConfig<SkillVfxSet>(
                assetName, SetFolder, out bool created);

            if (!created)
                return set;

            var serialized = new SerializedObject(set);
            serialized.FindProperty("_cast").objectReferenceValue = Load<GameObject>(cast);
            serialized.FindProperty("_projectile").objectReferenceValue = Load<GameObject>(projectile);
            serialized.FindProperty("_hit").objectReferenceValue = Load<GameObject>(hit);
            serialized.FindProperty("_scale").floatValue = scale;
            serialized.FindProperty("_lift").floatValue = lift;
            serialized.FindProperty("_yaw").floatValue = yaw;
            serialized.FindProperty("_castSound").objectReferenceValue = Load<AudioClip>(castSound);
            serialized.FindProperty("_hitSound").objectReferenceValue = Load<AudioClip>(hitSound);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return set;
        }

        /// <summary>
        /// One prefab or clip by path, or null with a word about it.
        ///
        /// Warned rather than thrown: a pack that is not in the project is a
        /// set with empty slots, which draws nothing and breaks nothing — and
        /// the message is what tells somebody why their fireball is invisible.
        /// </summary>
        private static T Load<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                Debug.LogWarning(
                    $"[{nameof(SkillVfxContentFactory)}] No {typeof(T).Name} at '{path}'. The visual " +
                    "set will have an empty slot — assign one by hand, or import the pack.");
            }

            return asset;
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
