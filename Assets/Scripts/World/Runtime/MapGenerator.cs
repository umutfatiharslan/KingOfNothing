using System;
using System.Collections.Generic;
using KingOfNothing.CameraControl;
using KingOfNothing.Core;
using KingOfNothing.World.Generation;
using KingOfNothing.World.Persistence;
using UnityEngine;
using UnityEngine.Events;

namespace KingOfNothing.World
{
    /// <summary>
    /// Builds the world when the game starts: loads the ORIGINAL map (a frozen file, see OriginalMapAsset;
    /// baked from the settings the first time), paints the tilemaps, spawns the objects, fills MapData and then
    /// applies the EDITED map file of this computer on top of it (see MapChangeHistory). While playing, every
    /// change is recorded into the edited map file; the original is never touched. Reset To Original deletes the
    /// edited map file. Use the buttons in the Inspector to preview, bake or build the map in the editor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MapData))]
    public class MapGenerator : MonoBehaviour
    {
        public enum SeedSource
        {
            [Tooltip("Use the seed stored in the settings asset (whole team gets the same map).")]
            Settings,
            [Tooltip("New random map every time.")]
            Random,
            [Tooltip("Use Custom Seed below.")]
            Custom,
        }

        const string GeneratedRootName = "[Generated Map]";

        [SerializeField] MapGenerationSettings settings;

        [Header("Seed")]
        [SerializeField] SeedSource seedSource = SeedSource.Settings;
        [SerializeField] int customSeed;

        [Header("Original map")]
        [Tooltip("The frozen original map. If it is empty it is baked from the settings the first time the map is built in the editor.")]
        [SerializeField] OriginalMapAsset originalMap;
        [Tooltip("On = build the frozen original map (+ the edited map file). Off = generate a new map from the settings and " +
                 "the seed every time (test mode: nothing is saved).")]
        [SerializeField] bool useOriginalMap = true;

        [Header("Edited map (saved on this computer)")]
        [Tooltip("Apply the edited map file on top of the original when the map is built.")]
        [SerializeField] bool loadEditedMap = true;
        [Tooltip("Record every change made while playing (both game modes) into the edited map file.")]
        [SerializeField] bool recordChanges = true;
        [Tooltip("File name in the player's data folder (.../Saves). Empty = '<original map name>_edited'.")]
        [SerializeField] string editedMapFileName = "";
        [Tooltip("Seconds after the last change before the file is written (it is always written when the game stops).")]
        [Min(0f)] [SerializeField] float autosaveDelay = 2f;

        [Header("Behaviour")]
        [Tooltip("Build the map when Play mode / the game starts.")]
        [SerializeField] bool generateOnAwake = true;
        [Tooltip("Spawn trees, stones, gold... (turn off for quick terrain tests).")]
        [SerializeField] bool spawnObjects = true;
        [Tooltip("Save a map generated in the editor into the scene. Off = the editor result is only a preview and the scene stays small.")]
        [SerializeField] bool keepEditorResultInScene;
        [SerializeField] bool logReport = true;

        [Header("Scene references (optional)")]
        [Tooltip("Camera whose bounds and background colour are set to the map. Empty = main camera.")]
        [SerializeField] RTSCameraController cameraController;

        [Header("Events")]
        [SerializeField] UnityEvent onMapGenerated = new UnityEvent();

        /// <summary>Raised after a full Generate().</summary>
        public event Action<MapGenerator> MapGenerated;

        public MapGenerationSettings Settings => settings;
        public OriginalMapAsset OriginalMap => originalMap;
        public MapLayout Layout { get; private set; }
        /// <summary>Statistics of the last generation run (null when the map came from the original file).</summary>
        public MapGenerationReport LastReport { get; private set; }
        /// <summary>Statistics of the map that is shown (also for maps loaded from the original file).</summary>
        public string LastReportText { get; private set; }
        public int LastSeed { get; private set; }
        /// <summary>Cell where the exiled party starts (-1 if none was found).</summary>
        public Vector2Int StartCell { get; private set; } = new Vector2Int(-1, -1);
        public bool HasMap => Layout != null;
        public bool KeepEditorResultInScene => keepEditorResultInScene;
        public bool GenerateOnAwake => generateOnAwake;
        /// <summary>The map data that is shown (the original, or a fresh one in test mode).</summary>
        public MapSnapshot CurrentSnapshot { get; private set; }
        /// <summary>True when the shown map is the frozen original (edits are saved).</summary>
        public bool ShowsOriginalMap { get; private set; }
        /// <summary>Undo / redo history and edited map file (null when changes are not saved).</summary>
        public MapChangeHistory History { get; private set; }

