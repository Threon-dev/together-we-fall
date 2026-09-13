using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TogetherWeFall.Audio;
using TogetherWeFall.Bootstrap;
using TogetherWeFall.CameraRig;
using TogetherWeFall.Combat.Authoring;
using TogetherWeFall.Curtain;
using TogetherWeFall.Config;
using TogetherWeFall.DebugTools;
using TogetherWeFall.Dungeon;
using TogetherWeFall.Enemies.Authoring;
using TogetherWeFall.Loot.Authoring;
using TogetherWeFall.Lobby;
using TogetherWeFall.Player;
using TogetherWeFall.Equipment;
using TogetherWeFall.Equipment.Authoring;
using TogetherWeFall.Skills.Authoring;
using TogetherWeFall.Shared.Authoring;
using TogetherWeFall.Spawning.Authoring;
using TogetherWeFall.UI;
using TogetherWeFall.Vfx;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The parts every generated scene needs: a player, a camera, the debug
    /// overlay, the bootstrap that wires them, and the assets they refer to.
    ///
    /// Extracted the moment a second scene builder appeared. Two copies of the
    /// wiring would not stay identical for long, and the failure mode of a scene
    /// builder that is subtly out of date is a scene that looks right and
    /// behaves differently — the most expensive kind of difference to chase.
    ///
    /// Static, like every editor tool: a MenuItem has to be. The no-statics rule
    /// is about the runtime architecture, which never calls into this file.
    /// </summary>
    public static class SceneBuildUtility
    {
        public const string ArtFolder = "Assets/_Project/Art";
        public const string DataFolder = "Assets/_Project/Data";
        public const string ConfigFolder = DataFolder + "/Config";
        public const string PrefabFolder = "Assets/_Project/Prefabs";

        public static void CreateLighting()
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

        public static PlayerMotor CreatePlayer(Vector3 position)
        {
            var player = new GameObject("Player");
            player.transform.position = position;

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = Vector3.zero;

            PlayerAnimationPresenter animation = player.AddComponent<PlayerAnimationPresenter>();

            // The model is a child, so the motor turns the root and the model
            // only animates. Its origin is at the feet; the controller's is in
            // the middle of the capsule.
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterContentFactory.ModelPrefabPath);

            if (modelPrefab == null)
            {
                Debug.LogError(
                    $"[SceneBuildUtility] No player model at '{CharacterContentFactory.ModelPrefabPath}'. " +
                    "The player will be invisible.");
            }
            else
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, player.transform);
                model.transform.localPosition = new Vector3(0f, -controller.height * 0.5f, 0f);
                model.transform.localRotation = Quaternion.identity;

                CharacterContentFactory.ConvertMaterials(model);

                // Not ??: a missing component is Unity's fake null, not a real one.
                if (!model.TryGetComponent(out Animator animator))
                    animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = CharacterContentFactory.CreateLocomotionController();

                // The motor owns position and facing; clips only pose the body.
                animator.applyRootMotion = false;

                SetReference(animation, "_animator", animator);
            }

            // Its own layer, so the portrait camera can be pointed at the
            // character and see nothing else. Recursive because the model has
            // limbs, and a portrait that quietly renders only the root is the
            // sort of empty box nobody debugs.
            SetLayerRecursively(player, EnsureLayer(CharacterLayer));

            player.AddComponent<PlayerInputReader>();
            player.AddComponent<PlayerPositionPublisher>();
            player.AddComponent<PlayerActionPublisher>();
            return player.AddComponent<PlayerMotor>();
        }

        /// <summary>
        /// The camera that draws the character into the inventory panel.
        ///
        /// A child of the player, so it turns with them and always frames the
        /// front, and culled to the character's layer, so the world it is
        /// standing in is never drawn behind them. It is off until the panel
        /// asks — see CharacterPortrait.
        /// </summary>
        public static CharacterPortrait CreateCharacterPortrait(PlayerMotor player)
        {
            var portraitObject = new GameObject("CharacterPortrait");
            portraitObject.transform.SetParent(player.transform, worldPositionStays: false);

            // Far enough back that a two-metre body fills the frame at this
            // field of view, and level with the middle of it rather than with
            // the eyes: the panel wants the whole character, not a face.
            portraitObject.transform.localPosition = new Vector3(0f, 0.1f, 4f);
            portraitObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Camera camera = portraitObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);

            // The index this returns rather than a lookup by name: the layer may
            // have been written into the project a moment ago.
            camera.cullingMask = 1 << EnsureLayer(CharacterLayer);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 6f;

            // Off until something turns it on, and with no target texture yet:
            // a camera with neither would render this close-up over the game.
            camera.enabled = false;

            CharacterPortrait portrait = portraitObject.AddComponent<CharacterPortrait>();
            SetReference(portrait, "_camera", camera);

            return portrait;
        }

        /// <summary>
        /// The layer the character is on, added to the project the first time a
        /// scene is built.
        ///
        /// Nothing in this project reads layers for physics or raycasts, so this
        /// costs nothing anywhere else — it exists so one camera can be told
        /// what to draw.
        /// </summary>
        public const string CharacterLayer = "Character";

        private static int EnsureLayer(string name)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");

            if (assets.Length == 0)
            {
                Debug.LogWarning(
                    $"[SceneBuildUtility] Could not open the tag manager, so the '{name}' layer " +
                    "was not created. The character portrait will draw the world behind the " +
                    "player.");

                return 0;
            }

            var manager = new SerializedObject(assets[0]);
            SerializedProperty layers = manager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                    return i;
            }

            // Everything below eight belongs to Unity — Default, UI, Water and
            // the rest — and overwriting one of those would be a project-wide
            // change made by a scene builder.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);

                if (!string.IsNullOrEmpty(layer.stringValue))
                    continue;

                layer.stringValue = name;
                manager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            Debug.LogWarning(
                $"[SceneBuildUtility] Every user layer is taken, so '{name}' could not be " +
                "added. Free one, or the character portrait will draw the whole world.");

            return 0;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;

            for (int i = 0; i < target.transform.childCount; i++)
                SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
        }

        public static TopDownCameraRig CreateCameraRig()
        {
            var rigObject = new GameObject("CameraRig");
            Camera camera = rigObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;

            rigObject.AddComponent<AudioListener>();

            TopDownCameraRig rig = rigObject.AddComponent<TopDownCameraRig>();
            SetReference(rig, "_camera", camera);
            return rig;
        }

        public static GameObject CreateDebugTools()
        {
            var debugObject = new GameObject("DebugTools");
            debugObject.AddComponent<DebugHud>();
            debugObject.AddComponent<DebugSpawnTrigger>();
            return debugObject;
        }

        /// <summary>
        /// Wires the bootstrap. The dungeon director is optional — the arena
        /// scene has none, and the bootstrap treats null as "this scene has an
        /// authored floor".
        /// </summary>
        public static void CreateBootstrap(
            TopDownCameraRig cameraRig,
            PlayerMotor player,
            GameObject debugTools,
            DungeonDirector dungeon = null,
            InventoryUI inventoryUI = null,
            LobbyUI lobbyUI = null,
            SceneLoadBridge sceneLoader = null,
            VfxPresenter vfxPresenter = null,
            CurtainPresenter curtainPresenter = null,
            AudioPresenter audioPresenter = null,
            PlayerHud playerHud = null)
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
            serialized.FindProperty("_dungeon").objectReferenceValue = dungeon;
            serialized.FindProperty("_actionPublisher").objectReferenceValue =
                player.GetComponent<PlayerActionPublisher>();
            serialized.FindProperty("_inventoryUI").objectReferenceValue = inventoryUI;
            serialized.FindProperty("_lobbyUI").objectReferenceValue = lobbyUI;
            serialized.FindProperty("_sceneLoader").objectReferenceValue = sceneLoader;
            serialized.FindProperty("_vfxPresenter").objectReferenceValue = vfxPresenter;
            serialized.FindProperty("_curtainPresenter").objectReferenceValue = curtainPresenter;
            serialized.FindProperty("_audioPresenter").objectReferenceValue = audioPresenter;
            serialized.FindProperty("_playerHud").objectReferenceValue = playerHud;
            serialized.FindProperty("_playerAnimation").objectReferenceValue =
                player.GetComponent<PlayerAnimationPresenter>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static GameObject CreateWaveSpawner(GameObject enemyPrefab, SpawnConfig config)
        {
            var spawnerObject = new GameObject("WaveSpawner");
            WaveSpawnerAuthoring spawner = spawnerObject.AddComponent<WaveSpawnerAuthoring>();

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_enemyPrefab").objectReferenceValue = enemyPrefab;
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return spawnerObject;
        }

        public static GameObject CreateSimulationSettings(
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
        /// Creates the enemy prefab as an asset. Done here rather than by hand
        /// so the prefab and EnemyConfig are guaranteed to be wired together —
        /// an unassigned reference in the baker yields silent, motionless
        /// enemies, and that cause takes a long time to track down.
        /// </summary>
        public static GameObject CreateEnemyPrefab(EnemyConfig config, Material material)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            source.name = "EnemyPrefab";
            source.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            // No collider needed: enemies do not use physics, and pushing apart
            // is handled by SeparationSystem. A redundant collider across 500
            // entities is pure loss in both memory and time — and in a generated
            // dungeon it would also be baked into the navmesh as an obstacle.
            Object.DestroyImmediate(source.GetComponent<CapsuleCollider>());

            EnemyAuthoring authoring = source.AddComponent<EnemyAuthoring>();
            SetReference(authoring, "_config", config);

            return SaveAsPrefab(source, "EnemyPrefab");
        }

        /// <summary>
        /// The chest prefab. A box for now — what matters is that it carries the
        /// authoring component, so the entity comes out with its state, its
        /// interaction radius and its flags already in place.
        /// </summary>
        public static GameObject CreateChestPrefab(LootConfig config, Material material)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "ChestPrefab";
            source.transform.localScale = new Vector3(1.1f, 1f, 0.9f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Nothing in this project reads colliders off an entity — there is
            // no Unity Physics — and a collider on a runtime-spawned chest would
            // only be one more component to carry around.
            Object.DestroyImmediate(source.GetComponent<BoxCollider>());

            var authoring = source.AddComponent<ChestAuthoring>();
            SetReference(authoring, "_config", config);

            return SaveAsPrefab(source, "ChestPrefab");
        }

        /// <summary>
        /// The dropped-item prefab. One prefab for every rarity: the colour is
        /// written per instance, so the material here is plain white and the
        /// drop system tints it.
        /// </summary>
        public static GameObject CreateLootItemPrefab(LootConfig config, Material material)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "LootItemPrefab";
            source.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            Object.DestroyImmediate(source.GetComponent<BoxCollider>());

            var authoring = source.AddComponent<LootItemAuthoring>();
            SetReference(authoring, "_config", config);

            return SaveAsPrefab(source, "LootItemPrefab");
        }

        /// <summary>
        /// The projectile prefab. Small, colliderless, and tinted per instance
        /// by the damage type of whatever fired it.
        /// </summary>
        public static GameObject CreateProjectilePrefab(Material material)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "SkillProjectilePrefab";
            source.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Projectiles find their targets by distance, not by physics. A
            // collider would be one more component on something the game
            // creates and destroys by the dozen.
            Object.DestroyImmediate(source.GetComponent<BoxCollider>());

            source.AddComponent<SkillProjectileAuthoring>();

            return SaveAsPrefab(source, "SkillProjectilePrefab");
        }

        /// <summary>
        /// The persistent-zone prefab: a disc lying on the ground, tinted per
        /// instance by whichever element lit it.
        ///
        /// Flattened on the GameObject rather than at spawn time, because
        /// LocalTransform carries one uniform scale and a zone needs to be wide
        /// and thin. A non-uniform scale here bakes into a transform matrix of
        /// its own, which the uniform scale the pool writes then multiplies —
        /// so setting the diameter stays one number, and the disc stays flat.
        ///
        /// Colliderless, like everything else here: overlap with a zone is a
        /// distance to its centre, and there is no Unity Physics to consult.
        /// A collider would also be baked into the dungeon navmesh as an
        /// obstacle, which is the last thing a patch of fire should be.
        /// </summary>
        public static GameObject CreateZonePrefab(Material material)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            source.name = "ElementZonePrefab";

            // A cylinder primitive is one unit across and two tall, so this is a
            // disc of diameter one — which makes the scale the pool writes the
            // diameter, with no conversion anybody has to remember.
            source.transform.localScale = new Vector3(1f, 0.02f, 1f);
            source.GetComponent<MeshRenderer>().sharedMaterial = material;

            Object.DestroyImmediate(source.GetComponent<CapsuleCollider>());

            source.AddComponent<ElementZoneAuthoring>();

            return SaveAsPrefab(source, "ElementZonePrefab");
        }

        private static GameObject SaveAsPrefab(GameObject source, string assetName)
        {
            EnsureAssetFolder(PrefabFolder);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                source, $"{PrefabFolder}/{assetName}.prefab");

            Object.DestroyImmediate(source);
            return prefab;
        }

        public static T CreateOrLoadConfig<T>(string assetName) where T : ScriptableObject
            => CreateOrLoadConfig<T>(assetName, ConfigFolder, out _);

        public static T CreateOrLoadConfig<T>(string assetName, out bool created)
            where T : ScriptableObject
            => CreateOrLoadConfig<T>(assetName, ConfigFolder, out created);

        /// <summary>
        /// Loads the asset or creates an empty one.
        ///
        /// The created flag matters for assets the builder fills with starter
        /// content: rebuilding a scene must not overwrite a table somebody has
        /// since tuned by hand.
        ///
        /// The folder is only where a NEW asset lands. An existing one is found
        /// by name anywhere under Data, so assets can be filed into subfolders
        /// by hand — otherwise the next build would make a second copy at the
        /// old path, with the same name and therefore the same id.
        /// </summary>
        public static T CreateOrLoadConfig<T>(string assetName, string folder, out bool created)
            where T : ScriptableObject
        {
            T existing = LoadConfig<T>(assetName);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            EnsureAssetFolder(folder);
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, $"{folder}/{assetName}.asset");
            created = true;
            return asset;
        }

        /// <summary>One asset by name, wherever under Data it is filed, or null.</summary>
        public static T LoadConfig<T>(string assetName) where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets(
                $"t:{typeof(T).Name} {assetName}", new[] { DataFolder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets matches names loosely — "SupportHemorrhage" also
                // finds "SupportHemorrhageWeak".
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetName)
                    return AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return null;
        }

        /// <summary>
        /// Appends one line to a starter kit.
        ///
        /// Shared because two factories fill the kit — the library from the
        /// Tools menu and the coins from the lobby build — and an entry is three
        /// fields, which is three chances for the two of them to disagree about
        /// what a default is.
        /// </summary>
        public static void AppendKitEntry(
            SerializedProperty entries, ItemDefinition item, bool worn, int count)
        {
            if (entries == null || item == null)
                return;

            entries.arraySize++;

            SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("_item").objectReferenceValue = item;
            entry.FindPropertyRelative("_placement").enumValueIndex =
                (int)(worn ? StarterKitPlacement.Worn : StarterKitPlacement.Bag);
            entry.FindPropertyRelative("_count").intValue = Mathf.Max(1, count);
        }

        /// <summary>
        /// Appends an item to a loot table if it is not already in it.
        ///
        /// Additive on purpose, and shared because two factories now need it.
        /// The table is hand-tuned by now: a factory that rewrote it would throw
        /// away weights somebody chose, and one that skipped it entirely would
        /// leave an item that exists on disk and can never be found — which is
        /// indistinguishable, in play, from the feature not being there.
        /// </summary>
        public static void EnsureInLootTable(LootTable table, ItemDefinition item)
        {
            if (table == null || item == null)
                return;

            var serialized = new SerializedObject(table);
            SerializedProperty entries = serialized.FindProperty("_entries");

            for (int i = 0; i < entries.arraySize; i++)
            {
                Object existing = entries.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_item").objectReferenceValue;

                if (existing == item)
                    return;
            }

            entries.arraySize++;

            SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("_item").objectReferenceValue = item;
            added.FindPropertyRelative("_weight").floatValue = 1f;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Material CreateMaterial(string name, Color color)
        {
            EnsureAssetFolder(ArtFolder);
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

        /// <summary>
        /// The object that carries every skill and its supports into ECS. Goes
        /// into the SubScene with the rest: without baking there is no blob, and
        /// without the blob nothing can be cast.
        ///
        /// It no longer sets a starting loadout. Skills live in gems now, so
        /// what a character can cast is decided by what is socketed — the list
        /// here is the catalogue of what exists, not a choice about anybody.
        /// </summary>
        public static GameObject CreateSkillDatabase(
            SkillDefinition[] skills, GameObject projectilePrefab, GameObject zonePrefab)
        {
            var databaseObject = new GameObject("SkillDatabase");
            SkillDatabaseAuthoring authoring = databaseObject.AddComponent<SkillDatabaseAuthoring>();

            var serialized = new SerializedObject(authoring);
            serialized.FindProperty("_projectilePrefab").objectReferenceValue = projectilePrefab;
            serialized.FindProperty("_zonePrefab").objectReferenceValue = zonePrefab;

            SerializedProperty list = serialized.FindProperty("_skills");
            list.arraySize = skills.Length;

            for (int i = 0; i < skills.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = skills[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return databaseObject;
        }

        /// <summary>
        /// The object that carries the loot tables and prefabs into ECS. Goes
        /// into the SubScene with the wave spawner: without baking there is no
        /// blob, and without the blob nothing can drop.
        ///
        /// Both scenes carry one, and the arena needs it for the half that is
        /// not about dropping at all: the SAME bake produces the item database,
        /// and without that there are no gems, so nothing anywhere is castable.
        /// The arena still has no chests — nothing places any without a dungeon —
        /// so what it actually gains is the item pool and the database.
        /// </summary>
        public static GameObject CreateLootDatabase(
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

            WriteSets(serialized.FindProperty("_sets"));

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return databaseObject;
        }

        /// <summary>
        /// Every item set in the project, wired into the bake.
        ///
        /// Found rather than listed, which is the one place in these builders
        /// that scans instead of naming names — and deliberately, because the
        /// point of a set being one asset is that a designer creates it and it
        /// works. A list here would mean a new set needs a code change to be
        /// baked, which is exactly what the asset exists to avoid. "Which items
        /// exist" is still a content decision; "which sets are in the game" is
        /// every set asset there is.
        ///
        /// The demo set is ensured first, so the mechanism exists in content on
        /// a fresh project rather than only in code.
        /// </summary>
        private static void WriteSets(SerializedProperty list)
        {
            ItemContentFactory.CreateOrLoadDemoSet();

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ItemSetDefinition)}");
            list.arraySize = guids.Length;

            for (int i = 0; i < guids.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<ItemSetDefinition>(
                        AssetDatabase.GUIDToAssetPath(guids[i]));
            }
        }

        /// <summary>
        /// The object that carries the element reaction table into ECS. Goes
        /// into the SubScene with the rest: without baking there is no database,
        /// and both element systems require one — a scene missing this plays
        /// exactly as it did before statuses existed.
        /// </summary>
        public static GameObject CreateElementReactionDatabase(ElementReactionTable table)
        {
            var databaseObject = new GameObject("ElementReactions");
            var authoring = databaseObject.AddComponent<ElementReactionAuthoring>();

            SetReference(authoring, "_table", table);

            return databaseObject;
        }

        /// <summary>
        /// The object that carries the character sheet everyone starts from.
        /// Goes into the SubScene: without baking there is no CharacterBaseStats
        /// singleton, and the stat system will not run at all.
        /// </summary>
        public static GameObject CreateCharacterStats(CharacterConfig config)
        {
            // The sheet gains the stats a new system needs, once, before it is
            // baked. Additive and idempotent, exactly like the starter coins:
            // a sheet somebody has tuned keeps every number they set, and one
            // written before crits existed stops meaning "never crits, and no
            // item can fix it".
            //
            // Here rather than in each scene builder because all three call
            // this, and a rule about what a character sheet must contain should
            // not be written down three times.
            EnsureBaseStat(config, StatKind.CritChance, 5f);      // TUNE
            EnsureBaseStat(config, StatKind.CritMultiplier, 1.5f);  // TUNE

            var statsObject = new GameObject("CharacterStats");
            CharacterStatsAuthoring authoring = statsObject.AddComponent<CharacterStatsAuthoring>();

            SetReference(authoring, "_config", config);

            // The loadout, as its own asset. Created empty on a fresh project
            // and filled by the content factories — the coins in the lobby, the
            // library from the Tools menu — so the reference is wired once here
            // and every scene gets the same kit until somebody points one at a
            // copy.
            SetReference(
                authoring, "_starterKit", CreateOrLoadConfig<StarterKitConfig>("StarterKitConfig"));

            return statsObject;
        }

        /// <summary>
        /// Adds a base stat to the character sheet unless it already names one.
        ///
        /// Never overwrites: a value somebody tuned by hand is the whole reason
        /// these are assets. What it fixes is the other case — a sheet authored
        /// before a stat existed, where the missing line is not a missing number
        /// but a system that silently never does anything.
        /// </summary>
        private static void EnsureBaseStat(CharacterConfig config, StatKind stat, float value)
        {
            if (config == null)
                return;

            var serialized = new SerializedObject(config);
            SerializedProperty stats = serialized.FindProperty("_baseStats");

            for (int i = 0; i < stats.arraySize; i++)
            {
                if (stats.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("_stat").enumValueIndex == (int)stat)
                {
                    return;
                }
            }

            stats.arraySize++;

            SerializedProperty added = stats.GetArrayElementAtIndex(stats.arraySize - 1);
            added.FindPropertyRelative("_stat").enumValueIndex = (int)stat;
            added.FindPropertyRelative("_value").floatValue = value;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The object that draws chain lines and blast rings and knocks the
        /// camera about. Its pooled line renderers are parented to it, so the
        /// whole effect layer is one collapsible entry in the hierarchy.
        /// </summary>
        public static VfxPresenter CreateVfxPresenter(VfxConfig config, Material lineMaterial)
        {
            // Behind everything else, and unclickable. Numbers over a crowd must
            // never be the thing covering a panel the player opened, and they
            // must never be the thing that ate a cast.
            Canvas canvas = CreateUiCanvas("VfxPresenter", sortingOrder: -2, raycasts: false);

            GameObject vfxObject = canvas.gameObject;
            VfxPresenter presenter = vfxObject.AddComponent<VfxPresenter>();

            // The authored sets: created where they do not exist yet, then
            // listed for the presenter. Found rather than named, like the item
            // sets and for the same reason — a new set should be an asset
            // somebody makes, not a line somebody adds here.
            SkillVfxContentFactory.CreateOrLoadSets();
            SkillVfxSet[] sets = SkillVfxContentFactory.LoadAll();

            var serialized = new SerializedObject(presenter);
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.FindProperty("_lineMaterial").objectReferenceValue = lineMaterial;
            serialized.FindProperty("_canvas").objectReferenceValue = canvas;

            SerializedProperty list = serialized.FindProperty("_skillVfx");
            list.arraySize = sets.Length;

            for (int i = 0; i < sets.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = sets[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return presenter;
        }

        /// <summary>
        /// The material the effect lines are drawn with.
        ///
        /// Unlit, and picked from a chain of fallbacks because which shaders a
        /// project has depends on its render pipeline. The pool tints each line
        /// through a property block AND through vertex colours, so whichever of
        /// these turns up, one of the two paths colours it.
        /// </summary>
        public static Material CreateVfxLineMaterial(string name)
        {
            EnsureAssetFolder(ArtFolder);
            string path = $"{ArtFolder}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Color");

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", Color.white);
            material.color = Color.white;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The inventory panel. A canvas and the component that fills it — there
        /// are no prefabs, because a panel generated entirely from a buffer would
        /// have nothing but an empty container to author.
        ///
        /// Over the lobby's panels: this is the one the player opens on purpose,
        /// and it can be open while standing in a shop.
        /// </summary>
        public static InventoryUI CreateInventoryUI(CharacterPortrait portrait)
        {
            Canvas canvas = CreateUiCanvas("InventoryUI", sortingOrder: 1, raycasts: true);

            InventoryUI ui = canvas.gameObject.AddComponent<InventoryUI>();
            SetReference(ui, "_canvas", canvas);
            SetReference(ui, "_portrait", portrait);

            // Every item with art, found rather than named: an icon is set on
            // the asset, and should not also need a line here.
            var serialized = new SerializedObject(ui);
            SerializedProperty icons = serialized.FindProperty("_icons");
            icons.arraySize = 0;

            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(ItemDefinition)}", new[] { DataFolder }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.Icon == null)
                    continue;

                icons.arraySize++;
                SerializedProperty entry = icons.GetArrayElementAtIndex(icons.arraySize - 1);
                entry.FindPropertyRelative("ItemId").intValue = item.ItemId;
                entry.FindPropertyRelative("Sprite").objectReferenceValue = item.Icon;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return ui;
        }

        /// <summary>
        /// The lobby panels: the NPC prompt, the shop, the forge, the portal and
        /// the damage meter.
        ///
        /// Its own canvas beside the inventory one rather than a section inside
        /// it. They are shown at different times by different things, and a panel
        /// that has to ask another panel whether it may draw is a dependency
        /// neither of them needs.
        /// </summary>
        public static LobbyUI CreateLobbyUI()
        {
            Canvas canvas = CreateUiCanvas("LobbyUI", sortingOrder: 0, raycasts: true);

            LobbyUI ui = canvas.gameObject.AddComponent<LobbyUI>();
            SetReference(ui, "_canvas", canvas);

            return ui;
        }

        /// <summary>
        /// The one object allowed to swap a scene. Everything that decides WHEN
        /// happens in ECS; this only does it.
        /// </summary>
        public static SceneLoadBridge CreateSceneLoadBridge()
        {
            var bridgeObject = new GameObject("SceneLoadBridge");
            return bridgeObject.AddComponent<SceneLoadBridge>();
        }

        /// <summary>
        /// The orbs and the skill bar.
        ///
        /// Its own canvas, sorted between the damage numbers and the inventory:
        /// the HUD must sit over the numbers floating off a crowd, and under the
        /// panel the player deliberately opened. Sorting is the only place that
        /// can be said, because the panels build themselves lazily and in no
        /// fixed order.
        ///
        /// No raycaster, which is the whole of what `pickingMode = Ignore` used
        /// to say on every element the HUD has: a readout is not a surface, and
        /// a canvas nothing can hit says that once.
        /// </summary>
        public static PlayerHud CreatePlayerHud()
        {
            Canvas canvas = CreateUiCanvas("PlayerHud", sortingOrder: -1, raycasts: false);

            PlayerHud hud = canvas.gameObject.AddComponent<PlayerHud>();
            SetReference(hud, "_canvas", canvas);

            return hud;
        }

        /// <summary>
        /// The screen curtain.
        ///
        /// Its own canvas, sorted above everything else. A fade that the
        /// inventory panel can be on top of is not a fade — and the sorting
        /// order is the only place that can be said, because the panels are
        /// built lazily and in no fixed order.
        /// </summary>
        public static CurtainPresenter CreateCurtainPresenter()
        {
            Canvas canvas = CreateUiCanvas("CurtainPresenter", sortingOrder: 10, raycasts: false);

            CurtainPresenter presenter = canvas.gameObject.AddComponent<CurtainPresenter>();
            SetReference(presenter, "_canvas", canvas);

            return presenter;
        }

        /// <summary>
        /// A screen-space canvas for one screen, scaled exactly the way the UI
        /// Toolkit panel was: 1200×800 reference, matched on width.
        ///
        /// One canvas per screen rather than one shared canvas, because that is
        /// what the panels already assumed — each drew into its own document and
        /// said where it sat with a sorting order. A shared canvas would make
        /// every rebuild of the inventory dirty the HUD's mesh as well, which is
        /// the one thing separate canvases are actually for.
        ///
        /// `raycasts: false` leaves the raycaster off, and nothing on the canvas
        /// can then be clicked at all. That is the right answer for anything
        /// that only reports — the HUD, the curtain, floating numbers — and the
        /// wrong one for anything with a button on it.
        /// </summary>
        public static Canvas CreateUiCanvas(string name, int sortingOrder, bool raycasts)
        {
            RequireTextMeshPro();
            EnsureEventSystem();

            var canvasObject = new GameObject(name);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1200f, 800f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            if (raycasts)
                canvasObject.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>
        /// Shouts if TextMeshPro's resources have never been imported.
        ///
        /// Without them every label in the game draws nothing and the console
        /// fills with TMP's own complaints — which reads as a broken scene rather
        /// than as a missing one-time import. The package ships them as a
        /// .unitypackage, so there is no way to depend on them; the best that can
        /// be done is to say exactly which menu item fixes it.
        /// </summary>
        /// <summary>
        /// The one object that turns a click into a uGUI event.
        ///
        /// One per scene, found rather than counted: three builders and five
        /// canvases all need it and none of them owns it. Without it the panels
        /// draw and nothing in them can be pressed — which looks like broken
        /// panels rather than like a missing object.
        ///
        /// The Input System's module rather than the legacy one: the project is on
        /// the new handler, and StandaloneInputModule throws at runtime there.
        /// Its actions are left empty on purpose — the module assigns its own
        /// defaults in OnEnable, and a generated asset saved into a scene would be
        /// one more thing to keep in step.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            var eventObject = new GameObject("EventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<InputSystemUIInputModule>();
        }

        private static void RequireTextMeshPro()
        {
            if (TMPro.TMP_Settings.instance != null)
                return;

            Debug.LogError(
                "[SceneBuildUtility] TextMeshPro resources are missing, so no UI text will " +
                "render. Import them once with Window > TextMeshPro > Import TMP Essential " +
                "Resources, then build the scene again.");
        }

        /// <summary>
        /// The object that plays what the simulation announced. Its pooled
        /// AudioSources are parented to it, so the whole voice bank is one
        /// collapsible entry in the hierarchy.
        /// </summary>
        public static AudioPresenter CreateAudioPresenter(AudioConfig config)
        {
            var audioObject = new GameObject("AudioPresenter");

            AudioPresenter presenter = audioObject.AddComponent<AudioPresenter>();
            SetReference(presenter, "_config", config);

            return presenter;
        }

        /// <summary>
        /// The audio config, with a row for every cue the simulation can name.
        ///
        /// Filled in on creation and never touched again, the same way the
        /// sample items are: a table with eight empty rows is something a
        /// designer drops clips into, and an empty table is something they have
        /// to work out the shape of first. No clips are assigned — there are
        /// none in the project — so every cue is silent until one is, which is
        /// a normal state rather than an error.
        /// </summary>
        public static AudioConfig CreateAudioConfig(string assetName)
        {
            AudioConfig config = CreateOrLoadConfig<AudioConfig>(assetName, out bool created);

            if (!created)
                return config;

            // cue, volume, spatial, voices per frame, minimum gap
            //
            // The gaps are the interesting column. Ungated, a sustained chain
            // reaction starts a death sound a hundred and twenty times a
            // second, which is not a hundred and twenty deaths — it is one
            // clipped tone. At 0.09 it is ten a second, which still reads as
            // carnage and still reads as individual bodies. That number is
            // measured; the other two are the same reasoning applied to
            // shorter sounds and want a pass with actual clips in them.
            var defaults = new (AudioCue cue, float volume, bool spatial, int voices, float gap)[]
            {
                (AudioCue.SkillCast, 0.6f, true, 4, 0.03f),
                (AudioCue.ProjectileImpact, 0.5f, true, 4, 0.03f),
                (AudioCue.Explosion, 0.9f, true, 2, 0.10f),
                (AudioCue.ChainZap, 0.6f, true, 3, 0.06f),
                (AudioCue.EnemyDeath, 0.7f, true, 3, 0.09f),
                (AudioCue.ChestOpen, 0.9f, true, 1, 0f),
                (AudioCue.ItemPickup, 0.8f, false, 2, 0f),
                (AudioCue.UiClick, 0.6f, false, 3, 0f)
            };

            var serialized = new SerializedObject(config);
            SerializedProperty cues = serialized.FindProperty("_cues");
            cues.arraySize = defaults.Length;

            for (int i = 0; i < defaults.Length; i++)
            {
                SerializedProperty element = cues.GetArrayElementAtIndex(i);

                element.FindPropertyRelative("_cue").enumValueIndex = (int)defaults[i].cue;
                element.FindPropertyRelative("_volume").floatValue = defaults[i].volume;
                element.FindPropertyRelative("_spatial").boolValue = defaults[i].spatial;
                element.FindPropertyRelative("_maxVoicesPerFrame").intValue = defaults[i].voices;
                element.FindPropertyRelative("_minIntervalSeconds").floatValue = defaults[i].gap;
                element.FindPropertyRelative("_pitchRange").vector2Value =
                    new Vector2(0.95f, 1.05f);
                element.FindPropertyRelative("_clips").arraySize = 0;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        /// <summary>
        /// Creates a project folder if it is missing, parents first.
        ///
        /// Through the AssetDatabase rather than Directory.CreateDirectory: a
        /// folder made behind the database's back does not exist as far as
        /// CreateAsset is concerned, and the asset written into it fails with a
        /// message about an invalid path rather than about a missing folder.
        /// </summary>
        public static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            int separator = folder.LastIndexOf('/');
            if (separator <= 0)
                return;

            string parent = folder.Substring(0, separator);
            string leaf = folder.Substring(separator + 1);

            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static void SetReference(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Adds the scene to the build list if it is not already there. Appending
        /// rather than replacing, because the project now has two generated
        /// scenes and rebuilding one must not evict the other.
        /// </summary>
        public static void RegisterInBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scenePath)
                    return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            System.Array.Copy(scenes, updated, scenes.Length);
            updated[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);

            EditorBuildSettings.scenes = updated;
        }
    }
}
