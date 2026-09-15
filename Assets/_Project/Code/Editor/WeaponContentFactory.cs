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
    /// Weapons from the store packs, as items with a body: the swords from the
    /// two low-poly sword packs, and the bows, quivers and arrow from the
    /// Dimasjk bow pack.
    ///
    /// Each model gets a grip prefab in Art/Weapons: its origin is where the
    /// hand closes (or, for what is not held, where it is mounted), its long
    /// axis runs along +Z and its width along +Y. That is the one convention
    /// PlayerAnimationPresenter and the arrow's flight rely on, so anything from
    /// any pack sits the same way.
    ///
    /// The packs agree on nothing — not the axis a thing lies along, not which
    /// end is which, not the unit — so the grip is measured off the mesh, by
    /// shape. The longest axis is always the long one; what differs is where
    /// the grip is and which end leads (see Measure). Good for these shapes;
    /// wrong for anything shaped otherwise. A prefab is only written once, so a
    /// grip nudged by hand survives the next build.
    ///
    /// Items follow the other factories: filled only when created. The
    /// exceptions are the body, the back model and the icon, written wherever
    /// they are still empty — which is how items that existed before a pack did
    /// get one without their numbers being touched.
    /// </summary>
    public static class WeaponContentFactory
    {
        private const string FreePack = "Assets/Free Low Poly Fantasy Swords/Prefabs";
        private const string LowPolyPack = "Assets/Low Poly Fantasy Swords/Prefabs/Prefabs";
        private const string BowPack = "Assets/Dimasjk Studio/Weapons/Low Poly Style Bows & Quivers Part 1";

        private const string ModelFolder = SceneBuildUtility.ArtFolder + "/Weapons";
        private const string ItemFolder = SceneBuildUtility.DataFolder + "/Items/Weapons";

        /// <summary>The arrow a Weapon Arrow flies as. SkillVfxContentFactory points its set here.</summary>
        public const string ArrowModelPath = ModelFolder + "/Arrow_01.prefab";

        /// <summary>What a pack's typical sword measures once scaled, in metres.</summary>
        private const float TypicalSwordLength = 0.9f;

        /// <summary>This much longer than the pack's typical sword needs both hands.</summary>
        private const float TwoHandedRatio = 1.25f;

        // The rest are scaled to a length of their own: a pack holds one of each,
        // so there is no typical one to measure against.
        private const float BowLength = 1.2f;
        private const float QuiverLength = 0.7f;
        private const float ArrowLength = 0.8f;

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

        /// <summary>The pack numbers its sets; each is a bow, its string, a quiver and an arrow.</summary>
        private static readonly (string set, string display, ItemRarity rarity)[] Bows =
        {
            // Older than the pack: it gets a body and keeps everything else.
            ("01", "Hunters Bow", ItemRarity.Uncommon),
            ("04", "Elderwood Bow", ItemRarity.Rare)
        };

        private enum Shape
        {
            Sword,
            Bow,
            Quiver,
            Arrow
        }

        private struct Grip
        {
            public Vector3 Point;
            public Vector3 Blade;
            public Vector3 Edge;
            public float Length;
        }

        // ─────────────────────────────────────────────────────────────────
        // Swords
        // ─────────────────────────────────────────────────────────────────

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

                Grip grip = Measure(Shape.Sword, prefab);
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

                GameObject model = CreateModel(prefab.name, new[] { prefab }, grip, TypicalSwordLength / packLength);
                ItemDefinition item = CreateSwordItem(index, twoHanded, strike, model);

                SceneBuildUtility.EnsureInLootTable(table, item);
            }

            // The blink and the dash, on every sword in the folder rather than
            // only the ones this pack made — the hand-authored ones are swords
            // too. Filled only when empty, so a sword somebody gave other skills
            // keeps them.
            SkillDefinition blink = SkillContentFactory.CreateBlinkStrike();
            SkillDefinition dash = SkillContentFactory.CreateDashStrike();

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemFolder + "/Swords" }))
            {
                var sword = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (sword == null)
                    continue;

                var serialized = new SerializedObject(sword);
                SerializedProperty signatures = serialized.FindProperty("_signatureSkills");

                if (signatures.arraySize == 0)
                {
                    signatures.arraySize = 2;
                    signatures.GetArrayElementAtIndex(0).objectReferenceValue = blink;
                    signatures.GetArrayElementAtIndex(1).objectReferenceValue = dash;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static ItemDefinition CreateSwordItem(int index, bool twoHanded, SkillDefinition strike, GameObject model)
        {
            var (_, _, display, rarity) = Swords[index];

            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                display.Replace(" ", string.Empty), ItemFolder + "/Swords", out bool created);

            var serialized = new SerializedObject(item);
            FillIfEmpty(serialized, "_model", model);

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
                Weld(serialized, strike);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        // ─────────────────────────────────────────────────────────────────
        // Bows
        // ─────────────────────────────────────────────────────────────────

        public static void CreateBows(LootTable table)
        {
            SkillDefinition arrow = SkillContentFactory.CreateArrowAttack();

            foreach ((string set, string display, ItemRarity rarity) in Bows)
            {
                GameObject bow = LoadBowPart(set, $"Bow_{set}");
                if (bow == null)
                    continue;

                // The pack keeps the string apart from the bow. Both come out
                // of one mesh file and share its origin, so side by side they
                // are the bow as it is strung.
                GameObject bowModel = CreateModel($"Bow_{set}", Shape.Bow, BowLength, bow, LoadBowPart(set, $"Bowstring_{set}"));

                GameObject quiver = LoadBowPart(set, $"Quiver_{set}");
                GameObject quiverModel = quiver != null
                    ? CreateModel($"Quiver_{set}", Shape.Quiver, QuiverLength, quiver)
                    : null;

                // The pack draws its bows strung, under the string's name.
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{BowPack}/Icons/Bow_{set}/Bowstring_{set}_Icon.png");

                ItemDefinition item = CreateBowItem(display, rarity, arrow, bowModel, quiverModel, icon);
                SceneBuildUtility.EnsureInLootTable(table, item);
            }

            // One arrow for the one arrow skill: the look belongs to the skill,
            // not to the bow that fired it.
            GameObject arrowPrefab = LoadBowPart("01", "Arrow_01");
            if (arrowPrefab != null)
                CreateModel("Arrow_01", Shape.Arrow, ArrowLength, arrowPrefab);
        }

        private static GameObject LoadBowPart(string set, string prefab)
            => AssetDatabase.LoadAssetAtPath<GameObject>($"{BowPack}/Prefabs/Bow_{set}/{prefab}.prefab");

        private static ItemDefinition CreateBowItem(
            string display, ItemRarity rarity, SkillDefinition arrow, GameObject model, GameObject quiver, Sprite icon)
        {
            ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                display.Replace(" ", string.Empty), ItemFolder + "/Bows", out bool created);

            var serialized = new SerializedObject(item);

            // The stance goes with the body: an item that already had a body
            // chosen by hand keeps the stance chosen with it.
            SerializedProperty modelProperty = serialized.FindProperty("_model");
            if (modelProperty.objectReferenceValue == null && model != null)
            {
                modelProperty.objectReferenceValue = model;
                serialized.FindProperty("_stance").enumValueIndex = (int)WeaponStance.Bow;
            }

            FillIfEmpty(serialized, "_backModel", quiver);
            FillIfEmpty(serialized, "_icon", icon);

            if (created)
            {
                int tier = (int)rarity;

                serialized.FindProperty("_displayName").stringValue = display;
                serialized.FindProperty("_rarity").enumValueIndex = tier;
                serialized.FindProperty("_slot").enumValueIndex = (int)EquipmentSlot.MainHand;
                serialized.FindProperty("_isTwoHanded").boolValue = true;

                serialized.FindProperty("_gridWidth").intValue = 2;
                serialized.FindProperty("_gridHeight").intValue = 3;
                serialized.FindProperty("_canRotate").boolValue = true;

                ItemContentFactory.WriteStats(
                    serialized.FindProperty("_baseStats"),
                    new[] { new ItemStatValue(StatKind.Damage, 12f + tier * 8f) });

                ItemContentFactory.WriteAffixes(serialized.FindProperty("_affixes"), new[]
                {
                    new ItemAffix(StatKind.Damage, ModifierKind.Increased, 10f + tier * 8f),
                    new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 10f + tier * 5f)
                });

                Weld(serialized, arrow);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        // ─────────────────────────────────────────────────────────────────
        // Grip prefabs
        // ─────────────────────────────────────────────────────────────────

        private static GameObject CreateModel(string name, Shape shape, float length, params GameObject[] parts)
        {
            parts = parts.Where(p => p != null).ToArray();

            Grip grip = Measure(shape, parts);
            return grip.Length > 0f ? CreateModel(name, parts, grip, length / grip.Length) : null;
        }

        private static GameObject CreateModel(string name, GameObject[] parts, Grip grip, float scale)
        {
            SceneBuildUtility.EnsureAssetFolder(ModelFolder);
            string path = $"{ModelFolder}/{name}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var root = new GameObject(name);

            // The measured grip goes to the origin, the long axis to +Z, the
            // width to +Y. On a child, so the root stays an identity that can be
            // placed.
            var pose = new GameObject("Pose").transform;
            pose.SetParent(root.transform, worldPositionStays: false);

            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(grip.Blade, grip.Edge));
            pose.localRotation = rotation;
            pose.localScale = Vector3.one * scale;
            pose.localPosition = -(rotation * (grip.Point * scale));

            foreach (GameObject part in parts)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(part, pose);

                // Unpacked, so the swapped materials and removed colliders are
                // the prefab's own rather than overrides on a nested one.
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                CharacterContentFactory.ConvertMaterials(model, ModelFolder + "/Materials");

                // Nothing in this project collides with GameObjects, and a
                // collider in the hand would shove the CharacterController it is
                // attached to — or stop the character's own arrows.
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(includeInactive: true))
                    Object.DestroyImmediate(collider);
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// Where a model is held and which way it points, read off its mesh.
        ///
        /// The longest axis is the long one for every shape here, and the
        /// second longest its width. The rest is per shape:
        ///
        /// - Sword: the widest slice across the blade is the guard, the handle
        ///   is the shorter side of it, and the hand closes just below the guard.
        /// - Bow: held in the middle, by the riser. The string is a handful of
        ///   vertices and the riser many, so the middle's centre of mass sits on
        ///   the riser side — +Y, the side that faces away from the archer.
        /// - Quiver: mounted by its middle, mouth along +Z — the busier end,
        ///   where the fletchings or the rim are.
        /// - Arrow: head along +Z, and the head is the slimmer end: the
        ///   fletchings are wide for longer than the point is.
        /// </summary>
        private static Grip Measure(Shape shape, params GameObject[] parts)
        {
            var points = new List<Vector3>();

            // An asset's root has no parent, so its local-to-world is the
            // prefab's own space — the space the grip prefab will reproduce.
            foreach (GameObject part in parts)
            {
                foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(includeInactive: true))
                {
                    if (filter.sharedMesh == null)
                        continue;

                    Matrix4x4 toRoot = filter.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                        points.Add(toRoot.MultiplyPoint3x4(vertex));
                }
            }

            if (points.Count == 0)
            {
                Debug.LogWarning($"[WeaponContentFactory] '{parts.FirstOrDefault()?.name}' has no mesh to measure; skipped.");
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
            const int Quarter = Slices / 4;

            var low = Enumerable.Repeat(new Vector2(float.MaxValue, float.MaxValue), Slices).ToArray();
            var high = Enumerable.Repeat(new Vector2(float.MinValue, float.MinValue), Slices).ToArray();
            var count = new int[Slices];
            var sum = new Vector2[Slices];

            foreach (Vector3 point in points)
            {
                int slice = Mathf.Clamp((int)((point[along] - min) / length * Slices), 0, Slices - 1);
                var flat = new Vector2(point[across], point[through]);

                low[slice] = Vector2.Min(low[slice], flat);
                high[slice] = Vector2.Max(high[slice], flat);
                count[slice]++;
                sum[slice] += flat;
            }

            float Width(int s) => high[s].x < low[s].x ? 0f : Mathf.Max(high[s].x - low[s].x, high[s].y - low[s].y);

            Vector3 grip = bounds.center;
            Vector3 blade = Vector3.zero;
            Vector3 edge = Vector3.zero;
            edge[across] = 1f;

            switch (shape)
            {
                case Shape.Sword:
                {
                    int guard = Enumerable.Range(0, Slices).OrderByDescending(s => Width(s)).First();

                    float guardAt = min + (guard + 0.5f) * length / Slices;
                    bool handleAtMin = guardAt - min < min + length - guardAt;
                    float pommel = handleAtMin ? min : min + length;

                    grip[along] = Mathf.Lerp(guardAt, pommel, 0.4f);
                    blade[along] = handleAtMin ? 1f : -1f;
                    break;
                }

                case Shape.Bow:
                {
                    int a = Slices / 2 - 1, b = Slices / 2;
                    int middle = count[a] + count[b];

                    if (middle > 0)
                    {
                        Vector2 centre = (sum[a] + sum[b]) / middle;
                        grip[across] = centre.x;
                        grip[through] = centre.y;
                    }

                    blade[along] = 1f;
                    edge[across] = grip[across] >= bounds.center[across] ? 1f : -1f;
                    break;
                }

                case Shape.Quiver:
                {
                    int lowEnd = count.Take(Quarter).Sum();
                    int highEnd = count.Skip(Slices - Quarter).Sum();

                    blade[along] = highEnd >= lowEnd ? 1f : -1f;
                    break;
                }

                case Shape.Arrow:
                {
                    float lowWidth = Enumerable.Range(0, Quarter).Average(s => Width(s));
                    float highWidth = Enumerable.Range(Slices - Quarter, Quarter).Average(s => Width(s));

                    blade[along] = highWidth <= lowWidth ? 1f : -1f;
                    break;
                }
            }

            return new Grip { Point = grip, Blade = blade, Edge = edge, Length = length };
        }

        // ─────────────────────────────────────────────────────────────────

        private static void FillIfEmpty(SerializedObject serialized, string field, Object value)
        {
            SerializedProperty property = serialized.FindProperty(field);

            if (property.objectReferenceValue == null && value != null)
                property.objectReferenceValue = value;
        }

        private static void Weld(SerializedObject serialized, SkillDefinition skill)
        {
            SerializedProperty innate = serialized.FindProperty("_innateSkills");
            innate.arraySize = 1;
            innate.GetArrayElementAtIndex(0).objectReferenceValue = skill;
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            return sorted[sorted.Length / 2];
        }
    }
}
