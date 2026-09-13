using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TogetherWeFall.Audio;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Config;
using TogetherWeFall.Curtain;
using TogetherWeFall.DebugTools.Authoring;
using TogetherWeFall.Lobby;
using TogetherWeFall.Lobby.Authoring;
using TogetherWeFall.Player;
using TogetherWeFall.UI;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Builds the lobby: a place rather than a screen.
    ///
    /// The team gathers here, buys and sells, works on their gear, tries a build
    /// against dummies that report what it is actually doing, and walks into a
    /// portal together. Everything in it is made of parts that already existed —
    /// an NPC is a chest with a different meaning attached, a shop is a
    /// container without an owner, a dummy is the one from the arena with a log
    /// on it — which is why this file is a layout rather than a subsystem.
    ///
    /// Authored by hand like the arena and for a weaker reason: nothing here is
    /// measured, so the floor could be generated. It is not because a hub is
    /// somewhere players learn the shape of, and a hub that moved between
    /// sessions would be a hub nobody knows their way around.
    /// </summary>
    public static class LobbySceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Lobby.unity";
        private const string NavMeshFolder = "Assets/_Project/Scenes";

        private const float GroundSize = 50f;

        [MenuItem("Tools/Together We Fall/Build Lobby Scene")]
        public static void BuildLobbyScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material groundMaterial =
                SceneBuildUtility.CreateMaterial("LobbyGround", new Color(0.19f, 0.21f, 0.26f));
            Material enemyMaterial =
                SceneBuildUtility.CreateMaterial("EnemyBody", new Color(0.85f, 0.25f, 0.22f));
            Material dummyMaterial =
                SceneBuildUtility.CreateMaterial("TrainingDummy", new Color(0.72f, 0.62f, 0.35f));
            Material vendorMaterial =
                SceneBuildUtility.CreateMaterial("LobbyVendor", new Color(0.85f, 0.72f, 0.35f));
            Material forgeMaterial =
                SceneBuildUtility.CreateMaterial("LobbyForge", new Color(0.75f, 0.40f, 0.25f));
            Material portalMaterial =
                SceneBuildUtility.CreateMaterial("LobbyPortal", new Color(0.45f, 0.35f, 0.85f));
            Material trainerMaterial =
                SceneBuildUtility.CreateMaterial("LobbyTrainer", new Color(0.40f, 0.70f, 0.55f));

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

            // The same content as everywhere else. A lobby whose item set
            // differed from the dungeon's would be a shop selling things that do
            // not exist on the floor below.
            SkillDefinition[] skills =
                BuildLibraryFactory.WithLibrary(SkillContentFactory.CreateStarterSkills());
            ElementReactionTable reactionTable = ElementContentFactory.CreateReactionTable();
            LootTable lootTable = ItemContentFactory.CreateOrLoadTreasureTable();

            ElementContentFactory.CreateZoneGem(
                SkillContentFactory.CreateZoneSkill(), lootTable);
            SkillContentFactory.CreateBuildContent(lootTable);
            BuildLibraryFactory.CreateGems(lootTable);
            ItemContentFactory.AssignDefaultAttacks();

            // Money, and a handful of it in the starting kit so the shop can be
            // tried the first time somebody walks up to it.
            ItemDefinition coin = LobbyContentFactory.CreateOrLoadCoin(lootTable);
            LobbyContentFactory.GrantStarterCoins(
                SceneBuildUtility.CreateOrLoadConfig<StarterKitConfig>("StarterKitConfig"), coin);

            SceneBuildUtility.CreateLighting();
            GameObject ground = CreateGround(groundMaterial);

            GameObject npcs = CreateNpcs(
                vendorMaterial, forgeMaterial, portalMaterial, trainerMaterial);

            GameObject dummies = CreateTrainingDummies(dummyMaterial);

            // No spawn points, so nothing ever spawns — but the spawner has to
            // exist for the same reason it does in the dungeon: the debug key
            // asks it for a wave, and a scene without one answers that with an
            // exception rather than with nothing.
            GameObject waveSpawner = SceneBuildUtility.CreateWaveSpawner(enemyPrefab, spawnConfig);

            GameObject simulationSettings =
                SceneBuildUtility.CreateSimulationSettings(pathfindingConfig, separationConfig);
            GameObject characterStats = SceneBuildUtility.CreateCharacterStats(characterConfig);
            GameObject lootDatabase = SceneBuildUtility.CreateLootDatabase(
                lootConfig, lootTable, chestPrefab, lootItemPrefab);
            GameObject skillDatabase =
                SceneBuildUtility.CreateSkillDatabase(skills, projectilePrefab, zonePrefab);
            GameObject elementReactions =
                SceneBuildUtility.CreateElementReactionDatabase(reactionTable);

            PlayerMotor player =
                SceneBuildUtility.CreatePlayer(new Vector3(0f, 1f, 0f));
            TopDownCameraRig cameraRig = SceneBuildUtility.CreateCameraRig();
            GameObject debugTools = SceneBuildUtility.CreateDebugTools();

            InventoryUI inventoryUI = SceneBuildUtility.CreateInventoryUI(
                SceneBuildUtility.CreateCharacterPortrait(player));
            LobbyUI lobbyUI = SceneBuildUtility.CreateLobbyUI();
            SceneLoadBridge sceneLoader = SceneBuildUtility.CreateSceneLoadBridge();

            Material vfxLineMaterial = SceneBuildUtility.CreateVfxLineMaterial("VfxLine");
            VfxPresenter vfxPresenter =
                SceneBuildUtility.CreateVfxPresenter(vfxConfig, vfxLineMaterial);

            CurtainPresenter curtainPresenter =
                SceneBuildUtility.CreateCurtainPresenter();

            AudioPresenter audioPresenter = SceneBuildUtility.CreateAudioPresenter(
                SceneBuildUtility.CreateAudioConfig("AudioConfig"));

            SceneBuildUtility.CreateBootstrap(
                cameraRig, player, debugTools, dungeon: null, inventoryUI: inventoryUI,
                lobbyUI: lobbyUI, sceneLoader: sceneLoader, vfxPresenter: vfxPresenter,
                curtainPresenter: curtainPresenter, audioPresenter: audioPresenter);

            BuildNavMesh(ground);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SceneBuildUtility.RegisterInBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();

            Selection.objects = new Object[]
            {
                waveSpawner, simulationSettings, characterStats, skillDatabase,
                elementReactions, lootDatabase, npcs, dummies
            };

            Debug.Log(
                $"[LobbySceneBuilder] Lobby built: {ScenePath}\n" +
                "ONE STEP LEFT: WaveSpawner, SimulationSettings, CharacterStats, SkillDatabase, " +
                "ElementReactions, LootDatabase, LobbyNpcs and TrainingDummies are already " +
                "selected in the hierarchy — right-click them, then New Sub Scene > From " +
                "Selection. Without a SubScene none of them is baked into entities: there are " +
                "no NPCs to talk to, no items to trade and nothing to cast.\n" +
                "In play mode: walk up to an NPC and press E. The trader is west, the forge " +
                "east, the portal north and the dummies south. Esc closes a panel. Press I " +
                "first if you want to socket the starting gems before testing a build.\n" +
                "The portal loads the Dungeon scene, so build that one too if it is not in the " +
                "build settings yet.");
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

        /// <summary>
        /// The four services, spread far enough apart that their interaction
        /// radii do not overlap.
        ///
        /// That matters more than it looks: the resolver hands a press to the
        /// NEAREST thing in reach, so two NPCs standing together would make
        /// which shop opens a matter of half a metre.
        /// </summary>
        private static GameObject CreateNpcs(
            Material vendor, Material forge, Material portal, Material trainer)
        {
            var root = new GameObject("LobbyNpcs");

            LobbyNpcAuthoring shop = CreateNpc(
                root, "Trader", NpcServiceType.Vendor, new Vector3(-9f, 1f, 7f),
                PrimitiveType.Cube, vendor);

            var shopSerialized = new SerializedObject(shop);
            WriteObjectArray(
                shopSerialized.FindProperty("_stock"), LobbyContentFactory.LoadVendorStock());
            shopSerialized.ApplyModifiedPropertiesWithoutUndo();

            CreateNpc(
                root, "Forge", NpcServiceType.Crafting, new Vector3(9f, 1f, 7f),
                PrimitiveType.Cube, forge);

            LobbyNpcAuthoring gate = CreateNpc(
                root, "Portal", NpcServiceType.DungeonPortal, new Vector3(0f, 1.5f, 16f),
                PrimitiveType.Cylinder, portal);

            var gateSerialized = new SerializedObject(gate);
            gateSerialized.FindProperty("_targetScene").stringValue = "Dungeon";
            gateSerialized.ApplyModifiedPropertiesWithoutUndo();

            CreateNpc(
                root, "Trainer", NpcServiceType.TrainingGround, new Vector3(0f, 1f, -7f),
                PrimitiveType.Capsule, trainer);

            return root;
        }

        private static LobbyNpcAuthoring CreateNpc(
            GameObject root,
            string name,
            NpcServiceType service,
            Vector3 position,
            PrimitiveType shape,
            Material material)
        {
            GameObject npc = GameObject.CreatePrimitive(shape);
            npc.name = name;
            npc.transform.SetParent(root.transform);
            npc.transform.position = position;
            npc.GetComponent<MeshRenderer>().sharedMaterial = material;

            // The collider has to go. The navmesh below is baked from colliders,
            // and an NPC standing in the middle of the room would punch a hole
            // in it — the same reason the arena strips them off its dummies.
            Object.DestroyImmediate(npc.GetComponent<Collider>());

            LobbyNpcAuthoring authoring = npc.AddComponent<LobbyNpcAuthoring>();

            var serialized = new SerializedObject(authoring);
            serialized.FindProperty("_service").enumValueIndex = (int)service;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return authoring;
        }

        /// <summary>
        /// Five dummies in three arrangements, which is the smallest set that
        /// covers what a player needs to check.
        ///
        /// One standing alone for a straight damage reading, one sliding
        /// sideways so a projectile skill has to lead it, and three in a clump
        /// for anything that chains or bursts. Any fewer and one of those three
        /// questions has nowhere to be asked.
        /// </summary>
        private static GameObject CreateTrainingDummies(Material material)
        {
            var root = new GameObject("TrainingDummies");

            MakeDummy(root, "Dummy_Static", new Vector3(-8f, 0f, -13f), material, 0f);
            MakeDummy(root, "Dummy_Moving", new Vector3(0f, 0f, -13f), material, 5f);

            for (int i = 0; i < 3; i++)
            {
                MakeDummy(
                    root, $"Dummy_Group_{i}",
                    new Vector3(8f + i * 2.2f, 0f, -13f), material, 0f);
            }

            return root;
        }

        private static void MakeDummy(
            GameObject root, string name, Vector3 position, Material material, float patrolDistance)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = name;
            dummy.transform.SetParent(root.transform);
            dummy.transform.position = position;
            dummy.GetComponent<MeshRenderer>().sharedMaterial = material;

            Object.DestroyImmediate(dummy.GetComponent<CapsuleCollider>());

            TrainingDummyAuthoring authoring = dummy.AddComponent<TrainingDummyAuthoring>();

            if (patrolDistance <= 0f)
                return;

            var serialized = new SerializedObject(authoring);
            serialized.FindProperty("_patrolDistance").floatValue = patrolDistance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WriteObjectArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Bakes a navmesh even though nothing in the lobby walks on one.
        ///
        /// The training dummies carry EnemyTag — every targeting query in the
        /// game asks that one question — and the enemy movement systems wake up
        /// for it. They find nothing to move, because a dummy has no
        /// MovementData, but the navmesh query is built regardless, and building
        /// one against a floor with no navmesh is a warning per session for no
        /// reason. One bake is cheaper than an exception nobody can act on.
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
                    "[LobbySceneBuilder] Navmesh bake produced no data. Select Ground and press " +
                    "Bake on its NavMeshSurface to retry.");
                return;
            }

            // Saved as an asset, or the scene would store a reference to runtime
            // data that does not survive a reload.
            string dataPath = $"{NavMeshFolder}/Lobby_NavMesh.asset";
            Directory.CreateDirectory(NavMeshFolder);

            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, dataPath);
        }
    }
}
