using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TogetherWeFall.Config;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The swords from the two low-poly packs, as items with a body.
    ///
    /// Each model gets a grip prefab in Art/Weapons: its origin is where the
    /// hand closes, the blade runs along +Z and its width along +Y. That is the
    /// one convention PlayerAnimationPresenter relies on, so a sword from any
    /// pack sits in the hand the same way.
    ///
    /// The packs agree on nothing — not the axis the blade lies along, not
    /// which end is the handle, not the unit — so the grip is measured off the
    /// mesh: the longest axis is the blade, the widest slice across it is the
    /// guard, and the handle is the shorter side of the guard. Good for a sword;
    /// wrong for anything shaped otherwise. The prefab is only written once, so
    /// a grip nudged by hand survives the next build.
    ///
    /// Items follow the other factories: filled only when created. The one
    /// exception is the model, written wherever it is still empty — which is how
    /// the two swords that existed before this got a body.
    /// </summary>
    public static class WeaponContentFactory
    {
        private const string FreePack = "Assets/Free Low Poly Fantasy Swords/Prefabs";
        private const string LowPolyPack = "Assets/Low Poly Fantasy Swords/Prefabs/Prefabs";

        private const string ModelFolder = SceneBuildUtility.ArtFolder + "/Weapons";
        private const string ItemFolder = SceneBuildUtility.DataFolder + "/Items/Weapons/Swords";

        /// <summary>What a pack's typical sword measures once scaled, in metres.</summary>
        private const float TypicalLength = 0.9f;

        /// <summary>This much longer than the pack's typical sword needs both hands.</summary>
        private const float TwoHandedRatio = 1.25f;

        private static readonly (string pack, string prefab, string display, ItemRarity rarity)[] Swords =
        {
            (FreePack, "iron_sword", "Iron Sword", ItemRarity.Common),
            (FreePack, "iron_sword_2", "Iron Longsword", ItemRarity.Common),
            (FreePack, "iron_sword_3", "Iron Sabre", ItemRarity.Uncommon),
            (FreePack, "bw_sword", "Ashen Blade", ItemRarity.Uncommon),
            (FreePack, "emerald_sword", "Emerald Edge", ItemRarity.Rare),
            (FreePack, "gold_sword", "Gilded Sword", ItemRarity.Rare),
            (FreePack, "gold_sword_2", "Gilded Longsword", ItemRarity.Epic),
            (FreePack, "rose_sword", "Rosethorn", ItemRarity.Epic),

            // The two swords that already existed, as names without a body.
            (FreePack, "ice_sword", "Frostbite Blade", ItemRarity.Rare),
            (FreePack, "big_sword", "Dawnbringer", ItemRarity.Legendary),

            (LowPolyPack, "WoodenSword", "Wooden Sword", ItemRarity.Common),
            (LowPolyPack, "BoneSword", "Bone Sword", ItemRarity.Uncommon),
            (LowPolyPack, "DarkIronSword", "Dark Iron Sword", ItemRarity.Uncommon),
            (LowPolyPack, "PunkSword", "Scrapper", ItemRarity.Rare),
            (LowPolyPack, "KatsunesiSword", "Katsunesi", ItemRarity.Rare),
            (LowPolyPack, "DeadSword", "Gravewhisper", ItemRarity.Epic),
            (LowPolyPack, "DemonicSword", "Demonic Sword", ItemRarity.Epic),
            (LowPolyPack, "DragonicSword", "Dragonic Sword", ItemRarity.Legendary),
            (LowPolyPack, "ArtifactSword", "Artifact Sword", ItemRarity.Legendary)
        };

        private struct Grip
        {
            public Vector3 Point;
            public Vector3 Blade;
            public Vector3 Edge;
            public float Length;
        }

        public static void CreateSwords(LootTable table)
        {
            SkillContentFactory.CreateDefaultAttacks(out SkillDefinition strike, out _);

            var measured = new List<(int index, GameObject prefab, Grip grip)>();

            for (int i = 0; i < Swords.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Swords[i].pack}/{Swords[i].prefab}.prefab");

                // A pack that is not imported is not an error: the items it
                // would have made simply do not exist.
                if (prefab == null)
                    continue;

                Grip grip = Measure(prefab);
                if (grip.Length > 0f)
                    measured.Add((i, prefab, grip));
            }

            // One scale per pack rather than per sword, so a greatsword stays
            // longer than the arming sword beside it — which is also what says
            // it needs both hands.
            Dictionary<string, float> typical = measured
                .GroupBy(m => Swords[m.index].pack)
                .ToDictionary(g => g.Key, g => Median(g.Select(m => m.grip.Length)));

            foreach ((int index, GameObject prefab, Grip grip) in measured)
            {
                float packLength = typical[Swords[index].pack];
                bool twoHanded = grip.Length >= packLength * TwoHandedRatio;

                GameObject model = CreateModel(prefab, grip, TypicalLength / packLength);
                ItemDefinition item = CreateItem(index, twoHanded, strike, model);

                SceneBuildUtility.EnsureInLootTable(table, item);
            }
        }

        private static Grip Measure(GameObject prefab)
        {
            var points = new List<Vector3>();

            // The asset's root has no parent, so its local-to-world is the
            // prefab's own space — the space the grip prefab will reproduce.
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (filter.sharedMesh == null)
                    continue;

                Matrix4x4 toRoot = filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    points.Add(toRoot.MultiplyPoint3x4(vertex));
            }

            if (points.Count == 0)
            {
                Debug.LogWarning($"[WeaponContentFactory] '{prefab.name}' has no mesh to measure; skipped.");
                return default;
            }

            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points)
                bounds.Encapsulate(point);

            Vector3 size = bounds.size;
            int[] axes = new[] { 0, 1, 2 }.OrderByDescending(a => size[a]).ToArray();
            int along = axes[0], across = axes[1], through = axes[2];

            float min = bounds.min[along];
            float length = size[along];

            const int Slices = 24;
            var low = Enumerable.Repeat(new Vector2(float.MaxValue, float.MaxValue), Slices).ToArray();
            var high = Enumerable.Repeat(new Vector2(float.MinValue, float.MinValue), Slices).ToArray();

            foreach (Vector3 point in points)
            {
                int slice = Mathf.Clamp((int)((point[along] - min) / length * Slices), 0, Slices - 1);
                var flat = new Vector2(point[across], point[through]);
                low[slice] = Vector2.Min(low[slice], flat);
                high[slice] = Vector2.Max(high[slice], flat);
            }

            int guard = 0;
            float widest = -1f;

            for (int s = 0; s < Slices; s++)
            {
                if (high[s].x < low[s].x)
                    continue;

                float width = Mathf.Max(high[s].x - low[s].x, high[s].y - low[s].y);
                if (width > widest)
                {
                    widest = width;
                    guard = s;
                }
            }

            float guardAt = min + (guard + 0.5f) * length / Slices;
            bool handleAtMin = guardAt - min < min + length - guardAt;
            float pommel = handleAtMin ? min : min + length;

            // Just below the guard, where the index finger closes.
            Vector3 grip = bounds.center;
            grip[along] = Mathf.Lerp(guardAt, pommel, 0.4f);

            Vector3 blade = Vector3.zero;
            blade[along] = handleAtMin ? 1f : -1f;

            Vector3 edge = Vector3.zero;
            edge[across] = 1f;

            return new Grip { Point = grip, Blade = blade, Edge = edge, Length = length };
        }

        private static GameObject CreateModel(GameObject prefab, Grip grip, float scale)
        {
            SceneBuildUtility.EnsureAssetFolder(ModelFolder);
            string path = $"{ModelFolder}/{prefab.name}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var root = new GameObject(prefab.name);

            // The measured grip goes to the origin, the blade to +Z, the width
            // to +Y. On a child, so the root stays an identity the presenter can
            // place.
            var pose = new GameObject("Pose").transform;
            pose.SetParent(root.transform, worldPositionStays: false);

            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(grip.Blade, grip.Edge));
            pose.localRotation = rotation;
            pose.localScale = Vector3.one * scale;
            pose.localPosition = -(rotation * (grip.Point * scale));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pose);

            // Unpacked, so the swapped materials and removed colliders are the
            // prefab's own rather than overrides on a nested one.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            CharacterContentFactory.ConvertMaterials(model, ModelFolder + "/Materials");

            // Nothing in this project collides with GameObjects, and a collider
            // in the hand would shove the CharacterController it is attached to.
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(includeInactive: true))
                Object.DestroyImmediate(collider);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static ItemDefinition CreateItem(int index, bool twoHanded, SkillDefinition strike, GameObject model)
        {
            var (_, _, display, rarity) = Swords[index];

            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                display.Replace(" ", string.Empty), ItemFolder, out bool created);

            var serialized = new SerializedObject(item);

            SerializedProperty modelProperty = serialized.FindProperty("_model");
            if (modelProperty.objectReferenceValue == null)
                modelProperty.objectReferenceValue = model;

            if (created)
            {
                // Placeholders that climb with rarity, like every number in
                // these factories. Two hands hit harder and swing the same.
                int tier = (int)rarity;

                serialized.FindProperty("_displayName").stringValue = display;
                serialized.FindProperty("_rarity").enumValueIndex = tier;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_isTwoHanded").boolValue = twoHanded;

                serialized.FindProperty("_gridWidth").intValue = 1;
                serialized.FindProperty("_gridHeight").intValue = twoHanded ? 4 : 3;
                serialized.FindProperty("_canRotate").boolValue = true;

                ItemContentFactory.WriteStats(
                    serialized.FindProperty("_baseStats"),
                    new[] { new ItemStatValue(StatKind.Damage, (10f + tier * 7f) * (twoHanded ? 1.6f : 1f)) });

                var affixes = new List<ItemAffix> { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 10f + tier * 8f) };
                if (tier > 0)
                    affixes.Add(new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 5f * tier));

                ItemContentFactory.WriteAffixes(serialized.FindProperty("_affixes"), affixes.ToArray());

                SerializedProperty innate = serialized.FindProperty("_innateSkills");
                innate.arraySize = 1;
                innate.GetArrayElementAtIndex(0).objectReferenceValue = strike;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            return sorted[sorted.Length / 2];
        }
    }
}
