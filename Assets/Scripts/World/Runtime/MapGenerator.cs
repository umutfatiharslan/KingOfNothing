using System;
using System.Collections.Generic;
using KingOfNothing.CameraControl;
using KingOfNothing.World.Generation;
using UnityEngine;
using UnityEngine.Events;

namespace KingOfNothing.World
{
    /// <summary>
    /// Builds the world: runs the generation pipeline with the assigned settings, paints the tilemaps,
    /// spawns the map objects and fills MapData. By default it runs when the game starts, so the (huge)
    /// generated content never has to be saved in the scene; the same seed always gives the same map.
    /// Use the buttons in the Inspector to preview or build the map in the editor.
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

        [Header("Behaviour")]
        [Tooltip("Generate the map when Play mode / the game starts.")]
        [SerializeField] bool generateOnAwake = true;
        [Tooltip("Spawn trees, stones, gold... (turn off for quick terrain tests).")]
        [SerializeField] bool spawnObjects = true;
        [Tooltip("Save a map generated in the editor into the scene. Off = the editor result is only a preview and the scene stays small.")]
        [SerializeField] bool keepEditorResultInScene;
        [SerializeField] bool logReport = true;

        [Header("Developer edits")]
        [Tooltip("Objects placed / deleted / moved with the developer tools; replayed after every generation of this map.")]
        [SerializeField] MapEditLog editLog;

        [Header("Scene references (optional)")]
        [Tooltip("Camera whose bounds and background colour are set to the map. Empty = main camera.")]
        [SerializeField] RTSCameraController cameraController;

        [Header("Events")]
        [SerializeField] UnityEvent onMapGenerated = new UnityEvent();

        /// <summary>Raised after a full Generate().</summary>
        public event Action<MapGenerator> MapGenerated;

        public MapGenerationSettings Settings => settings;
        public MapLayout Layout { get; private set; }
        public MapGenerationReport LastReport { get; private set; }
        public int LastSeed { get; private set; }
        /// <summary>Cell where the exiled party starts (-1 if none was found).</summary>
        public Vector2Int StartCell { get; private set; } = new Vector2Int(-1, -1);
        public bool HasMap => Layout != null;
        public bool KeepEditorResultInScene => keepEditorResultInScene;
        public bool GenerateOnAwake => generateOnAwake;

        MapData mapData;
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

        [ContextMenu("Generate")]
        public void Generate() => Generate(ResolveSeed(), null);

        /// <summary>Full generation: layout, tilemaps, objects, MapData, camera.</summary>
        public void Generate(int seed, Action<string, float> progress)
        {
            if (!CheckSettings()) return;
            ClearGenerated();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var context = GenerateLayout(seed, progress);
            if (context == null) return;

            Layout = context.Layout;
            LastReport = context.Report;
            LastSeed = seed;

            bool persist = Application.isPlaying || keepEditorResultInScene;
            MapData.Initialize(Layout, settings.biomes, !Application.isPlaying && keepEditorResultInScene);

            var root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, false);

            progress?.Invoke("Painting tilemaps", 0.85f);
            new TilemapPainter().Paint(Layout, settings, root);

            int startIndex = StartLocationFinder.Find(Layout, context.Config, settings.startArea, unchecked(seed + 17));
            StartCell = startIndex >= 0 ? new Vector2Int(Layout.X(startIndex), Layout.Y(startIndex)) : new Vector2Int(-1, -1);

            int objectCount = 0;
            if (spawnObjects)
            {
                progress?.Invoke("Placing objects", 0.9f);
                var ruleSet = PlacementRuleSet.Build(settings);
                var reserved = StartLocationFinder.ClearArea(Layout, startIndex, settings.startArea.clearRadius);
                var requests = new ObjectPlacementPlanner().Plan(Layout, ruleSet.Rules, unchecked(seed * 31 + 7), reserved);
                objectsRoot = new GameObject("Nature").transform;
                objectsRoot.SetParent(root, false);
                objectCount = new MapObjectSpawner().Spawn(requests, ruleSet, objectsRoot, MapData);
                ApplyEdits();
            }

            if (!persist) MarkAsEditorPreview(root.gameObject);
            SetupCamera();

            if (logReport)
                Debug.Log($"[MapGenerator] Map {Layout.Width}x{Layout.Height}, seed {seed}, {objectCount} objects, " +
                          $"{watch.ElapsedMilliseconds} ms\n{LastReport}", this);

            MapGenerated?.Invoke(this);
            onMapGenerated.Invoke();
        }

        [ContextMenu("Clear Generated Map")]
        public void ClearGenerated()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            MapData.Clear();
            Layout = null;
            objectsRoot = null;
            editsRoot = null;
            StartCell = new Vector2Int(-1, -1);
        }

        // ------------------------------------------------------------------ developer edits

        public MapEditLog EditLog => editLog;

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

        /// <summary>Parent for objects created by the developer tools.</summary>
        public Transform EditsRoot
        {
            get
            {
                if (editsRoot == null)
                {
                    editsRoot = new GameObject("Developer Edits").transform;
                    editsRoot.SetParent(objectsRoot != null ? objectsRoot : transform, false);
                }
                return editsRoot;
            }
        }

        /// <summary>Stores a developer edit so it is replayed on the next generation.</summary>
        public void RecordEdit(MapEdit edit)
        {
            if (editLog == null || Layout == null)
            {
                Debug.LogWarning("[MapGenerator] No Map Edit Log assigned; the change is not saved.", this);
                return;
            }
            editLog.Record(edit, LastSeed, Layout.Width, Layout.Height);
        }

        public void ClearEdits()
        {
            if (editLog != null) editLog.Clear();
        }

        void ApplyEdits()
        {
            if (editLog == null) return;
            editLog.LoadRuntimeEdits();
            if (editLog.Count == 0) return;
            if (!editLog.Matches(LastSeed, Layout.Width, Layout.Height))
            {
                Debug.LogWarning($"[MapGenerator] The map edits belong to seed {editLog.Seed}; they are not applied to seed {LastSeed}.", editLog);
                return;
            }

            int applied = 0, failed = 0;
            foreach (var edit in editLog.Edits)
            {
                if (ApplyEdit(edit)) applied++;
                else failed++;
            }
            Debug.Log($"[MapGenerator] Developer edits applied: {applied}" + (failed > 0 ? $", {failed} could not be applied" : ""), this);
        }

        bool ApplyEdit(MapEdit edit)
        {
            var definition = FindSpawnable(edit.objectId);
            switch (edit.type)
            {
                case MapEditType.Remove:
                {
                    var target = FindEditTarget(edit.cell, edit.objectId);
                    if (target == null) return false;
                    target.Remove();
                    return true;
                }
                case MapEditType.Add:
                    return definition != null && Objects.WorldObjectFactory.Create(definition, edit.cell, Vector2.zero, EditsRoot, MapData) != null;
                case MapEditType.Move:
                {
                    var target = FindEditTarget(edit.cell, edit.objectId);
                    if (target == null || definition == null) return false;
                    target.Remove();
                    return Objects.WorldObjectFactory.Create(definition, edit.targetCell, Vector2.zero, EditsRoot, MapData) != null;
                }
            }
            return false;
        }

        Objects.WorldObject FindEditTarget(Vector2Int cell, string id)
        {
            var target = MapData.GetObjectAt(cell);
            if (target == null || target.Definition == null || target.Definition.name != id) return null;
            return target;
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
