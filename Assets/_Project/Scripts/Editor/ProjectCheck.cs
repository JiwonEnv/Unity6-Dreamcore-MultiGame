using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Headless check of the project setup. Run with
    /// Unity.exe -batchmode -projectPath . -executeMethod DreamMovers.Editor.ProjectCheck.Run -quit
    /// Exits with code 1 and prints [ProjectCheck] FAIL lines when something is off.
    /// </summary>
    public static class ProjectCheck
    {
        public static void Run()
        {
            var problems = new List<string>();

            if (GraphicsSettings.defaultRenderPipeline is not UniversalRenderPipelineAsset)
                problems.Add("default render pipeline is not URP");

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var rp = QualitySettings.GetRenderPipelineAssetAt(i);
                if (rp != null && rp is not UniversalRenderPipelineAsset)
                    problems.Add($"quality level {QualitySettings.names[i]} uses a non-URP pipeline");
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenes.Count == 0)
                problems.Add("no scene in build settings");
            foreach (var path in scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    problems.Add($"build scene missing: {path}");

            foreach (var p in problems)
                Debug.LogError($"[ProjectCheck] FAIL {p}");
            Debug.Log(problems.Count == 0 ? "[ProjectCheck] PASS" : $"[ProjectCheck] {problems.Count} problem(s)");
            EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }
    }
}
