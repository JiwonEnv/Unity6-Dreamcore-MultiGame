using ProcLevelGen;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Builds the procedural test scene Assets/_DreamMovers/Scenes/ProcGen.unity, ported from Tools/ue/build_proc_level.py
    /// (tag ue5-final). Only the generator is placed: it assembles the 3 + 2 + 3 module kit from a new seed every run.
    /// The start module always sits at the origin (10 x 10 m, door toward +Z), so the player starts and the extraction
    /// zone are placed there. The scene also holds the network manager, the server's game mode (it fills the map with
    /// antiques, wardrobes and monsters), the HUD and the host/join menu. No oil pastel look yet.
    /// Builds the gameplay prefabs first (Dream Movers > Build Gameplay Prefabs).
    /// Menu: Dream Movers/Build ProcGen Scene. Batch: -executeMethod DreamMovers.Editor.ProcGenSceneBuilder.Build
    /// </summary>
    public static class ProcGenSceneBuilder
    {
        public const string ScenePath = ProjectFolders.Root + "/Scenes/ProcGen.unity";
        const string MaterialFolder = ProjectFolders.Root + "/Art/MaterialLibrary/Graybox";
        const string MaterialPath = MaterialFolder + "/M_Graybox.mat";

        // Dream Movers names for the sample kit (the generator stays game-agnostic).
        static readonly (PLGRole Role, string Name)[] ModuleNames =
        {
            (PLGRole.Start, "EntranceHall"),
            (PLGRole.Hub, "CarouselHall"),
            (PLGRole.End, "AtticBedroom"),
        };
        static readonly string[] ThemeNames = { "TeacupParlor", "GlassGallery", "ClockStudy", "Chapel" };

        [MenuItem("Dream Movers/Build ProcGen Scene")]
        public static void Build()
        {
            GameplayPrefabBuilder.Build();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;

            var generatorObject = new GameObject("LevelGenerator");
            var generator = generatorObject.AddComponent<PLGGenerator>();
            // Off until the network role is known: the server's game mode turns it on, clients build the server's seed.
            generator.enabled = false;
            generatorObject.AddComponent<DMLevelSync>();
            var serialized = new SerializedObject(generator);
            SerializedProperty modules = serialized.FindProperty("modules");
            for (int i = 0; i < modules.arraySize; ++i)
            {
                SerializedProperty module = modules.GetArrayElementAtIndex(i);
                var role = (PLGRole)module.FindPropertyRelative("Role").enumValueIndex;
                foreach ((PLGRole Role, string Name) entry in ModuleNames)
                {
                    if (entry.Role == role)
                    {
                        module.FindPropertyRelative("Name").stringValue = entry.Name;
                    }
                }
            }
            SerializedProperty themes = serialized.FindProperty("themes");
            for (int i = 0; i < themes.arraySize && i < ThemeNames.Length; ++i)
            {
                themes.GetArrayElementAtIndex(i).FindPropertyRelative("Name").stringValue = ThemeNames[i];
            }
            serialized.FindProperty("grayboxMaterial").objectReferenceValue = GrayboxMaterial();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // No preview is built here: play builds its own layout. Use Preview New Seed on the generator to look at layouts.

            // Inside the start module (plan X, Y in [-500, 500] cm); its only door is on plan +X (Unity +Z).
            var starts = new GameObject("PlayerStarts");
            float[] ys = { -180f, -60f, 60f, 180f };
            var startPoints = new Transform[ys.Length];
            for (int i = 0; i < ys.Length; ++i)
            {
                var start = new GameObject($"PlayerStart_{i}");
                start.transform.SetParent(starts.transform, false);
                start.transform.position = PLGSpace.ToUnity(new Vector3(150f, ys[i], 5f));
                startPoints[i] = start.transform;
            }

            // The music box bed, behind the starts (UE map: -250, 0, 100 cm, a 3 x 3 x 2 m box).
            var zone = new GameObject("ExtractionZone");
            zone.transform.position = PLGSpace.ToUnity(new Vector3(-250f, 0f, 100f));
            zone.AddComponent<DMExtractionZone>();

            var networkObject = new GameObject("NetworkManager");
            var network = networkObject.AddComponent<NetworkManager>();
            var transport = networkObject.AddComponent<UnityTransport>();
            network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, ConnectionApproval = false };
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(GameplayPrefabBuilder.PrefabListPath));

            var modeObject = new GameObject("GameMode");
            var mode = modeObject.AddComponent<DMGameMode>();
            var modeSettings = new SerializedObject(mode);
            void Prefab(string field, string path) => modeSettings.FindProperty(field).objectReferenceValue = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
            Prefab("gameStatePrefab", GameplayPrefabBuilder.GameStatePath);
            Prefab("dollPrefab", GameplayPrefabBuilder.DollPath);
            Prefab("valuablePrefab", GameplayPrefabBuilder.ValuablePath);
            Prefab("cottonBallPrefab", GameplayPrefabBuilder.CottonBallPath);
            Prefab("hidingSpotPrefab", GameplayPrefabBuilder.HidingSpotPath);
            Prefab("butlerPrefab", GameplayPrefabBuilder.ButlerPath);
            Prefab("bellHorsePrefab", GameplayPrefabBuilder.BellHorsePath);
            modeSettings.FindProperty("generator").objectReferenceValue = generator;
            SerializedProperty startList = modeSettings.FindProperty("playerStarts");
            startList.arraySize = startPoints.Length;
            for (int i = 0; i < startPoints.Length; ++i)
            {
                startList.GetArrayElementAtIndex(i).objectReferenceValue = startPoints[i];
            }
            modeSettings.ApplyModifiedPropertiesWithoutUndo();

            var ui = new GameObject("UI");
            ui.AddComponent<DMHud>();
            ui.AddComponent<DMConnectMenu>();

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(PLGSpace.ToUnity(new Vector3(-350f, 0f, 170f)), Quaternion.identity);
            cameraObject.AddComponent<DMViewCamera>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);
            Debug.Log($"[DreamMovers] built {ScenePath}");
        }

        static Material GrayboxMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                return material;
            }
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                ProjectFolders.Create();
            }
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Graybox" };
            material.SetFloat("_Smoothness", 0.1f);
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>The generated scene is the startup scene, like Lvl_ProcGen in the UE5 version.</summary>
        static void AddToBuildSettingsFirst(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
