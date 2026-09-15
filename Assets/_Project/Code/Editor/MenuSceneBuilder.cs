using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TogetherWeFall.Network;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// Builds the menu: Host, Join, and nothing else.
    ///
    /// No SubScene and no manual step, unlike the other three: the menu has no
    /// entities. It is the one scene that runs before anybody has decided
    /// whether this process is a host or a client, so it has to be the first
    /// scene in the build — NetworkBootstrap treats "started in the menu" as
    /// "do not create any network worlds yet".
    /// </summary>
    public static class MenuSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/" + NetworkBootstrap.MenuScene + ".unity";

        [MenuItem("Tools/Together We Fall/Build Menu Scene")]
        public static void BuildMenuScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("MenuCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f);

            Canvas canvas = SceneBuildUtility.CreateUiCanvas("SessionLauncher", sortingOrder: 20, raycasts: true);
            canvas.gameObject.AddComponent<SessionLauncher>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterFirstInBuildSettings();

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[MenuSceneBuilder] Menu built: {ScenePath}, first in the build settings.\n" +
                "Hosting needs the project linked to Unity Cloud (Edit > Project Settings > " +
                "Services) with Relay enabled on the dashboard. Pressing Play in the Lobby, " +
                "Dungeon or Arena scene still works without any of that: it hosts locally.");
        }

        /// <summary>
        /// Moves the menu to index zero, adding it if missing. The build starts
        /// at index zero, and a build that starts in the lobby would host itself
        /// locally instead of offering a friend a code.
        /// </summary>
        private static void RegisterFirstInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(entry => entry.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
