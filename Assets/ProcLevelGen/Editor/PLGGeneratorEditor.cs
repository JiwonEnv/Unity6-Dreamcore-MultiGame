using UnityEditor;
using UnityEngine;

namespace ProcLevelGen.Editor
{
    /// <summary>The UE CallInEditor buttons: preview a layout in the scene without playing (not saved with the scene).</summary>
    [CustomEditor(typeof(PLGGenerator))]
    public class PLGGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var generator = (PLGGenerator)target;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview"))
                {
                    Undo.RecordObject(generator, "Preview layout");
                    generator.Preview();
                }
                if (GUILayout.Button("Preview New Seed"))
                {
                    Undo.RecordObject(generator, "Preview new seed");
                    generator.PreviewNewSeed();
                    EditorUtility.SetDirty(generator);
                }
                if (GUILayout.Button("Clear Preview"))
                {
                    generator.ClearPreview();
                }
            }

            if (generator.IsGenerated)
            {
                EditorGUILayout.HelpBox(
                    $"Seed {generator.Seed}: {generator.Layout.Modules.FindAll(m => !m.Removed).Count} modules, " +
                    $"{generator.BoxCount} boxes, layout hash {generator.LayoutHash:x8}", MessageType.None);
            }
        }
    }
}
