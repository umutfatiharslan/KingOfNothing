using System;
using System.IO;
using UnityEngine;

namespace KingOfNothing.World.Persistence
{
    /// <summary>
    /// The edited copy of the original map on this computer: every change made while playing (both game modes)
    /// with its undo / redo history. Stored as JSON in the player's data folder (not in the project and not in
    /// git), so every team member has their own edited map. Deleting the file = back to the original map.
    /// </summary>
    public sealed class EditedMapFile
    {
        public const string FolderName = "Saves";

        public string FilePath { get; }

        public EditedMapFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "edited_map";
            foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
            FilePath = Path.Combine(Folder, fileName + ".json");
        }

        /// <summary>Folder holding the edited map files (Application.persistentDataPath/Saves).</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

        public bool Exists => File.Exists(FilePath);

        /// <summary>Reads the file; null if it does not exist or cannot be read.</summary>
        public EditedMapData Read()
        {
            if (!Exists) return null;
            try
            {
                var data = JsonUtility.FromJson<EditedMapData>(File.ReadAllText(FilePath));
                if (data != null && data.history == null) data.history = new System.Collections.Generic.List<MapChangeAction>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EditedMapFile] Could not read {FilePath}: {e.Message}");
                return null;
            }
        }

        /// <summary>Writes the file (via a temporary file, so a crash never leaves half a file).</summary>
        public bool Write(EditedMapData data)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EditedMapFile] Could not save {FilePath}: {e.Message}");
                return false;
            }
        }

        public void Delete()
        {
            try
            {
                if (Exists) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EditedMapFile] Could not delete {FilePath}: {e.Message}");
            }
        }

        /// <summary>
        /// Keeps a file that belongs to another original (renamed with a time stamp, never deleted silently).
        /// Returns the new path or null.
        /// </summary>
        public string Archive(string reason)
        {
            if (!Exists) return null;
            try
            {
                string archived = Path.Combine(Folder,
                    Path.GetFileNameWithoutExtension(FilePath) + $"_{reason}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Move(FilePath, archived);
                return archived;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EditedMapFile] Could not archive {FilePath}: {e.Message}");
                return null;
            }
        }
    }
}