        public string EditedMapFileName =>
            !string.IsNullOrWhiteSpace(editedMapFileName) ? editedMapFileName.Trim()
            : (originalMap != null ? originalMap.name : "OriginalMap") + "_edited";

        MapData mapData;
        Transform generatedRoot;
        Transform objectsRoot;
        Transform editsRoot;
        Dictionary<string, SpawnableDefinition> spawnablesById;
        List<SpawnableDefinition> palette;

        public MapData MapData
        {
            get
            {
                if (mapData == null) mapData = GetComponent<MapData>();
                return mapData;
            }
        }

        void Awake()
        {
            if (Application.isPlaying && generateOnAwake) Generate();
        }

        void LateUpdate() => History?.Tick();

        void OnApplicationQuit()
        {
            if (History != null && History.HasUnsavedChanges) History.Save();
        }

        void OnDisable() => StopHistory(true);

        public int ResolveSeed()
        {
            switch (seedSource)
            {
                case SeedSource.Random: return Environment.TickCount ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                case SeedSource.Custom: return customSeed;
                default: return settings != null ? settings.seed : 0;
            }
        }

        /// <summary>Runs only the terrain / biome pipeline (fast; nothing is created). Used for previews.</summary>
        public MapGenerationContext GenerateLayout(int seed, Action<string, float> progress = null)
        {
            if (!CheckSettings()) return null;
            progress?.Invoke("Loading reference map", 0f);
            var config = settings.ToConfig(seed);
            if (config.Source == MapSource.ReferenceMap && config.Reference == null)
            {
                Debug.LogError("[MapGenerator] The reference map could not be loaded (see the Reference Map asset).", settings);
                return null;
            }
            return MapGenerationPipeline.CreateFor(config).Run(config, progress);
        }

        /// <summary>
        /// Generates a complete map (terrain, biomes, start cell and every object) as data, without creating
        /// anything in the scene. This is what the original map file stores.
        /// </summary>
        public MapSnapshot CreateSnapshot(int seed, Action<string, float> progress = null)
        {
            var context = GenerateLayout(seed, progress);
            if (context == null) return null;
            var layout = context.Layout;
            LastReport = context.Report;

            var biomeNames = new string[settings.biomes.Count];
            for (int i = 0; i < biomeNames.Length; i++) biomeNames[i] = settings.biomes[i] != null ? settings.biomes[i].name : "";
            var snapshot = MapSnapshot.FromLayout(layout, seed, biomeNames, context.Report.ToString());

            int startIndex = StartLocationFinder.Find(layout, context.Config, settings.startArea, unchecked(seed + 17));
            if (startIndex >= 0)
            {
                snapshot.StartX = layout.X(startIndex);
                snapshot.StartY = layout.Y(startIndex);
            }

            progress?.Invoke("Placing objects", 0.9f);
            var ruleSet = PlacementRuleSet.Build(settings);
            var reserved = StartLocationFinder.ClearArea(layout, startIndex, settings.startArea.clearRadius);
            var requests = new ObjectPlacementPlanner().Plan(layout, ruleSet.Rules, unchecked(seed * 31 + 7), reserved);

            var typeIndex = new Dictionary<string, int>();
            var types = new List<string>();
            foreach (var request in requests)
            {
                if (request.VariantId < 0 || request.VariantId >= ruleSet.Variants.Count) continue;
                var definition = ruleSet.Variants[request.VariantId];
                if (definition == null) continue;
                if (!typeIndex.TryGetValue(definition.name, out int type))
                {
                    type = types.Count;
                    types.Add(definition.name);
                    typeIndex[definition.name] = type;
                }
                snapshot.Objects.Add(new MapObjectRecord
                {
                    Type = type,
                    Group = request.RuleId,
                    X = request.X,
                    Y = request.Y,
                    OffsetX = request.OffsetX,
                    OffsetY = request.OffsetY,
                });
            }
            snapshot.ObjectTypes = types.ToArray();
            snapshot.GroupLabels = ruleSet.RuleLabels.ToArray();
            return snapshot;
        }

        [ContextMenu("Generate")]
        public void Generate() => Generate(ResolveSeed(), null);

