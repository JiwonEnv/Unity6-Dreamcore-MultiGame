using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Creates the project folder layout (grouped by content: each asset keeps its mesh, textures, materials and prefab
    /// in one folder) and moves the existing assets into it. Moves go through the AssetDatabase, so GUIDs and every
    /// reference survive. Safe to run again: existing folders are kept and finished moves are skipped.
    /// Menu: Dream Movers/Create Folder Structure. Batch: -executeMethod DreamMovers.Editor.ProjectFolders.Create
    /// The rules are written down in Docs/Conventions.md.
    /// </summary>
    public static class ProjectFolders
    {
        public const string Root = "Assets/_DreamMovers";
        const string OldRoot = "Assets/_Project";

        /// <summary>Every folder of the layout. Folders that stay empty get a .keep file so git keeps them.</summary>
        static readonly string[] Folders =
        {
            Root + "/Art/Characters/Animations",
            Root + "/Art/Monsters",
            Root + "/Art/Props/Valuables",
            Root + "/Art/Props/Decor",
            Root + "/Art/Environment/Modules",
            Root + "/Art/Environment/Landmarks",
            Root + "/Art/MaterialLibrary/Graybox",
            Root + "/Art/MaterialLibrary/Shaders",
            Root + "/Art/VFX",
            Root + "/Art/UI",
            Root + "/Audio/Music",
            Root + "/Audio/SFX",
            Root + "/Gameplay",
            Root + "/Scenes",
            Root + "/Lighting/Volumes",
            Root + "/Scripts/Runtime",
            Root + "/Scripts/Editor",
            Root + "/Settings/Input",
            "Assets/ThirdParty",
            "Assets/_Sandbox",
        };

        /// <summary>Old path → new path. Whole folders move first so their contents keep their place.</summary>
        static readonly (string From, string To)[] Moves =
        {
            (OldRoot + "/Scripts/Editor", Root + "/Scripts/Editor"),
            (OldRoot + "/Settings", Root + "/Settings"),
            (OldRoot + "/Input/InputSystem_Actions.inputactions", Root + "/Settings/Input/InputSystem_Actions.inputactions"),
            (Root + "/Settings/SampleSceneProfile.asset", Root + "/Lighting/Volumes/SampleSceneProfile.asset"),
            (OldRoot + "/Scenes/ProcGen.unity", Root + "/Scenes/ProcGen.unity"),
            (OldRoot + "/Scenes/Sandbox.unity", "Assets/_Sandbox/Sandbox.unity"),
            (OldRoot + "/Materials/PLG_Graybox.mat", Root + "/Art/MaterialLibrary/Graybox/M_Graybox.mat"),
        };

        [MenuItem("Dream Movers/Create Folder Structure")]
        public static void Create()
        {
            var log = new List<string>();
            // No StartAssetEditing batch: folders created inside one are not visible to IsValidFolder until it ends.
            foreach ((string from, string to) in Moves)
            {
                bool exists = AssetDatabase.IsValidFolder(from) || AssetDatabase.LoadMainAssetAtPath(from) != null;
                if (!exists || AssetDatabase.IsValidFolder(to) || AssetDatabase.LoadMainAssetAtPath(to) != null)
                {
                    continue;
                }
                EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
                string error = AssetDatabase.MoveAsset(from, to);
                log.Add(string.IsNullOrEmpty(error) ? $"moved {from} -> {to}" : $"FAILED {from}: {error}");
            }
            foreach (string folder in Folders)
            {
                EnsureFolder(folder);
            }

            // The old root is only empty folders now.
            if (AssetDatabase.IsValidFolder(OldRoot) && !AssetDatabase.FindAssets("", new[] { OldRoot })
                    .Select(AssetDatabase.GUIDToAssetPath).Any(p => !AssetDatabase.IsValidFolder(p)))
            {
                AssetDatabase.DeleteAsset(OldRoot);
                log.Add($"removed {OldRoot}");
            }

            WriteKeepFiles();
            AssetDatabase.Refresh();
            foreach (string line in log)
            {
                Debug.Log($"[ProjectFolders] {line}");
            }
            Debug.Log("[ProjectFolders] done");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Git drops empty folders (and Unity then drops their .meta), so empty layout folders hold a .keep.</summary>
        static void WriteKeepFiles()
        {
            foreach (string folder in Folders)
            {
                string full = Path.GetFullPath(folder);
                string keep = Path.Combine(full, ".keep");
                bool empty = !Directory.EnumerateFileSystemEntries(full).Any(e => Path.GetFileName(e) != ".keep");
                if (empty && !File.Exists(keep))
                {
                    File.WriteAllText(keep, "");
                }
                else if (!empty && File.Exists(keep))
                {
                    File.Delete(keep);
                }
            }
        }
    }
}
