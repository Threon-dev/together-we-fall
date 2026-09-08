using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Config;
using TogetherWeFall.Dungeon;
using TogetherWeFall.Equipment;
using TogetherWeFall.Loot;
using TogetherWeFall.Loot.Authoring;
using TogetherWeFall.Player;
using TogetherWeFall.UI;
using TogetherWeFall.Audio;
using TogetherWeFall.Curtain;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Builds the procedural dungeon scene from scratch through a single menu
    /// item.
    ///
    /// The scene is almost empty on purpose: no floor, no walls, no spawn points
    /// and no baked navmesh, because all of it is produced at load time from a
    /// seed. What the scene does contain is the things that cannot be generated
    /// — the player, the camera, the debug overlay, and the SubScene objects
    /// that have to be baked into entities.
    ///
    /// The arena scene is left alone. It is the performance rig: an authored,
    /// unchanging floor is exactly what you want when the number you are reading
    /// has to be comparable between runs.
    /// </summary>
    public static class DungeonSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Dungeon.unity";
        private const string ItemFolder = "Assets/_Project/Data/Items";

        [MenuItem("Tools/Together We Fall/Build Dungeon Scene")]
        public static void BuildDungeonScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var enemyConfig = SceneBuildUtility.CreateOrLoadConfig<EnemyConfig>("EnemyConfig");
            var spawnConfig = SceneBuildUtility.CreateOrLoadConfig<SpawnConfig>("SpawnConfig");
            var pathfindingConfig =
                SceneBuildUtility.CreateOrLoadConfig<PathfindingConfig>("PathfindingConfig");
            var separationConfig =
                SceneBuildUtility.CreateOrLoadConfig<SeparationConfig>("SeparationConfig");
            var dungeonConfig =
                SceneBuildUtility.CreateOrLoadConfig<DungeonGenerationConfig>("DungeonGenerationConfig");
            var lootConfig = SceneBuildUtility.CreateOrLoadConfig<LootConfig>("LootConfig");
            var characterConfig =
                SceneBuildUtility.CreateOrLoadConfig<CharacterConfig>("CharacterConfig");
            var vfxConfig = SceneBuildUtility.CreateOrLoadConfig<VfxConfig>("VfxConfig");

            Material playerMaterial =
                SceneBuildUtility.CreateMaterial("PlayerBody", new Color(0.25f, 0.65f, 0.95f));
            Material enemyMaterial =
                SceneBuildUtility.CreateMaterial("EnemyBody", new Color(0.85f, 0.25f, 0.22f));
            Material chestMaterial =
                SceneBuildUtility.CreateMaterial("DungeonChest", new Color(0.52f, 0.38f, 0.18f));

            // Plain white: the drop system writes the rarity colour per item, so
            // any tint baked into the material would fight it.
            Material lootItemMaterial =
                SceneBuildUtility.CreateMaterial("LootItem", Color.white);

            // White as well, for the same reason: the cast writes the element
            // colour onto every projectile it fires.
            Material projectileMaterial =
                SceneBuildUtility.CreateMaterial("SkillProjectile", Color.white);

            GameObject enemyPrefab = SceneBuildUtility.CreateEnemyPrefab(enemyConfig, enemyMaterial);
            GameObject chestPrefab = SceneBuildUtility.CreateChestPrefab(lootConfig, chestMaterial);
            GameObject lootItemPrefab =
                SceneBuildUtility.CreateLootItemPrefab(lootConfig, lootItemMaterial);
            LootTable lootTable = CreateOrLoadTreasureTable();
            GameObject projectilePrefab =
                SceneBuildUtility.CreateProjectilePrefab(projectileMaterial);

            // White too: the pool writes the element colour onto every zone.
            GameObject zonePrefab = SceneBuildUtility.CreateZonePrefab(
                SceneBuildUtility.CreateMaterial("ElementZone", Color.white));

            SkillDefinition[] skills = SkillContentFactory.CreateStarterSkills();
            ElementReactionTable reactionTable = ElementContentFactory.CreateReactionTable();

            // The gem that casts the zone skill, added to the chest table if it
            // is not already there. Without it the zone skill exists in the
            // database and nothing can ever cast it.
            ElementContentFactory.CreateZoneGem(skills[skills.Length - 1], lootTable);

            // The trigger gem, the conditional support and the two keystone
            // rings, added to the same table for the same reason: a mechanism
            // that exists in the code and in no item is a mechanism nobody can
            // reach.
            SkillContentFactory.CreateBuildContent(lootTable);

            SceneBuildUtility.CreateLighting();

            DungeonDirector director = CreateDungeonRoot(dungeonConfig);

            GameObject waveSpawner = SceneBuildUtility.CreateWaveSpawner(enemyPrefab, spawnConfig);
            GameObject simulationSettings =
                SceneBuildUtility.CreateSimulationSettings(pathfindingConfig, separationConfig);
            GameObject lootDatabase =
                CreateLootDatabase(lootConfig, lootTable, chestPrefab, lootItemPrefab);
            GameObject characterStats =
                SceneBuildUtility.CreateCharacterStats(characterConfig);
            GameObject skillDatabase =
                SceneBuildUtility.CreateSkillDatabase(skills, projectilePrefab, zonePrefab);
            GameObject elementReactions =
                SceneBuildUtility.CreateElementReactionDatabase(reactionTable);

            // The director moves the player onto the entrance during Awake, so
            // the authored position only has to be somewhere harmless.
            PlayerMotor player = SceneBuildUtility.CreatePlayer(playerMaterial, new Vector3(0f, 1f, 0f));
            TopDownCameraRig cameraRig = SceneBuildUtility.CreateCameraRig();
            GameObject debugTools = SceneBuildUtility.CreateDebugTools();

            PanelSettings panelSettings =
                SceneBuildUtility.CreateOrLoadPanelSettings("RuntimePanelSettings");
            InventoryUI inventoryUI = SceneBuildUtility.CreateInventoryUI(panelSettings);

            Material vfxLineMaterial = SceneBuildUtility.CreateVfxLineMaterial("VfxLine");
            VfxPresenter vfxPresenter =
                SceneBuildUtility.CreateVfxPresenter(vfxConfig, vfxLineMaterial, panelSettings);

            // Above every other panel, so a fade covers the inventory too.
            CurtainPresenter curtainPresenter =
                SceneBuildUtility.CreateCurtainPresenter(panelSettings);

            AudioPresenter audioPresenter = SceneBuildUtility.CreateAudioPresenter(
                SceneBuildUtility.CreateAudioConfig("AudioConfig"));

            SceneBuildUtility.CreateBootstrap(
                cameraRig, player, debugTools, director, inventoryUI, vfxPresenter,
                curtainPresenter, audioPresenter);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SceneBuildUtility.RegisterInBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();

            // Same manual step as the arena: creating a SubScene from code
            // relies on editor API that cannot be verified by compiling here,
            // while through the menu it is two clicks.
            Selection.objects = new Object[]
            {
                waveSpawner, simulationSettings, lootDatabase, characterStats, skillDatabase,
                elementReactions
            };

            Debug.Log(
                $"[DungeonSceneBuilder] Dungeon scene built: {ScenePath}\n" +
                "ONE STEP LEFT: WaveSpawner, SimulationSettings, LootDatabase, CharacterStats, " +
                "SkillDatabase and ElementReactions are already selected in the hierarchy — " +
                "right-click them, then New Sub Scene > From Selection. Without a SubScene they " +
                "are never baked into entities: no waves spawn, enemies get no pathfinding " +
                "settings, no chests are placed, characters have no base stats, nothing is " +
                "castable and nothing burns.\n" +
                "There are deliberately no spawn points and no chests in the scene — the " +
                "dungeon creates them in its combat and treasure rooms once the floor is " +
                "generated.\n" +
                "In play mode: Space spawns a wave, E opens a chest or picks an item up, " +
                "I opens the inventory. Left mouse, right mouse, Q and R cast the four " +
                "starter skills.");
        }

        /// <summary>
        /// Creates the object the dungeon is built into.
        ///
        /// Two things here are load-bearing. The root sits at the world origin,
        /// because the layout centres itself there and the surface bakes in its
        /// own local space. And the surface collects its CHILDREN only —
        /// collecting the whole scene would bake the player capsule and every
        /// enemy into the navmesh as obstacles.
        /// </summary>
        private static DungeonDirector CreateDungeonRoot(DungeonGenerationConfig config)
        {
            var root = new GameObject("Dungeon");
            root.transform.position = Vector3.zero;

            var geometryRoot = new GameObject("Geometry");
            geometryRoot.transform.SetParent(root.transform, worldPositionStays: false);

            NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            DungeonDirector director = root.AddComponent<DungeonDirector>();

            var serialized = new SerializedObject(director);
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.FindProperty("_navMeshSurface").objectReferenceValue = surface;
            serialized.FindProperty("_geometryRoot").objectReferenceValue = geometryRoot.transform;

            AssignRoomMaterials(serialized);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return director;
        }

        /// <summary>
        /// Colour-codes the room types. Until there is art, this is the only way
        /// to see at a glance whether the generator put the boss room at the far
        /// end and the treasure rooms in dead ends.
        /// </summary>
        private static void AssignRoomMaterials(SerializedObject director)
        {
            AssignMaterial(director, "_wall", "DungeonWall", new Color(0.30f, 0.28f, 0.26f));
            AssignMaterial(director, "_corridorFloor", "DungeonCorridor", new Color(0.20f, 0.21f, 0.24f));
            AssignMaterial(director, "_entranceFloor", "DungeonEntrance", new Color(0.24f, 0.48f, 0.32f));
            AssignMaterial(director, "_transitFloor", "DungeonTransit", new Color(0.26f, 0.28f, 0.33f));
            AssignMaterial(director, "_combatFloor", "DungeonCombat", new Color(0.48f, 0.24f, 0.22f));
            AssignMaterial(director, "_treasureFloor", "DungeonTreasure", new Color(0.55f, 0.45f, 0.18f));
            AssignMaterial(director, "_bossFloor", "DungeonBoss", new Color(0.36f, 0.20f, 0.44f));
        }

        private static void AssignMaterial(
            SerializedObject director, string field, string assetName, Color color)
        {
            director.FindProperty($"_materials.{field}").objectReferenceValue =
                SceneBuildUtility.CreateMaterial(assetName, color);
        }

        /// <summary>
        /// The table treasure chests roll on, with a starter set of items.
        ///
        /// Filled in only when the asset is created. Rebuilding the scene must
        /// not overwrite a table somebody has since tuned — the point of these
        /// assets is that they are edited by hand, and a generator that silently
        /// reverts that editing is worse than no generator.
        ///
        /// The items themselves are placeholders with no stats, because stats
        /// belong to equipment and equipment is its own step. What they do carry
        /// is a rarity, which is the only property the drop pipeline reads.
        /// </summary>
        private static LootTable CreateOrLoadTreasureTable()
        {
            LootTable table = SceneBuildUtility.CreateOrLoadConfig<LootTable>(
                "TreasureLootTable", out bool created);

            if (!created)
                return table;

            ItemDefinition[] items = CreateSampleItems();

            var serialized = new SerializedObject(table);
            SerializedProperty entries = serialized.FindProperty("_entries");
            entries.arraySize = items.Length;

            for (int i = 0; i < items.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("_item").objectReferenceValue = items[i];
                entry.FindPropertyRelative("_weight").floatValue = 1f;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        /// <summary>
        /// The starter items.
        ///
        /// Placeholders, but not empty ones: every slot is represented, both
        /// modifier kinds appear, and the numbers climb with rarity. That is
        /// what makes the character sheet visibly move when something is
        /// equipped, which is the only way to see the stat maths running.
        /// </summary>
        private static ItemDefinition[] CreateSampleItems()
        {
            // After the slot come the inventory footprint — width, height and
            // whether the player may turn it on its side — and whether the item
            // needs both hands. They differ on purpose: eight one-by-one items
            // that all go in the same slot would exercise nothing.
            //
            // The two rings are here for the same reason. A ring is the only
            // item that fits in more than one slot, so without one the ring
            // pairing and the slot-to-slot move have nothing to act on.
            var definitions =
                new (string name, ItemRarity rarity, EquipmentSlot slot,
                     int width, int height, bool canRotate, bool twoHanded,
                     ItemStatValue[] baseStats, ItemAffix[] affixes)[]
                {
                    ("Cracked Dagger", ItemRarity.Common, EquipmentSlot.MainHand,
                        1, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 8f) },
                        new[] { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 10f) }),

                    ("Dented Buckler", ItemRarity.Common, EquipmentSlot.OffHand,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Armour, 12f) },
                        new[] { new ItemAffix(StatKind.Armour, ModifierKind.Increased, 10f) }),

                    ("Rusted Helm", ItemRarity.Common, EquipmentSlot.Helmet,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Armour, 8f) },
                        new[] { new ItemAffix(StatKind.MaxHealth, ModifierKind.Flat, 10f) }),

                    ("Hunters Bow", ItemRarity.Uncommon, EquipmentSlot.MainHand,
                        2, 3, true, true,
                        new[] { new ItemStatValue(StatKind.Damage, 14f) },
                        new[] { new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 15f) }),

                    ("Runed Gauntlets", ItemRarity.Uncommon, EquipmentSlot.Gloves,
                        2, 2, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 3f) },
                        new[] { new ItemAffix(StatKind.Damage, ModifierKind.Increased, 12f) }),

                    ("Band of Embers", ItemRarity.Uncommon, EquipmentSlot.Ring1,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.Damage, 4f) },
                        new[] { new ItemAffix(StatKind.FireResistance, ModifierKind.Flat, 12f) }),

                    ("Coil of the Deep", ItemRarity.Rare, EquipmentSlot.Ring1,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.MaxHealth, 15f) },
                        new[] { new ItemAffix(StatKind.ColdResistance, ModifierKind.Flat, 18f) }),

                    ("Frostbite Blade", ItemRarity.Rare, EquipmentSlot.MainHand,
                        1, 3, true, false,
                        new[] { new ItemStatValue(StatKind.Damage, 22f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 20f),
                            new ItemAffix(StatKind.ColdResistance, ModifierKind.Flat, 15f)
                        }),

                    ("Warlords Plate", ItemRarity.Epic, EquipmentSlot.Chest,
                        2, 3, false, false,
                        new[]
                        {
                            new ItemStatValue(StatKind.Armour, 40f),
                            new ItemStatValue(StatKind.MaxHealth, 25f)
                        },
                        new[] { new ItemAffix(StatKind.MaxHealth, ModifierKind.Increased, 18f) }),

                    ("Dawnbringer", ItemRarity.Legendary, EquipmentSlot.MainHand,
                        1, 4, true, true,
                        new[] { new ItemStatValue(StatKind.Damage, 45f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 45f),
                            new ItemAffix(StatKind.AttackSpeed, ModifierKind.Increased, 25f)
                        }),

                    ("Heart of the Fall", ItemRarity.Mythic, EquipmentSlot.Amulet,
                        1, 1, false, false,
                        new[] { new ItemStatValue(StatKind.MaxHealth, 50f) },
                        new[]
                        {
                            new ItemAffix(StatKind.Damage, ModifierKind.Increased, 30f),
                            new ItemAffix(StatKind.MoveSpeed, ModifierKind.Increased, 10f)
                        })
                };

            var items = new ItemDefinition[definitions.Length];

            for (int i = 0; i < definitions.Length; i++)
            {
                string assetName = definitions[i].name.Replace(" ", string.Empty);

                ItemDefinition item = SceneBuildUtility.CreateOrLoadConfig<ItemDefinition>(
                    assetName, ItemFolder, out bool created);

                if (created)
                {
                    var serialized = new SerializedObject(item);
                    serialized.FindProperty("_displayName").stringValue = definitions[i].name;
                    serialized.FindProperty("_rarity").enumValueIndex = (int)definitions[i].rarity;
                    serialized.FindProperty("_slot").enumValueIndex = (int)definitions[i].slot;

                    serialized.FindProperty("_gridWidth").intValue = definitions[i].width;
                    serialized.FindProperty("_gridHeight").intValue = definitions[i].height;
                    serialized.FindProperty("_canRotate").boolValue = definitions[i].canRotate;
                    serialized.FindProperty("_isTwoHanded").boolValue = definitions[i].twoHanded;

                    WriteStats(serialized.FindProperty("_baseStats"), definitions[i].baseStats);
                    WriteAffixes(serialized.FindProperty("_affixes"), definitions[i].affixes);

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                items[i] = item;
            }

            return items;
        }

        private static void WriteStats(SerializedProperty array, ItemStatValue[] values)
        {
            array.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_stat").enumValueIndex = (int)values[i].Stat;
                element.FindPropertyRelative("_value").floatValue = values[i].Value;
            }
        }

        private static void WriteAffixes(SerializedProperty array, ItemAffix[] affixes)
        {
            array.arraySize = affixes.Length;

            for (int i = 0; i < affixes.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_stat").enumValueIndex = (int)affixes[i].Stat;
                element.FindPropertyRelative("_kind").enumValueIndex = (int)affixes[i].Kind;
                element.FindPropertyRelative("_value").floatValue = affixes[i].Value;
            }
        }


        /// <summary>
        /// The object that carries the loot tables and prefabs into ECS. Goes
        /// into the SubScene with the wave spawner: without baking there is no
        /// blob, and without the blob nothing can drop.
        /// </summary>
        private static GameObject CreateLootDatabase(
            LootConfig config, LootTable table, GameObject chestPrefab, GameObject itemPrefab)
        {
            var databaseObject = new GameObject("LootDatabase");
            LootDatabaseAuthoring authoring = databaseObject.AddComponent<LootDatabaseAuthoring>();

            var serialized = new SerializedObject(authoring);
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.FindProperty("_chestPrefab").objectReferenceValue = chestPrefab;
            serialized.FindProperty("_itemPrefab").objectReferenceValue = itemPrefab;

            SerializedProperty tables = serialized.FindProperty("_tables");
            tables.arraySize = 1;
            tables.GetArrayElementAtIndex(0).objectReferenceValue = table;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return databaseObject;
        }
    }
}
