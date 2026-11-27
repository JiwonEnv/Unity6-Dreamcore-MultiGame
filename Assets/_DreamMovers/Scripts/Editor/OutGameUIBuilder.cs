using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DreamMovers.Editor
{
    /// <summary>
    /// The out-game screens' panel settings and the scene object that shows them, shared by the menu and game scene
    /// builders. Layouts are the UI_*.uxml files in Art/UI/OutGame (edit them in UI Builder; keep element names).
    /// </summary>
    public static class OutGameUIBuilder
    {
        public const string Folder = ProjectFolders.Root + "/Art/UI/OutGame";
        const string ThemePath = Folder + "/UI_Theme.tss";
        const string PanelSettingsPath = Folder + "/UI_PanelSettings.asset";

        static readonly (string Field, string File)[] Screens =
        {
            ("titleScreen", "UI_Title.uxml"),
            ("mainMenuScreen", "UI_MainMenu.uxml"),
            ("createRoomScreen", "UI_CreateRoom.uxml"),
            ("joinRoomScreen", "UI_JoinRoom.uxml"),
            ("settingsScreen", "UI_Settings.uxml"),
            ("creditsScreen", "UI_Credits.uxml"),
            ("pauseScreen", "UI_Pause.uxml"),
            ("loadingScreen", "UI_Loading.uxml"),
            ("popupScreen", "UI_Popup.uxml"),
        };

        /// <summary>Laid out for 1920 x 1080 and scaled to the screen.</summary>
        public static PanelSettings PanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null)
            {
                return settings;
            }
            AssetDatabase.ImportAsset(ThemePath);
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 10;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            if (settings.themeStyleSheet == null)
            {
                Debug.LogError($"[DreamMovers] theme not found at {ThemePath}");
            }
            return settings;
        }

        /// <summary>Adds the out-game UI object to the open scene.</summary>
        public static DMOutGameUI AddTo()
        {
            var ui = new GameObject("OutGameUI");
            var document = ui.AddComponent<UIDocument>();
            document.panelSettings = PanelSettings();
            var outGame = ui.AddComponent<DMOutGameUI>();
            var serialized = new SerializedObject(outGame);
            foreach ((string Field, string File) screen in Screens)
            {
                string path = $"{Folder}/{screen.File}";
                var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (asset == null)
                {
                    Debug.LogError($"[DreamMovers] out-game layout missing: {path}");
                }
                serialized.FindProperty(screen.Field).objectReferenceValue = asset;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return outGame;
        }

        /// <summary>Menu scene first (the game starts there), then the game scene, then anything else already listed.</summary>
        public static void OrderBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            string[] order = { MainMenuSceneBuilder.ScenePath, ProcGenSceneBuilder.ScenePath };
            for (int i = order.Length - 1; i >= 0; --i)
            {
                string path = order[i];
                scenes.RemoveAll(s => s.path == path);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                {
                    scenes.Insert(0, new EditorBuildSettingsScene(path, true));
                }
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