        /// <summary>Builds the map: original (or fresh in test mode), tilemaps, objects, MapData, edited map, camera.</summary>
        public void Generate(int seed, Action<string, float> progress)
        {
            if (!CheckSettings()) return;
            ClearGenerated();
            palette = null;
            spawnablesById = null;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var snapshot = useOriginalMap ? LoadOrBakeOriginal(seed, progress) : CreateSnapshot(seed, progress);
            if (snapshot == null) return;
            ShowsOriginalMap = useOriginalMap && originalMap != null && originalMap.HasData && originalMap.SnapshotId == snapshot.Id;

            bool persist = Application.isPlaying || keepEditorResultInScene;
            int objectCount = BuildWorld(snapshot, progress);
            string editedMap = StartHistory();

            if (!persist) MarkAsEditorPreview(generatedRoot.gameObject);
            SetupCamera();

            if (logReport)
                Debug.Log($"[MapGenerator] Map {Layout.Width}x{Layout.Height}, seed {LastSeed}, " +
                          (ShowsOriginalMap ? "original map" : "freshly generated (not saved)") +
                          $", {objectCount} objects, {editedMap}, {watch.ElapsedMilliseconds} ms\n{LastReportText}", this);

            MapGenerated?.Invoke(this);
            onMapGenerated.Invoke();
        }

        MapSnapshot LoadOrBakeOriginal(int seed, Action<string, float> progress)
        {
#if UNITY_EDITOR
            if (originalMap == null)
            {
                originalMap = OriginalMapAsset.LoadOrCreateDefault();
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            if (originalMap == null)
            {
                Debug.LogError("[MapGenerator] No Original Map assigned; a new map is generated and nothing is saved.", this);
                return CreateSnapshot(seed, progress);
            }
            var snapshot = originalMap.Load();
            if (snapshot != null) return snapshot;
#if UNITY_EDITOR
            Debug.Log($"[MapGenerator] '{originalMap.name}' is empty: baking the original map from the settings (seed {seed}).", originalMap);
            snapshot = CreateSnapshot(seed, progress);
            if (snapshot != null) originalMap.Store(snapshot);
            return snapshot;
#else
            Debug.LogError($"[MapGenerator] '{originalMap.name}' has no map; bake it in the editor. A new map is generated and nothing is saved.", this);
            return CreateSnapshot(seed, progress);
#endif
        }

        /// <summary>Paints and spawns a snapshot. Returns the number of objects.</summary>
        int BuildWorld(MapSnapshot snapshot, Action<string, float> progress)
        {
            var biomeNames = new List<string>();
            foreach (var b in settings.biomes) biomeNames.Add(b != null ? b.name : "");
            var missingBiomes = new List<string>();
            Layout = snapshot.ToLayout(biomeNames, missingBiomes);
            if (missingBiomes.Count > 0)
                Debug.LogWarning("[MapGenerator] Biomes of the map are not in the settings any more (shown as the first biome): " +
                                 string.Join(", ", missingBiomes), this);
            CurrentSnapshot = snapshot;
            LastSeed = snapshot.Seed;
            LastReportText = snapshot.Report;
            StartCell = new Vector2Int(snapshot.StartX, snapshot.StartY);

            MapData.Initialize(Layout, settings.biomes, !Application.isPlaying && keepEditorResultInScene);

            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);

            progress?.Invoke("Painting tilemaps", 0.85f);
            new TilemapPainter().Paint(Layout, settings, generatedRoot);

            if (!spawnObjects) return 0;
            progress?.Invoke("Placing objects", 0.95f);
            return SpawnObjects(snapshot);
        }

        int SpawnObjects(MapSnapshot snapshot)
        {
            objectsRoot = new GameObject("Nature").transform;
            objectsRoot.SetParent(generatedRoot, false);
            editsRoot = null;
            var spawner = new MapObjectSpawner();
            int count = spawner.Spawn(snapshot, FindSpawnable, objectsRoot, MapData);
            if (spawner.MissingTypes.Count > 0)
                Debug.LogWarning("[MapGenerator] Objects of the map are not in the settings any more (skipped): " +
                                 string.Join(", ", spawner.MissingTypes), this);
            return count;
        }

        string StartHistory()
        {
            StopHistory(false);
            if (!ShowsOriginalMap) return "changes are not saved (not the original map)";
            if (!spawnObjects) return "edited map not loaded (objects are off)";
            if (!loadEditedMap) return "edited map not loaded";

            History = new MapChangeHistory(new MapChangeHistory.Setup
            {
                Map = MapData,
                Original = CurrentSnapshot,
                File = new EditedMapFile(EditedMapFileName),
                Resolve = FindSpawnable,
                Parent = () => EditsRoot,
                Mode = () => GameModeManager.IsDeveloperMode ? "Developer" : "Default",
                Record = Application.isPlaying && recordChanges,
                AutosaveDelay = autosaveDelay,
            });
            return History.LoadAndApply();
        }

