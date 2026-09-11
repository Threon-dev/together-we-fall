using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Config;
using TogetherWeFall.DebugTools.Authoring;
using TogetherWeFall.Player;
using TogetherWeFall.Spawning.Authoring;
using TogetherWeFall.UI;
using TogetherWeFall.Audio;
using TogetherWeFall.Curtain;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Builds the test arena from scratch through a single menu item.
    ///
    /// Why a generator rather than by hand: a scene is YAML full of GUIDs, and
    /// any change made "with the mouse" neither survives a merge nor reproduces
    /// on another machine. A generator gives one button that says "rebuild the
    /// arena", which matters while the arena layout still changes from stage to
    /// stage.
    ///
    /// This scene is the performance rig and stays authored on purpose: an
    /// unchanging floor is what makes two FPS measurements comparable. The
    /// generated dungeon lives in its own scene, built by DungeonSceneBuilder.
    ///
    /// It carries the character sheet, the skills and — since skills became
    /// gems — the item database and the inventory panel too. That is not a
    /// softening of "the arena has no loot": nothing here places a chest and
    /// nothing rolls a table. It is that a gem is an item, so a scene with no
    /// items is a scene where no hotkey does anything, and a rig that cannot
    /// cast measures the wrong half of the game.
    ///
    /// What stays out is the floor clutter: no chests, no drops. Measuring what
    /// three hundred enemies cost while a chain reaction goes off through them
    /// is exactly what this scene is for, and items lying about would only be
    /// something else in the frame time. The pooled item entities are a fixed
    /// cost that does not grow with the wave, so the curve being measured is
    /// unaffected.
    /// </summary>
    public static class ArenaSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        private const string NavMeshFolder = "Assets/_Project/Scenes";

        private const float GroundSize = 60f;
        private const float SpawnRingRadius = 26f;
        private const int SpawnPointCount = 5;

        private const int TrainingDummyCount = 3;
        private const float TrainingDummySpacing = 4f;

        /// <summary>
        /// How far in front of the player start the dummies stand. Inside the
        /// range of every skill except the melee swing, which is meant to be
        /// walked up to.
        /// </summary>
        private const float TrainingDummyDistance = 9f;

        [MenuItem("Tools/Together We Fall/Build Arena Scene")]
        public static void BuildArenaScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material groundMaterial =
                SceneBuildUtility.CreateMaterial("ArenaGround", new Color(0.22f, 0.24f, 0.28f));
            Material obstacleMaterial =
                SceneBuildUtility.CreateMaterial("ArenaObstacle", new Color(0.40f, 0.34f, 0.30f));
            Material playerMaterial =
                SceneBuildUtility.CreateMaterial("PlayerBody", new Color(0.25f, 0.65f, 0.95f));
            Material enemyMaterial =
                SceneBuildUtility.CreateMaterial("EnemyBody", new Color(0.85f, 0.25f, 0.22f));
            Material dummyMaterial =
                SceneBuildUtility.CreateMaterial("TrainingDummy", new Color(0.72f, 0.62f, 0.35f));

            var enemyConfig = SceneBuildUtility.CreateOrLoadConfig<EnemyConfig>("EnemyConfig");
            var spawnConfig = SceneBuildUtility.CreateOrLoadConfig<SpawnConfig>("SpawnConfig");
            var pathfindingConfig =
                SceneBuildUtility.CreateOrLoadConfig<PathfindingConfig>("PathfindingConfig");
            var separationConfig =
                SceneBuildUtility.CreateOrLoadConfig<SeparationConfig>("SeparationConfig");
            var characterConfig =
                SceneBuildUtility.CreateOrLoadConfig<CharacterConfig>("CharacterConfig");
            var vfxConfig = SceneBuildUtility.CreateOrLoadConfig<VfxConfig>("VfxConfig");
            var lootConfig = SceneBuildUtility.CreateOrLoadConfig<LootConfig>("LootConfig");

            Material projectileMaterial =
                SceneBuildUtility.CreateMaterial("SkillProjectile", Color.white);

            Material zoneMaterial =
                SceneBuildUtility.CreateMaterial("ElementZone", Color.white);

            // The chest and item prefabs are needed as references, not as
            // scenery: the loot database refuses to bake without them. No chest
            // is ever placed here — only a dungeon places those — so what the
            // arena actually gets out of that bake is the item database.
            Material chestMaterial =
                SceneBuildUtility.CreateMaterial("DungeonChest", new Color(0.52f, 0.38f, 0.18f));
            Material lootItemMaterial =
                SceneBuildUtility.CreateMaterial("LootItem", Color.white);

            GameObject enemyPrefab = SceneBuildUtility.CreateEnemyPrefab(enemyConfig, enemyMaterial);
            GameObject projectilePrefab =
                SceneBuildUtility.CreateProjectilePrefab(projectileMaterial);
            GameObject zonePrefab = SceneBuildUtility.CreateZonePrefab(zoneMaterial);
            GameObject chestPrefab = SceneBuildUtility.CreateChestPrefab(lootConfig, chestMaterial);
            GameObject lootItemPrefab =
                SceneBuildUtility.CreateLootItemPrefab(lootConfig, lootItemMaterial);

            // The starter set and the wider library as one list. The database
            // is what "exists" means for a skill, so a library asset missing
            // from it is a gem that sits in a socket and casts nothing.
            SkillDefinition[] skills =
                BuildLibraryFactory.WithLibrary(SkillContentFactory.CreateStarterSkills());
            ElementReactionTable reactionTable = ElementContentFactory.CreateReactionTable();

            // The same table, the same gems, the same keystones as the dungeon.
            // Not because the arena rolls loot — it never does — but because the
            // item database is baked FROM the table, and a rig whose item set
            // differs from the game measures a game nobody plays.
            LootTable lootTable = ItemContentFactory.CreateOrLoadTreasureTable();

            // The gem that casts the zone skill, named rather than counted.
            // It used to reach for the last skill in the array, and the array
            // has since grown two welded attacks on the end — so the gem was one
            // rebuild away from casting a weapon bolt under the right name.
            ElementContentFactory.CreateZoneGem(
                SkillContentFactory.CreateZoneSkill(), lootTable);
            SkillContentFactory.CreateBuildContent(lootTable);

            // The library: a gem for every skill above, the supports that ask a
            // question before they act, and the four-link staff to put them in.
            // Without a weapon that links four holes, most of the combinations
            // the gems were written for cannot be assembled at all.
            BuildLibraryFactory.CreateGems(lootTable);

            // Every weapon gets the attack it is born with, where it does not
            // have one yet. This is what makes equipping a sword mean something
            // on its own: without it a weapon is a stat sheet you cannot swing.
            ItemContentFactory.AssignDefaultAttacks();

            SceneBuildUtility.CreateLighting();
            GameObject ground = CreateGround(groundMaterial);
            CreateObstacles(obstacleMaterial);

            GameObject spawnPoints = CreateSpawnPoints();
            GameObject trainingDummies = CreateTrainingDummies(dummyMaterial);
            GameObject waveSpawner = SceneBuildUtility.CreateWaveSpawner(enemyPrefab, spawnConfig);
            GameObject simulationSettings =
                SceneBuildUtility.CreateSimulationSettings(pathfindingConfig, separationConfig);
            GameObject characterStats = SceneBuildUtility.CreateCharacterStats(characterConfig);

            // Skills live in gems, gems are items, and items come from here. The
            // arena went without this for as long as skills were named directly
            // by the loadout; the moment a socket became the answer, a scene with
            // no item database became a scene where every hotkey is silent.
            GameObject lootDatabase = SceneBuildUtility.CreateLootDatabase(
                lootConfig, lootTable, chestPrefab, lootItemPrefab);
            GameObject skillDatabase =
                SceneBuildUtility.CreateSkillDatabase(skills, projectilePrefab, zonePrefab);
            GameObject elementReactions =
                SceneBuildUtility.CreateElementReactionDatabase(reactionTable);

            PlayerMotor player =
                SceneBuildUtility.CreatePlayer(playerMaterial, new Vector3(0f, 1f, 0f));
            TopDownCameraRig cameraRig = SceneBuildUtility.CreateCameraRig();
            GameObject debugTools = SceneBuildUtility.CreateDebugTools();

            PanelSettings panelSettings =
                SceneBuildUtility.CreateOrLoadPanelSettings("RuntimePanelSettings");

            // The arena has an inventory now, and it is not a convenience: the
            // starter kit hands out gems in the BAG, deliberately, so socketing
            // them is the player's decision. Without this panel there is no way
            // to make that decision, and therefore no way to cast anything.
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
                cameraRig, player, debugTools, dungeon: null, inventoryUI: inventoryUI,
                vfxPresenter: vfxPresenter, curtainPresenter: curtainPresenter,
                audioPresenter: audioPresenter);

            BuildNavMesh(ground);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SceneBuildUtility.RegisterInBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();

            // Select what has to move into a SubScene. Creating a SubScene from
            // code relies on editor API that cannot be verified by compiling
            // here, while through the menu it is two clicks — so leaving that
            // step to a human is more honest than shipping code that might not
            // build.
            Selection.objects = new Object[]
            {
                spawnPoints, waveSpawner, simulationSettings, characterStats, skillDatabase,
                elementReactions, lootDatabase, trainingDummies
            };

            Debug.Log(
                $"[ArenaSceneBuilder] Arena built: {ScenePath}\n" +
                "ONE STEP LEFT: SpawnPoints, WaveSpawner, SimulationSettings, CharacterStats, " +
                "SkillDatabase, ElementReactions, LootDatabase and TrainingDummies are already " +
                "selected in the hierarchy — right-click them, then New Sub Scene > " +
                "From Selection. Without a SubScene they are never baked into entities: no " +
                "waves spawn, enemies receive no " +
                "pathfinding settings, nothing is castable, nothing burns and the dummies are " +
                "scenery.\n" +
                "In play mode: press I FIRST. The starter kit wears the wand and puts seven gems " +
                "in the BAG, so nothing casts until an active gem is socketed AND dragged onto " +
                "a hotkey — the same two steps as in the dungeon, and on purpose: the " +
                "arrangement is the player's.\n" +
                "Then Space spawns a wave, and left mouse, right mouse, Q and R cast whatever " +
                "was bound. The three dummies ahead of the start take damage, flash and never " +
                "fall over — the quickest way to watch a status tick and a reaction go off.");
        }

        private static GameObject CreateGround(Material material)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";

            // The Plane primitive is 10x10 units at scale 1.
            float scale = GroundSize / 10f;
            ground.transform.localScale = new Vector3(scale, 1f, scale);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;

            return ground;
        }

        private static void CreateObstacles(Material material)
        {
            // Laid out so they block the straight lines from the spawn points to
            // the centre — otherwise there is no way to see whether navmesh
            // avoidance actually works.
            var layout = new (Vector3 position, Vector3 scale)[]
            {
                (new Vector3(  8f, 1.5f,   6f), new Vector3(6f, 3f,  3f)),
                (new Vector3( -9f, 1.5f,   4f), new Vector3(3f, 3f,  8f)),
                (new Vector3(  2f, 1.5f, -11f), new Vector3(9f, 3f,  3f)),
                (new Vector3(-14f, 1.5f, -10f), new Vector3(4f, 3f,  4f)),
                (new Vector3( 15f, 1.5f,  -7f), new Vector3(3f, 3f,  7f)),
                (new Vector3( -4f, 1.5f,  14f), new Vector3(8f, 3f,  3f)),
                (new Vector3( 17f, 1.5f,  12f), new Vector3(4f, 3f,  4f)),
                (new Vector3(-18f, 1.5f,  16f), new Vector3(5f, 3f,  3f)),
            };

            var root = new GameObject("Obstacles");

            for (int i = 0; i < layout.Length; i++)
            {
                GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = $"Obstacle_{i}";
                obstacle.transform.SetParent(root.transform);
                obstacle.transform.position = layout[i].position;
                obstacle.transform.localScale = layout[i].scale;
                obstacle.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private static GameObject CreateSpawnPoints()
        {
            var root = new GameObject("SpawnPoints");

            for (int i = 0; i < SpawnPointCount; i++)
            {
                float angle = i / (float)SpawnPointCount * Mathf.PI * 2f;
                var point = new GameObject($"SpawnPoint_{i}");
                point.transform.SetParent(root.transform);
                point.transform.position = new Vector3(
                    Mathf.Cos(angle) * SpawnRingRadius,
                    0f,
                    Mathf.Sin(angle) * SpawnRingRadius);

                point.AddComponent<SpawnPointAuthoring>();
            }

            return root;
        }

        /// <summary>
        /// Three targets that stand still and never die.
        ///
        /// Only in the arena, deliberately. They carry EnemyTag so that every
        /// targeting query finds them, and in the dungeon that would mean a
        /// combat room containing one could never be counted as cleared.
        /// </summary>
        private static GameObject CreateTrainingDummies(Material material)
        {
            var root = new GameObject("TrainingDummies");

            for (int i = 0; i < TrainingDummyCount; i++)
            {
                GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = $"TrainingDummy_{i}";
                dummy.transform.SetParent(root.transform);

                float offset = (i - (TrainingDummyCount - 1) * 0.5f) * TrainingDummySpacing;
                dummy.transform.position = new Vector3(offset, 0f, TrainingDummyDistance);

                dummy.GetComponent<MeshRenderer>().sharedMaterial = material;

                // The collider has to go, and not only because nothing uses
                // physics: the arena navmesh is baked from colliders further
                // down this method, and three capsules standing in front of the
                // player would each punch a hole in it.
                Object.DestroyImmediate(dummy.GetComponent<CapsuleCollider>());

                dummy.AddComponent<TrainingDummyAuthoring>();
            }

            return root;
        }

        /// <summary>
        /// Bakes the navmesh and persists the result as an asset.
        ///
        /// The asset step is not optional: BuildNavMesh alone produces runtime
        /// data that lives only in memory, so saving the scene would store a
        /// reference to nothing and enemies would find no paths after a reload.
        /// The dungeon scene does not do this — it bakes at load time instead,
        /// because a floor that does not exist until the seed is rolled has
        /// nothing to save.
        /// </summary>
        private static void BuildNavMesh(GameObject ground)
        {
            NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                Debug.LogWarning(
                    "[ArenaSceneBuilder] Navmesh bake produced no data. Select Ground and " +
                    "press Bake on its NavMeshSurface to retry.");
                return;
            }

            string dataPath = $"{NavMeshFolder}/Arena_NavMesh.asset";
            Directory.CreateDirectory(NavMeshFolder);

            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, dataPath);
        }
    }
}
