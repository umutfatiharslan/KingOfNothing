using KingOfNothing.World;
using KingOfNothing.World.Generation;
using UnityEditor;
using UnityEngine;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Inspector of MapGenerator: fast layout preview (terrain + biomes as a picture, with statistics),
    /// full generation in the editor, clearing, and seed helpers.
    /// </summary>
    [CustomEditor(typeof(MapGenerator))]
    public class MapGeneratorEditor : UnityEditor.Editor
    {
        Texture2D preview;
        string previewReport;
        int previewSeed;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var generator = (MapGenerator)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Map tools", EditorStyles.boldLabel);

            if (generator.Settings == null)
            {
                EditorGUILayout.HelpBox("Assign a MapGenerationSettings asset (King of Nothing > Setup > Create Default Map Assets).", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Preview Layout", "Terrain and biomes only, shown below. Fast; creates nothing.")))
                    Preview(generator, generator.ResolveSeed());
                if (GUILayout.Button(new GUIContent("Preview New Seed", "Preview with a random seed (settings are not changed).")))
                    Preview(generator, Random.Range(0, int.MaxValue));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Generate In Scene", "Full map with tilemaps and objects. Takes a few seconds.")))
                    GenerateWithProgress(generator);
                if (GUILayout.Button("Clear")) generator.ClearGenerated();
            }

            if (preview != null)
            {
                if (GUILayout.Button(new GUIContent($"Use Seed {previewSeed}", "Store the previewed seed in the settings asset (team-wide map).")))
                {
                    Undo.RecordObject(generator.Settings, "Set map seed");
                    generator.Settings.seed = previewSeed;
                    EditorUtility.SetDirty(generator.Settings);
                }

                float width = EditorGUIUtility.currentViewWidth - 40f;
                float height = width * preview.height / preview.width;
                var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
                EditorGUILayout.HelpBox(previewReport, MessageType.None);
            }
            else if (generator.LastReport != null)
            {
                EditorGUILayout.HelpBox($"Last map (seed {generator.LastSeed}):\n{generator.LastReport}", MessageType.None);
            }
        }

        void Preview(MapGenerator generator, int seed)
        {
            try
            {
                var context = generator.GenerateLayout(seed, (step, t) => EditorUtility.DisplayProgressBar("Map preview", step, t));
                if (context == null) return;
                if (preview != null) DestroyImmediate(preview);
                preview = MapPreviewTexture.Render(context.Layout, generator.Settings.biomes, 2);
                previewReport = $"Seed {seed}\n{context.Report}";
                previewSeed = seed;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void GenerateWithProgress(MapGenerator generator)
        {
            try
            {
                generator.Generate(generator.ResolveSeed(), (step, t) => EditorUtility.DisplayProgressBar("Generating map", step, t));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void OnDisable()
        {
            if (preview != null) DestroyImmediate(preview);
        }
    }
}