        void StopHistory(bool save)
        {
            if (History == null) return;
            if (save && History.HasUnsavedChanges) History.Save();
            History.Dispose();
            History = null;
        }

        [ContextMenu("Clear Generated Map")]
        public void ClearGenerated()
        {
            StopHistory(true);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            MapData.Clear();
            Layout = null;
            generatedRoot = null;
            objectsRoot = null;
            editsRoot = null;
            CurrentSnapshot = null;
            ShowsOriginalMap = false;
            StartCell = new Vector2Int(-1, -1);
        }

        // ------------------------------------------------------------------ original / edited map

        /// <summary>
        /// Back to the original map: deletes the edited map file and rebuilds every object from the original
        /// (terrain and characters stay). New changes start a new edited map file.
        /// </summary>
        public void ResetToOriginal()
        {
            if (CurrentSnapshot == null || generatedRoot == null) return;
            if (History != null)
            {
                History.DeleteFile();
                StopHistory(false);
            }
            else new EditedMapFile(EditedMapFileName).Delete();

            if (objectsRoot != null)
            {
                objectsRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(objectsRoot.gameObject);
                else DestroyImmediate(objectsRoot.gameObject);
            }
            MapData.ClearObjects();
            int count = spawnObjects ? SpawnObjects(CurrentSnapshot) : 0;
            StartHistory();
            if (!Application.isPlaying && !keepEditorResultInScene) MarkAsEditorPreview(generatedRoot.gameObject);
            Debug.Log($"[MapGenerator] Map reset to the original ({count} objects); the edited map file was deleted.", this);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Generates the map from the current settings and stores it as the new original (editor only).
        /// The old edited map file no longer matches and is archived the next time the map is built.
        /// </summary>
        public bool BakeOriginal(int seed, Action<string, float> progress = null)
        {
            if (!CheckSettings()) return false;
            if (originalMap == null)
            {
                originalMap = OriginalMapAsset.LoadOrCreateDefault();
                UnityEditor.EditorUtility.SetDirty(this);
            }
            var snapshot = CreateSnapshot(seed, progress);
            if (snapshot == null) return false;
            originalMap.Store(snapshot);
            Debug.Log($"[MapGenerator] New original map baked into '{originalMap.name}': seed {seed}, {snapshot.Objects.Count} objects, " +
                      $"{originalMap.SizeKilobytes} KB.", originalMap);
            return true;
        }
#endif

        // ------------------------------------------------------------------ developer tools

        /// <summary>Every object that can be placed with the developer tools.</summary>
        public IReadOnlyList<SpawnableDefinition> DeveloperPalette
        {
            get
            {
                if (palette == null && settings != null) palette = settings.CollectAllSpawnables();
                return palette ?? new List<SpawnableDefinition>();
            }
        }

        public SpawnableDefinition FindSpawnable(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (spawnablesById == null)
            {
                spawnablesById = new Dictionary<string, SpawnableDefinition>();
                foreach (var s in DeveloperPalette)
                    if (s != null) spawnablesById[s.name] = s;
            }
            return spawnablesById.TryGetValue(id, out var result) ? result : null;
        }

        /// <summary>Parent for objects created by the developer tools and the edited map.</summary>
        public Transform EditsRoot
        {
            get
            {
                if (editsRoot == null)
                {
                    editsRoot = new GameObject("Edited Map Objects").transform;
                    editsRoot.SetParent(objectsRoot != null ? objectsRoot : transform, false);
                }
                return editsRoot;
            }
        }

        bool CheckSettings()
        {
            if (settings == null)
            {
                Debug.LogError("[MapGenerator] No MapGenerationSettings assigned. Run 'King of Nothing > Setup > Create Default Map Assets'.", this);
                return false;
            }
            List<string> problems = settings.Validate();
            if (problems.Count == 0) return true;
            Debug.LogError("[MapGenerator] Settings problems:\n - " + string.Join("\n - ", problems), settings);
            return false;
        }

        /// <summary>Editor preview objects are not saved with the scene (keeps the scene file small).</summary>
        static void MarkAsEditorPreview(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
        }

        void SetupCamera()
        {
            var controller = cameraController != null ? cameraController : (Camera.main != null ? Camera.main.GetComponent<RTSCameraController>() : null);
            var cam = controller != null ? controller.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = settings.waterColor;
            }
            if (controller != null && Layout != null)
            {
                controller.SetBounds(new Rect(-4f, -4f, Layout.Width + 8f, Layout.Height + 8f));
                if (StartCell.x >= 0) controller.FocusOn(MapData.CellCenter(StartCell), true);
            }
        }
    }
}
