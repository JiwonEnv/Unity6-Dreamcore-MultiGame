using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Builds Assets/_DreamMovers/Scenes/MainMenu.unity, the scene the game starts in: a camera, a light and the
    /// out-game UI (title, main menu, room create and join, settings, credits). The backdrop is a plain color until
    /// the title art exists. Menu: Dream Movers/Build Main Menu Scene.
    /// Batch: -executeMethod DreamMovers.Editor.MainMenuSceneBuilder.Build
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        public const string ScenePath = ProjectFolders.Root + "/Scenes/MainMenu.unity";

        [MenuItem("Dream Movers/Build Main Menu Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.1f, 0.14f);

            var sun = new GameObject("Sun", typeof(Light));
            Light light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            OutGameUIBuilder.AddTo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            OutGameUIBuilder.OrderBuildScenes();
            Debug.Log($"[DreamMovers] built {ScenePath}");
        }

        /// <summary>Both scenes, in order (batch: -executeMethod DreamMovers.Editor.MainMenuSceneBuilder.BuildAll).</summary>
        [MenuItem("Dream Movers/Build All Scenes")]
        public static void BuildAll()
        {
            ProcGenSceneBuilder.Build();
            Build();
        }
    }
}
