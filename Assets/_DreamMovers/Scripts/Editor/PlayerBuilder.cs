using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DreamMovers.Editor
{
    /// <summary>
    /// Windows development build (main menu, then the generated map scene), for trying the multiplayer with two copies on one PC
    /// (one hosts, one joins 127.0.0.1). Output: Builds/Windows/DreamMovers.exe (not in git).
    /// Menu: Dream Movers/Build Windows Player. Batch: -executeMethod DreamMovers.Editor.PlayerBuilder.BuildWindows
    /// </summary>
    public static class PlayerBuilder
    {
        public const string OutputPath = "Builds/Windows/DreamMovers.exe";

        [MenuItem("Dream Movers/Build Windows Player")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainMenuSceneBuilder.ScenePath, ProcGenSceneBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DreamMovers] player build {report.summary.result}: {OutputPath}, {report.summary.totalErrors} errors");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
