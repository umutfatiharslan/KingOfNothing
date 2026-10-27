using System;
using KingOfNothing.World.Persistence;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// The ORIGINAL map, frozen: terrain, biomes and every object exactly as they were generated once.
    /// It is never changed by playing or by the developer tools; those changes go to the edited map file
    /// (see MapChangeHistory). Changing the code or the generation settings does not change it either:
    /// only "Bake Original Map" on the MapGenerator does. Commit it so the whole team has the same original.
    /// Object stats (health, yield...) are not frozen; they always come from the current object assets.
    /// </summary>
    [CreateAssetMenu(fileName = "OriginalMap", menuName = "King of Nothing/World/Original Map")]
    public class OriginalMapAsset : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/World/OriginalMap.asset";

        [Header("Baked map (read only)")]
        [SerializeField] string snapshotId;
        [SerializeField] int seed;
        [SerializeField] int width;
        [SerializeField] int height;
        [SerializeField] int objectCount;
        [SerializeField] string bakedAt;
        [SerializeField, HideInInspector] byte[] data;

        [NonSerialized] MapSnapshot cache;

        public bool HasData => data != null && data.Length > 0;
        public string SnapshotId => snapshotId;
        public int Seed => seed;
        public int Width => width;
        public int Height => height;
        public int ObjectCount => objectCount;
        public string BakedAt => bakedAt;
        public int SizeKilobytes => HasData ? data.Length / 1024 : 0;

        /// <summary>The frozen map, or null if nothing is baked / the data is unreadable.</summary>
        public MapSnapshot Load()
        {
            if (!HasData) return null;
            if (cache != null && cache.Id == snapshotId) return cache;
            try
            {
                cache = MapSnapshot.Deserialize(data);
                return cache;
            }
            catch (Exception e)
            {
                Debug.LogError($"[OriginalMapAsset] '{name}' could not be read: {e.Message}", this);
                return null;
            }
        }

#if UNITY_EDITOR
        /// <summary>Replaces the original with a new snapshot and saves the asset (editor only).</summary>
        public void Store(MapSnapshot snapshot)
        {
            data = snapshot.Serialize();
            cache = snapshot;
            snapshotId = snapshot.Id;
            seed = snapshot.Seed;
            width = snapshot.Width;
            height = snapshot.Height;
            objectCount = snapshot.Objects.Count;
            bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
        }

        /// <summary>Loads the default asset, creating an empty one if it does not exist yet.</summary>
        public static OriginalMapAsset LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<OriginalMapAsset>(DefaultPath);
            if (asset != null) return asset;
            string folder = System.IO.Path.GetDirectoryName(DefaultPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !UnityEditor.AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
                UnityEditor.AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }
            asset = CreateInstance<OriginalMapAsset>();
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            return asset;
        }
#endif
    }
}
