using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KingOfNothing.World
{
    public enum MapEditType
    {
        /// <summary>An object was deleted.</summary>
        Remove,
        /// <summary>An object was placed.</summary>
        Add,
        /// <summary>An object was moved to another cell.</summary>
        Move,
    }

    /// <summary>One change made with the developer tools.</summary>
    [Serializable]
    public class MapEdit
    {
        public MapEditType type;
        [Tooltip("Asset name of the object's SpawnableDefinition.")]
        public string objectId;
        public Vector2Int cell;
        [Tooltip("Move only: the new cell.")]
        public Vector2Int targetCell;
    }

    /// <summary>
    /// Changes made to the map with the developer tools (place / delete / move objects). The generator replays
    /// them after every generation of the same map, so the edits are permanent: in the editor they are saved into
    /// this asset (they survive leaving Play mode and can be committed); in builds they are saved as JSON.
    /// The log belongs to one seed and map size; it is ignored (with a warning) for other maps.
    /// </summary>
    [CreateAssetMenu(fileName = "MapEdits", menuName = "King of Nothing/World/Map Edit Log")]
    public class MapEditLog : ScriptableObject
    {
        [SerializeField] int seed;
        [SerializeField] int width;
        [SerializeField] int height;
        [SerializeField] List<MapEdit> edits = new List<MapEdit>();

        [Serializable]
        class SaveData
        {
            public int seed, width, height;
            public List<MapEdit> edits = new List<MapEdit>();
        }

        [NonSerialized] bool runtimeLoaded;

        public int Seed => seed;
        public int Count => edits.Count;
        public IReadOnlyList<MapEdit> Edits => edits;

        string RuntimePath => Path.Combine(Application.persistentDataPath, $"map_edits_{name}.json");

        public bool Matches(int mapSeed, int mapWidth, int mapHeight) =>
            edits.Count == 0 || (seed == mapSeed && width == mapWidth && height == mapHeight);

        /// <summary>Builds read their own saved edits (the asset in a build is read only).</summary>
        public void LoadRuntimeEdits()
        {
            if (Application.isEditor || runtimeLoaded) return;
            runtimeLoaded = true;
            try
            {
                if (!File.Exists(RuntimePath)) return;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(RuntimePath));
                if (data == null) return;
                seed = data.seed;
                width = data.width;
                height = data.height;
                edits = data.edits ?? new List<MapEdit>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MapEditLog] Could not read {RuntimePath}: {e.Message}");
            }
        }

        public void Record(MapEdit edit, int mapSeed, int mapWidth, int mapHeight)
        {
            if (edit == null) return;
            if (!Matches(mapSeed, mapWidth, mapHeight))
            {
                Debug.LogWarning("[MapEditLog] The log belonged to another map; it is cleared.", this);
                edits.Clear();
            }
            seed = mapSeed;
            width = mapWidth;
            height = mapHeight;
            edits.Add(edit);
            Save();
        }

        public void Clear()
        {
            edits.Clear();
            Save();
        }

        void Save()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
            try
            {
                var data = new SaveData { seed = seed, width = width, height = height, edits = edits };
                File.WriteAllText(RuntimePath, JsonUtility.ToJson(data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MapEditLog] Could not save {RuntimePath}: {e.Message}");
            }
#endif
        }
    }
}
