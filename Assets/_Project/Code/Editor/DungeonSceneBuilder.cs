using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            LootTable lootTable = ItemContentFactory.CreateOrLoadTreasureTable();
            GameObject projectilePrefab =
                SceneBuildUtility.CreateProjectilePrefab(projectileMaterial);

            // White too: the pool writes the element colour onto every zone.
            GameObject zonePrefab = SceneBuildUtility.CreateZonePrefab(
                SceneBuildUtility.CreateMaterial("ElementZone", Color.white));

            // The starter set and the wider library as one list. The database
            // is what "exists" means for a skill, so a library asset missing
            // from it is a gem that sits in a socket and casts nothing.
            SkillDefinition[] skills =
                BuildLibraryFactory.WithLibrary(SkillContentFactory.CreateStarterSkills());
            ElementReactionTable reactionTable = ElementContentFactory.CreateReactionTable();

            // The gem that casts the zone skill, added to the chest table if it
            // is not already there. Without it the zone skill exists in the
            // database and nothing can ever cast it.
            //
            // Named rather than counted: it used to reach for the last skill in
            // the array, and the array has since grown two welded attacks on the
            // end — so the gem was one rebuild away from casting a weapon bolt
            // under the right name.
            ElementContentFactory.CreateZoneGem(
                SkillContentFactory.CreateZoneSkill(), lootTable);

            // The trigger gem, the conditional support and the two keystone
            // rings, added to the same table for the same reason: a mechanism
            // that exists in the code and in no item is a mechanism nobody can
            // reach.
            SkillContentFactory.CreateBuildContent(lootTable);

            // The library: a gem for every skill above, the supports that ask a
            // question before they act, and the two-handed staff to put them
            // in: two rolled skills, six support holes each. Without a weapon
            // that links that many, most of the combinations the gems were
            // written for cannot be assembled at all.
            BuildLibraryFactory.CreateGems(lootTable);

            // Every weapon gets the attack it is born with, where it does not
            // have one yet. This is what makes equipping a sword mean something
            // on its own: without it a weapon is a stat sheet you cannot swing.
            ItemContentFactory.AssignDefaultAttacks();

            SceneBuildUtility.CreateLighting();

            DungeonDirector director = CreateDungeonRoot(dungeonConfig);

            GameObject waveSpawner = SceneBuildUtility.CreateWaveSpawner(enemyPrefab, spawnConfig);
            GameObject simulationSettings =
                SceneBuildUtility.CreateSimulationSettings(pathfindingConfig, separationConfig);
            GameObject lootDatabase =
                SceneBuildUtility.CreateLootDatabase(
                    lootConfig, lootTable, chestPrefab, lootItemPrefab);
            GameObject characterStats =
                SceneBuildUtility.CreateCharacterStats(characterConfig);
            GameObject skillDatabase =
                SceneBuildUtility.CreateSkillDatabase(skills, projectilePrefab, zonePrefab);
            GameObject elementReactions =
                SceneBuildUtility.CreateElementReactionDatabase(reactionTable);

            // The director moves the player onto the entrance during Awake, so
            // the authored position only has to be somewhere harmless.
            PlayerMotor player = SceneBuildUtility.CreatePlayer(new Vector3(0f, 1f, 0f));
            TopDownCameraRig cameraRig = SceneBuildUtility.CreateCameraRig();
            GameObject debugTools = SceneBuildUtility.CreateDebugTools();

            InventoryUI inventoryUI = SceneBuildUtility.CreateInventoryUI(
                SceneBuildUtility.CreateCharacterPortrait(player));

            Material vfxLineMaterial = SceneBuildUtility.CreateVfxLineMaterial("VfxLine");
            VfxPresenter vfxPresenter =
                SceneBuildUtility.CreateVfxPresenter(vfxConfig, vfxLineMaterial);

            // Above every other panel, so a fade covers the inventory too.
            CurtainPresenter curtainPresenter =
                SceneBuildUtility.CreateCurtainPresenter();

            AudioPresenter audioPresenter = SceneBuildUtility.CreateAudioPresenter(
                SceneBuildUtility.CreateAudioConfig("AudioConfig"));

            // The orbs and the skill bar. Between the damage numbers and the
            // inventory, so it covers neither.
            PlayerHud playerHud = SceneBuildUtility.CreatePlayerHud();

            // Named rather than positional. The lobby's two optional arguments
            // landed in the middle of this list, so the positional call was
            // silently handing the VFX presenter to the lobby parameter — the
            // exact failure a list of eight same-shaped optionals invites.
            SceneBuildUtility.CreateBootstrap(
                cameraRig, player, debugTools,
                dungeon: director,
                inventoryUI: inventoryUI,
                vfxPresenter: vfxPresenter,
                curtainPresenter: curtainPresenter,
                audioPresenter: audioPresenter,
                playerHud: playerHud);

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

    }
}
