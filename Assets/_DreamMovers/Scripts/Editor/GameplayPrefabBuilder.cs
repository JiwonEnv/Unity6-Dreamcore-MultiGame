using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Editor.Configuration;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Builds the network prefabs the game spawns (in Assets/_DreamMovers/Gameplay) and the prefab list the
    /// NetworkManager registers. Everything visual is made in code at runtime (placeholder shapes, like the UE5
    /// version), so these prefabs only carry components. Safe to run again: prefabs are overwritten in place and
    /// keep their GUIDs. Menu: Dream Movers/Build Gameplay Prefabs.
    /// Batch: -executeMethod DreamMovers.Editor.GameplayPrefabBuilder.Build
    /// </summary>
    public static class GameplayPrefabBuilder
    {
        public const string Folder = ProjectFolders.Root + "/Gameplay";
        public const string PrefabListPath = Folder + "/NetworkPrefabs.asset";

        public const string GameStatePath = Folder + "/GameState.prefab";
        public const string DollPath = Folder + "/DollPlayer.prefab";
        public const string ValuablePath = Folder + "/Valuable.prefab";
        public const string CottonBallPath = Folder + "/CottonBall.prefab";
        public const string HidingSpotPath = Folder + "/HidingSpot.prefab";
        public const string ButlerPath = Folder + "/CandleButler.prefab";
        public const string BellHorsePath = Folder + "/BellHorse.prefab";

        [MenuItem("Dream Movers/Build Gameplay Prefabs")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                ProjectFolders.Create();
            }
            StopDefaultPrefabList();
            var prefabs = new List<GameObject>
            {
                Save(GameStatePath, MakeGameState()),
                Save(DollPath, MakeDoll()),
                Save(ValuablePath, MakeValuable<DMValuable>("Valuable")),
                Save(CottonBallPath, MakeValuable<DMCottonBall>("CottonBall")),
                Save(HidingSpotPath, MakeHidingSpot()),
                Save(ButlerPath, MakeMonster<DMButler>("CandleButler")),
                Save(BellHorsePath, MakeMonster<DMBellHorse>("BellHorse")),
            };

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }
            foreach (NetworkPrefab existing in new List<NetworkPrefab>(list.PrefabList))
            {
                list.Remove(existing);
            }
            foreach (GameObject prefab in prefabs)
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DreamMovers] built {prefabs.Count} gameplay prefabs and {PrefabListPath}");
        }

        /// <summary>
        /// NGO keeps its own list of every network prefab in Assets/DefaultNetworkPrefabs.asset, outside the project
        /// folder. The game registers the list made here instead, so that one is turned off (Project Settings >
        /// Multiplayer > Netcode for GameObjects) and removed.
        /// </summary>
        static void StopDefaultPrefabList()
        {
            NetcodeForGameObjectsProjectSettings settings = NetcodeForGameObjectsProjectSettings.instance;
            if (settings.GenerateDefaultNetworkPrefabs)
            {
                settings.GenerateDefaultNetworkPrefabs = false;
                typeof(NetcodeForGameObjectsProjectSettings).GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(settings, null);
            }
            if (AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(settings.NetworkPrefabsPath) != null)
            {
                AssetDatabase.DeleteAsset(settings.NetworkPrefabsPath);
            }
        }

        static GameObject Save(string path, GameObject root)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            // NGO fills the network ID (GlobalObjectIdHash) in NetworkObject.OnValidate, which a prefab saved from a
            // script (or in batch mode) never gets; without it every prefab has ID 0 and spawning fails.
            var networkObject = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(networkObject, null);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
            return prefab;
        }

        static GameObject MakeGameState()
        {
            var root = new GameObject("GameState");
            root.AddComponent<NetworkObject>();
            root.AddComponent<DMGameState>();
            return root;
        }

        static GameObject MakeDoll()
        {
            var root = new GameObject("DollPlayer");
            // The level generator's checks look for the player by this tag.
            root.tag = "Player";
            root.AddComponent<CharacterController>();
            root.AddComponent<NetworkObject>();
            NetworkTransform sync = root.AddComponent<NetworkTransform>();
            // The owner moves its doll; only position and yaw matter (pitch is a separate synced value).
            sync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            sync.SyncRotAngleX = false;
            sync.SyncRotAngleZ = false;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            root.AddComponent<DMDoll>();
            root.AddComponent<DMGrab>();
            return root;
        }

        static GameObject MakeValuable<T>(string name) where T : DMValuable
        {
            var root = new GameObject(name);
            root.AddComponent<Rigidbody>();
            root.AddComponent<NetworkObject>();
            NetworkTransform sync = root.AddComponent<NetworkTransform>();
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            // Physics runs on the server; clients get a kinematic body that follows.
            root.AddComponent<NetworkRigidbody>();
            root.AddComponent<T>();
            return root;
        }

        static GameObject MakeHidingSpot()
        {
            var root = new GameObject("HidingSpot");
            root.AddComponent<NetworkObject>();
            root.AddComponent<DMHidingSpot>();
            return root;
        }

        static GameObject MakeMonster<T>(string name) where T : DMMonster
        {
            var root = new GameObject(name);
            root.AddComponent<NavMeshAgent>().enabled = false;
            root.AddComponent<NetworkObject>();
            NetworkTransform sync = root.AddComponent<NetworkTransform>();
            sync.SyncRotAngleX = false;
            sync.SyncRotAngleZ = false;
            sync.SyncScaleX = sync.SyncScaleY = sync.SyncScaleZ = false;
            root.AddComponent<T>();
            return root;
        }
    }
}
