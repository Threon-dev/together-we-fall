using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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
        public const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string UiFolder = "Assets/_Project/UI";

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

        public static PlayerMotor CreatePlayer(Material material, Vector3 position)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = position;
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
            player.AddComponent<PlayerActionPublisher>();
            return player.AddComponent<PlayerMotor>();
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
            => CreateOrLoadConfig<T>(assetName, DataFolder, out _);

        public static T CreateOrLoadConfig<T>(string assetName, out bool created)
            where T : ScriptableObject
            => CreateOrLoadConfig<T>(assetName, DataFolder, out created);

        /// <summary>
        /// Loads the asset or creates an empty one.
        ///
        /// The created flag matters for assets the builder fills with starter
        /// content: rebuilding a scene must not overwrite a table somebody has
        /// since tuned by hand.
        /// </summary>
        public static T CreateOrLoadConfig<T>(string assetName, string folder, out bool created)
            where T : ScriptableObject
        {
            EnsureAssetFolder(folder);
            string path = $"{folder}/{assetName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
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

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return databaseObject;
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
            var statsObject = new GameObject("CharacterStats");
            CharacterStatsAuthoring authoring = statsObject.AddComponent<CharacterStatsAuthoring>();

            SetReference(authoring, "_config", config);

            return statsObject;
        }

        /// <summary>
        /// The object that draws chain lines and blast rings and knocks the
        /// camera about. Its pooled line renderers are parented to it, so the
        /// whole effect layer is one collapsible entry in the hierarchy.
        /// </summary>
        public static VfxPresenter CreateVfxPresenter(
            VfxConfig config, Material lineMaterial, PanelSettings panelSettings)
        {
            var vfxObject = new GameObject("VfxPresenter");

            UIDocument document = vfxObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            // Behind the inventory, which shares the same panel. Numbers over a
            // crowd must never be the thing covering a panel the player opened.
            document.sortingOrder = -1f;

            VfxPresenter presenter = vfxObject.AddComponent<VfxPresenter>();

            var serialized = new SerializedObject(presenter);
            serialized.FindProperty("_config").objectReferenceValue = config;
            serialized.FindProperty("_lineMaterial").objectReferenceValue = lineMaterial;
            serialized.FindProperty("_document").objectReferenceValue = document;
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
        /// The inventory panel. A UIDocument and the component that fills it —
        /// there is no UXML, because a panel generated entirely from a buffer
        /// would have nothing but an empty container to describe.
        /// </summary>
        public static InventoryUI CreateInventoryUI(PanelSettings panelSettings)
        {
            var uiObject = new GameObject("InventoryUI");

            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            InventoryUI ui = uiObject.AddComponent<InventoryUI>();
            SetReference(ui, "_document", document);

            return ui;
        }

        /// <summary>
        /// The lobby panels: the NPC prompt, the shop, the forge, the portal and
        /// the damage meter.
        ///
        /// Its own UIDocument beside the inventory one rather than a section
        /// inside it. They are shown at different times by different things, and
        /// a panel that has to ask another panel whether it may draw is a
        /// dependency neither of them needs.
        /// </summary>
        public static LobbyUI CreateLobbyUI(PanelSettings panelSettings)
        {
            var uiObject = new GameObject("LobbyUI");

            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            LobbyUI ui = uiObject.AddComponent<LobbyUI>();
            SetReference(ui, "_document", document);

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
        /// Its own UIDocument on the shared panel settings, sorted between the
        /// damage numbers and the inventory: the HUD must sit over the numbers
        /// floating off a crowd, and under the panel the player deliberately
        /// opened. Sorting is the only place that can be said, because the
        /// panels build themselves lazily and in no fixed order.
        /// </summary>
        public static PlayerHud CreatePlayerHud(PanelSettings panelSettings)
        {
            var hudObject = new GameObject("PlayerHud");

            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = -0.5f;

            PlayerHud hud = hudObject.AddComponent<PlayerHud>();
            SetReference(hud, "_document", document);

            return hud;
        }

        /// <summary>
        /// The screen curtain.
        ///
        /// Its own UIDocument on the same panel settings, sorted above
        /// everything else. A fade that the inventory panel can be on top of is
        /// not a fade — and the sorting order is the only place that can be
        /// said, because the panels are built lazily and in no fixed order.
        /// </summary>
        public static CurtainPresenter CreateCurtainPresenter(PanelSettings panelSettings)
        {
            var curtainObject = new GameObject("CurtainPresenter");

            UIDocument document = curtainObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 10f;

            CurtainPresenter presenter = curtainObject.AddComponent<CurtainPresenter>();
            SetReference(presenter, "_document", document);

            return presenter;
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
        /// The panel settings every runtime UI document needs.
        ///
        /// UI Toolkit refuses to render a runtime panel without a theme, so one
        /// is generated alongside it. A .tss importing the built-in default is
        /// exactly what the editor's own "Create > UI Toolkit > TSS Theme File"
        /// produces — this only saves the trip through the menu.
        /// </summary>
        public static PanelSettings CreateOrLoadPanelSettings(string assetName)
        {
            EnsureAssetFolder(UiFolder);
            string path = $"{UiFolder}/{assetName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (existing != null)
                return existing;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = CreateOrLoadRuntimeTheme();

            if (settings.themeStyleSheet == null)
            {
                Debug.LogWarning(
                    "[SceneBuildUtility] Could not create a runtime theme. The inventory panel " +
                    "will not render until a TSS theme is assigned to " + path + ". Create one " +
                    "with Assets > Create > UI Toolkit > TSS Theme File and drag it onto the " +
                    "Theme Style Sheet field.");
            }

            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }

        private static ThemeStyleSheet CreateOrLoadRuntimeTheme()
        {
            string path = $"{UiFolder}/RuntimeTheme.tss";

            var existing = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
            if (existing != null)
                return existing;

            EnsureAssetFolder(UiFolder);
            File.WriteAllText(path, "@import url(\"unity-theme://default\");");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
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
