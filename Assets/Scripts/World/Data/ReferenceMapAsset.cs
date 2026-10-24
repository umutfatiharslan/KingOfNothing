using System.IO;
using KingOfNothing.World.Generation;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// The hand-made reference map (Azgaar .map file) baked to the game grid and stored compressed.
    /// In the editor it re-imports itself automatically when the source file, the map size or the river count
    /// changes; builds use the stored data.
    /// </summary>
    [CreateAssetMenu(fileName = "ReferenceMap", menuName = "King of Nothing/World/Reference Map")]
    public class ReferenceMapAsset : ScriptableObject
    {
        [Tooltip("Azgaar Fantasy Map Generator save file, relative to the project folder.")]
        [SerializeField] string sourcePath = "Assets/Map Source/World Map.map";

        [Header("Baked data (read only)")]
        [SerializeField] int bakedWidth;
        [SerializeField] int bakedHeight;
        [SerializeField] int bakedMaxRivers;
        [SerializeField] float bakedRiverWarp = -1f;
        [SerializeField] float bakedRiverWarpScale;
        [SerializeField] long bakedSourceTicks;
        [SerializeField, HideInInspector] byte[] data;

        [System.NonSerialized] ReferenceMapData cache;

        public string SourcePath => sourcePath;

        /// <summary>Reference data for the given settings; re-imports in the editor when needed. Null if unavailable.</summary>
        public ReferenceMapData GetData(int width, int height, ReferenceMapSettings settings)
        {
            int maxRivers = settings.maxRivers;
            bool upToDate = data != null && data.Length > 0 && bakedWidth == width && bakedHeight == height && bakedMaxRivers == maxRivers
                            && Mathf.Approximately(bakedRiverWarp, settings.riverWarp) && Mathf.Approximately(bakedRiverWarpScale, settings.riverWarpScale);
#if UNITY_EDITOR
            if (upToDate && File.Exists(sourcePath) && File.GetLastWriteTimeUtc(sourcePath).Ticks != bakedSourceTicks)
                upToDate = false;
            if (!upToDate)
            {
                Import(width, height, settings);
                upToDate = data != null && data.Length > 0;
            }
#endif
            if (!upToDate)
            {
                Debug.LogError($"[ReferenceMapAsset] '{name}' has no data for {width}x{height}. Import it in the editor.", this);
                return null;
            }
            if (cache == null || cache.Width != width || cache.Height != height) cache = ReferenceMapData.Deserialize(data);
            return cache;
        }

#if UNITY_EDITOR
        /// <summary>Parses the source file and stores the result in this asset.</summary>
        public bool Import(int width, int height, ReferenceMapSettings settings)
        {
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[ReferenceMapAsset] Source file not found: {sourcePath}", this);
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var parsed = AzgaarMapParser.Parse(File.ReadAllText(sourcePath), width, height, settings.maxRivers,
                settings.riverWarp, settings.riverWarpScale);
            data = parsed.Serialize();
            bakedWidth = width;
            bakedHeight = height;
            bakedMaxRivers = settings.maxRivers;
            bakedRiverWarp = settings.riverWarp;
            bakedRiverWarpScale = settings.riverWarpScale;
            bakedSourceTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
            cache = parsed;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[ReferenceMapAsset] Imported {sourcePath} at {width}x{height} ({data.Length / 1024} KB, {watch.ElapsedMilliseconds} ms).", this);
            return true;
        }

        public void EditorSetSource(string path) => sourcePath = path;
#endif
    }
}
