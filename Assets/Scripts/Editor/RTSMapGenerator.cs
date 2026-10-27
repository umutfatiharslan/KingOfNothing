using System.Linq;
using KingOfNothing.CameraControl;
using KingOfNothing.Characters;
using KingOfNothing.Core;
using KingOfNothing.Interaction;
using KingOfNothing.Units;
using KingOfNothing.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Editor menu of the map:
    ///   King of Nothing > Setup > Create Default Map Assets     (tile sets, objects, biomes, settings, villager prefab)
    ///   King of Nothing > Setup > Reset Default Map Assets      (same, but overwrites tuned values)
    ///   King of Nothing > Setup > Create World Scene            (Assets/Scenes/World.unity with everything wired)
    ///   King of Nothing > Map > Generate / Preview / Clear      (on the MapGenerator of the open scene)
    /// The map itself is generated when Play starts, so the scene file stays small and can be committed.
    /// </summary>
    [InitializeOnLoad]
    public static class RTSMapGenerator
    {
        public const string WorldScenePath = "Assets/Scenes/World.unity";
        const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";

        static RTSMapGenerator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>Editor previews are removed before Play so the generator starts from a clean scene.</summary>
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            foreach (var generator in Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Exclude))
                if (generator.GenerateOnAwake && !generator.KeepEditorResultInScene)
                    generator.ClearGenerated();
        }

        // ------------------------------------------------------------------ setup

        [MenuItem("King of Nothing/Setup/Create Default Map Assets", priority = 0)]
        public static void CreateDefaultAssets()
        {
            var settings = DefaultMapAssetsBuilder.Build(false);
            if (settings != null) Selection.activeObject = settings;
        }

        [MenuItem("King of Nothing/Setup/Reset Default Map Assets (overwrite)", priority = 1)]
        public static void ResetDefaultAssets()
        {
            if (!EditorUtility.DisplayDialog("Reset map assets",
                    "All biome, object and map settings assets in " + DefaultMapAssetsBuilder.DataRoot +
                    " are reset to their default values. Continue?", "Reset", "Cancel"))
                return;
            var settings = DefaultMapAssetsBuilder.Build(true);
            if (settings != null) Selection.activeObject = settings;
        }

        [MenuItem("King of Nothing/Setup/Create World Scene", priority = 20)]
        public static void CreateWorldScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var settings = AssetDatabase.LoadAssetAtPath<MapGenerationSettings>(DefaultMapAssetsBuilder.SettingsPath);
            if (settings == null) settings = DefaultMapAssetsBuilder.Build(false);
            if (settings == null) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldScenePath) != null &&
                !EditorUtility.DisplayDialog("World scene", WorldScenePath + " already exists. Replace it?", "Replace", "Cancel"))
                return;

            AssetDatabase.DeleteAsset(WorldScenePath);
            UnityEngine.SceneManagement.Scene scene;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScenePath) != null && AssetDatabase.CopyAsset(TemplateScenePath, WorldScenePath))
                scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, WorldScenePath);
            }

            // Camera
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = settings.waterColor;
            cam.transform.position = new Vector3(settings.width * 0.5f, settings.height * 0.5f, -10f);
            var cameraController = cam.GetComponent<RTSCameraController>();
            if (cameraController == null) cameraController = cam.gameObject.AddComponent<RTSCameraController>();

            // Game systems
            var game = new GameObject("Game");
            game.AddComponent<GameModeManager>();
            var devTools = game.AddComponent<DevInteractionController>();
            new GameObject("Selection Manager").AddComponent<SelectionManager>();

            // World
            var world = new GameObject("World");
            var mapDataComponent = world.AddComponent<MapData>();
            var mapDataSo = new SerializedObject(mapDataComponent);
            mapDataSo.FindProperty("movement").objectReferenceValue = MovementSettings.LoadOrCreateDefault();
            mapDataSo.FindProperty("interaction").objectReferenceValue = InteractionSettings.LoadOrCreateDefault();
            mapDataSo.ApplyModifiedPropertiesWithoutUndo();
            var generator = world.AddComponent<MapGenerator>();
            var so = new SerializedObject(generator);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("cameraController").objectReferenceValue = cameraController;
            so.FindProperty("originalMap").objectReferenceValue = OriginalMapAsset.LoadOrCreateDefault();
            so.ApplyModifiedPropertiesWithoutUndo();

            var devSo = new SerializedObject(devTools);
            devSo.FindProperty("generator").objectReferenceValue = generator;
            devSo.ApplyModifiedPropertiesWithoutUndo();

            // The three exiles
            var partyObject = new GameObject("Starting Party");
            var party = partyObject.AddComponent<StartingPartySpawner>();
            var partySo = new SerializedObject(party);
            partySo.FindProperty("generator").objectReferenceValue = generator;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultMapAssetsBuilder.VillagerPrefabPath);
            partySo.FindProperty("villagerPrefab").objectReferenceValue = prefab != null ? prefab.GetComponent<Villager>() : null;
            partySo.FindProperty("namePool").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NamePool>(DefaultMapAssetsBuilder.NamePoolPath);
            var traits = partySo.FindProperty("traitPool");
            var traitAssets = DefaultMapAssetsBuilder.BuildTraits();
            traits.arraySize = 0;
            foreach (var trait in traitAssets)
            {
                // Only traits that change walking speed, so the difference is visible from the start
                if (trait == null || Mathf.Approximately(trait.MoveSpeedMultiplier, 1f) || trait.Trigger != TraitTrigger.GameStart) continue;
                traits.arraySize++;
                traits.GetArrayElementAtIndex(traits.arraySize - 1).objectReferenceValue = trait;
            }
            partySo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, WorldScenePath);
            AddSceneToBuild(WorldScenePath);
            Selection.activeObject = generator;
            Debug.Log("[King of Nothing] World scene created: " + WorldScenePath + ". Press Play to generate the map.", generator);
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------ map actions

        static MapGenerator FindGenerator()
        {
            var generator = Object.FindAnyObjectByType<MapGenerator>();
            if (generator == null)
                EditorUtility.DisplayDialog("Map", "There is no MapGenerator in the open scene.\nUse King of Nothing > Setup > Create World Scene.", "OK");
            return generator;
        }

        [MenuItem("King of Nothing/Map/Generate In Editor", priority = 40)]
        public static void GenerateInEditor()
        {
            var generator = FindGenerator();
            if (generator != null) MapGeneratorEditor.GenerateWithProgress(generator);
        }

        [MenuItem("King of Nothing/Map/Clear Generated Map", priority = 41)]
        public static void ClearInEditor()
        {
            var generator = FindGenerator();
            if (generator != null) generator.ClearGenerated();
        }
    }
}
