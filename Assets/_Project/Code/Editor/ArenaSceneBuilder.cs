using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TogetherWeFall.Bootstrap;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Config;
using TogetherWeFall.DebugTools;
using TogetherWeFall.Enemies.Authoring;
using TogetherWeFall.Player;
using TogetherWeFall.Shared.Authoring;
using TogetherWeFall.Spawning.Authoring;

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
    /// </summary>
    public static class ArenaSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        private const string ArtFolder = "Assets/_Project/Art";
        private const string DataFolder = "Assets/_Project/Data";
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        private const string NavMeshFolder = "Assets/_Project/Scenes";

        private const float GroundSize = 60f;
        private const float SpawnRingRadius = 26f;
        private const int SpawnPointCount = 5;

        [MenuItem("Tools/Together We Fall/Build Arena Scene")]
        public static void BuildArenaScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material groundMaterial = CreateMaterial("ArenaGround", new Color(0.22f, 0.24f, 0.28f));
            Material obstacleMaterial = CreateMaterial("ArenaObstacle", new Color(0.40f, 0.34f, 0.30f));
            Material playerMaterial = CreateMaterial("PlayerBody", new Color(0.25f, 0.65f, 0.95f));
            Material enemyMaterial = CreateMaterial("EnemyBody", new Color(0.85f, 0.25f, 0.22f));

            var enemyConfig = CreateOrLoadConfig<EnemyConfig>("EnemyConfig");
            var spawnConfig = CreateOrLoadConfig<SpawnConfig>("SpawnConfig");
            var pathfindingConfig = CreateOrLoadConfig<PathfindingConfig>("PathfindingConfig");
            var separationConfig = CreateOrLoadConfig<SeparationConfig>("SeparationConfig");
            GameObject enemyPrefab = CreateEnemyPrefab(enemyConfig, enemyMaterial);

            CreateLighting();
            GameObject ground = CreateGround(groundMaterial);
            CreateObstacles(obstacleMaterial);

            GameObject spawnPoints = CreateSpawnPoints();
            GameObject waveSpawner = CreateWaveSpawner(enemyPrefab, spawnConfig);
            GameObject simulationSettings = CreateSimulationSettings(pathfindingConfig, separationConfig);

            PlayerMotor player = CreatePlayer(playerMaterial);
            TopDownCameraRig cameraRig = CreateCameraRig();
            GameObject debugTools = CreateDebugTools();
            CreateBootstrap(cameraRig, player, debugTools);

            BuildNavMesh(ground);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();

            AssetDatabase.SaveAssets();

            // Select what has to move into a SubScene. Creating a SubScene from
            // code relies on editor API that cannot be verified by compiling
            // here, while through the menu it is two clicks — so leaving that
            // step to a human is more honest than shipping code that might not
            // build.
            Selection.objects = new Object[] { spawnPoints, waveSpawner, simulationSettings };

            Debug.Log(
                $"[ArenaSceneBuilder] Arena built: {ScenePath}\n" +
                "ONE STEP LEFT: SpawnPoints, WaveSpawner and SimulationSettings are already " +
                "selected in the hierarchy — right-click them, then New Sub Scene > From " +
                "Selection. Without a SubScene they are never baked into entities: no waves " +
                "spawn and enemies receive no pathfinding settings.");
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.40f);
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

        private static GameObject CreateWaveSpawner(GameObject enemyPrefab, SpawnConfig config)
        {
            var spawnerObject = new GameObject("WaveSpawner");
            WaveSpawnerAuthoring spawner = spawnerObject.AddComponent<WaveSpawnerAuthoring>();

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_enemyPrefab").objectReferenceValue = enemyPrefab;
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return spawnerObject;
        }

        /// <summary>
        /// Creates the enemy prefab as an asset. Done here rather than by hand
        /// so the prefab and EnemyConfig are guaranteed to be wired together —
        /// an unassigned reference in the baker yields silent, motionless
        /// enemies, and that cause takes a long time to track down.
        /// </summary>
        private static GameObject CreateEnemyPrefab(EnemyConfig config, Material material)
        {
            Directory.CreateDirectory(PrefabFolder);
            string path = $"{PrefabFolder}/EnemyPrefab.prefab";

            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            source.name = "EnemyPrefab";
            source.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            // No collider needed: enemies do not use physics, and pushing apart
            // is handled by SeparationSystem in stage 4. A redundant collider
            // across 500 entities is pure loss in both memory and time.
            Object.DestroyImmediate(source.GetComponent<CapsuleCollider>());

            EnemyAuthoring authoring = source.AddComponent<EnemyAuthoring>();
            var serialized = new SerializedObject(authoring);
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);

            return prefab;
        }

        private static GameObject CreateSimulationSettings(
            PathfindingConfig pathfinding, SeparationConfig separation)
        {
            var settingsObject = new GameObject("SimulationSettings");
            SimulationSettingsAuthoring settings =
                settingsObject.AddComponent<SimulationSettingsAuthoring>();

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("_pathfinding").objectReferenceValue = pathfinding;
            serialized.FindProperty("_separation").objectReferenceValue = separation;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return settingsObject;
        }

        /// <summary>
        /// Bakes the navmesh and persists the result as an asset.
        ///
        /// The asset step is not optional: BuildNavMesh alone produces runtime
        /// data that lives only in memory, so saving the scene would store a
        /// reference to nothing and enemies would find no paths after a reload.
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

        private static T CreateOrLoadConfig<T>(string assetName) where T : ScriptableObject
        {
            Directory.CreateDirectory(DataFolder);
            string path = $"{DataFolder}/{assetName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static PlayerMotor CreatePlayer(Material material)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);
            player.GetComponent<MeshRenderer>().sharedMaterial = material;

            // CharacterController carries its own capsule collision — the
            // primitive's collider would only duplicate it and cause odd
            // contacts.
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = Vector3.zero;

            player.AddComponent<PlayerInputReader>();
            player.AddComponent<PlayerPositionPublisher>();
            return player.AddComponent<PlayerMotor>();
        }

        private static TopDownCameraRig CreateCameraRig()
        {
            var rigObject = new GameObject("CameraRig");
            Camera camera = rigObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;

            rigObject.AddComponent<AudioListener>();

            TopDownCameraRig rig = rigObject.AddComponent<TopDownCameraRig>();
            SetPrivateReference(rig, "_camera", camera);
            return rig;
        }

        private static GameObject CreateDebugTools()
        {
            var debugObject = new GameObject("DebugTools");
            debugObject.AddComponent<DebugHud>();
            debugObject.AddComponent<DebugSpawnTrigger>();
            return debugObject;
        }

        private static void CreateBootstrap(
            TopDownCameraRig cameraRig, PlayerMotor player, GameObject debugTools)
        {
            var bootstrapObject = new GameObject("GameBootstrap");
            GameBootstrap bootstrap = bootstrapObject.AddComponent<GameBootstrap>();

            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_cameraRig").objectReferenceValue = cameraRig;
            serialized.FindProperty("_player").objectReferenceValue = player;
            serialized.FindProperty("_input").objectReferenceValue =
                player.GetComponent<PlayerInputReader>();
            serialized.FindProperty("_positionPublisher").objectReferenceValue =
                player.GetComponent<PlayerPositionPublisher>();
            serialized.FindProperty("_hud").objectReferenceValue =
                debugTools.GetComponent<DebugHud>();
            serialized.FindProperty("_spawnTrigger").objectReferenceValue =
                debugTools.GetComponent<DebugSpawnTrigger>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Directory.CreateDirectory(ArtFolder);
            string path = $"{ArtFolder}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.color = color;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetPrivateReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}
