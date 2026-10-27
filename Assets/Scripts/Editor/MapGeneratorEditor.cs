using KingOfNothing.World;
using KingOfNothing.World.Generation;
using KingOfNothing.World.Persistence;
using UnityEditor;
using UnityEngine;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Inspector of MapGenerator: original map info and baking, fast layout preview (terrain + biomes as a
    /// picture, with statistics), building the map in the editor, clearing, seed helpers and the edited map file.
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

            DrawOriginalMap(generator);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Preview Layout", "Terrain and biomes only, shown below. Fast; creates nothing.")))
                    Preview(generator, generator.ResolveSeed());
                if (GUILayout.Button(new GUIContent("Preview New Seed", "Preview with a random seed (settings are not changed).")))
                    Preview(generator, Random.Range(0, int.MaxValue));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Build In Scene", "Full map (original + edited map file, or a fresh map in test mode) with tilemaps and objects. Takes a few seconds.")))
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
            else if (!string.IsNullOrEmpty(generator.LastReportText))
            {
                EditorGUILayout.HelpBox($"Last map (seed {generator.LastSeed}):\n{generator.LastReportText}", MessageType.None);
            }
        }

        static void DrawOriginalMap(MapGenerator generator)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Original map (frozen) / edited map (this computer)", EditorStyles.miniBoldLabel);
            var original = generator.OriginalMap;
            if (original == null || !original.HasData)
                EditorGUILayout.HelpBox("No original map baked yet. It is baked automatically the first time the map is built " +
                                        "(Play), or press Bake Original Map. Commit the OriginalMap asset afterwards.", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"Original: seed {original.Seed}, {original.Width}x{original.Height}, {original.ObjectCount} objects, " +
                                        $"baked {original.BakedAt} ({original.SizeKilobytes} KB). Changing the settings does not change it.",
                                        MessageType.None);

            string path = new EditedMapFile(generator.EditedMapFileName).FilePath;
            bool exists = System.IO.File.Exists(path);
            EditorGUILayout.HelpBox((exists ? "Edited map file: " : "No edited map file yet (the map equals the original): ") + path,
                exists ? MessageType.None : MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Bake Original Map", "Generate a new original from the current settings and seed (replaces the frozen one).")))
                {
                    bool hasOriginal = original != null && original.HasData;
                    if (!hasOriginal || EditorUtility.DisplayDialog("Bake original map",
                            "Replace the original map with a new one generated from the current settings (seed " + generator.ResolveSeed() + ")?\n\n" +
                            "Every team member's edited map file belongs to the old original; it will be archived (not applied) the next time the map is built.",
                            "Bake", "Cancel"))
                        BakeWithProgress(generator);
                }
                GUI.enabled = exists && !Application.isPlaying;
                if (GUILayout.Button(new GUIContent("Delete Edited Map", "Back to the original map on this computer (while playing, use Reset in the F2 developer panel).")) &&
                    EditorUtility.DisplayDialog("Delete edited map", "Delete " + path + "?\nThe map goes back to the original.", "Delete", "Cancel"))
                {
                    new EditedMapFile(generator.EditedMapFileName).Delete();
                    if (generator.HasMap) GenerateWithProgress(generator);
                }
                GUI.enabled = true;
                if (GUILayout.Button("Open Saves Folder"))
                {
                    System.IO.Directory.CreateDirectory(EditedMapFile.Folder);
                    EditorUtility.RevealInFinder(EditedMapFile.Folder);
                }
            }
        }

        static void BakeWithProgress(MapGenerator generator)
        {
            try
            {
                generator.BakeOriginal(generator.ResolveSeed(), (step, t) => EditorUtility.DisplayProgressBar("Baking original map", step, t));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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
