using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.World.Persistence
{
    public enum MapChangeType
    {
        /// <summary>An object appeared (placed by the developer, a stump left by a tree, a regrown tree...).</summary>
        Created,
        /// <summary>An object disappeared (destroyed, cut down, mined out...).</summary>
        Removed,
        /// <summary>An object was moved to another cell.</summary>
        Moved,
        /// <summary>An object's health changed (it was hit).</summary>
        Health,
        /// <summary>The amount of a dropped pile changed (more items fell on it).</summary>
        Amount,
    }

    /// <summary>
    /// One elementary change of the map. It holds enough data to be applied again (redo / loading the edited
    /// map) and to be reverted (undo).
    /// </summary>
    [Serializable]
    public class MapChange
    {
        public MapChangeType type;
        [Tooltip("Unique id of the object on this map.")]
        public int uid;
        [Tooltip("Asset name of the object's SpawnableDefinition.")]
        public string objectId;
        [Tooltip("Linked object (the tree a stump grows back into). Empty if none.")]
        public string linkedId;
        [Tooltip("Cell of the object (Moved: the cell it came from).")]
        public Vector2Int cell;
        [Tooltip("Moved only: the new cell.")]
        public Vector2Int targetCell;
        public Vector2 offset;
        [Tooltip("Health after the change (Created / Removed: health at that moment).")]
        public int health;
        [Tooltip("Health only: health before the change.")]
        public int previousHealth;
        [Tooltip("Items in a dropped pile (0 / 1 = a normal object). Amount: after the change.")]
        public int amount;
        [Tooltip("Amount only: items before the change.")]
        public int previousAmount;
    }

    /// <summary>
    /// One step of the undo history: everything that happened because of one action (a developer tool click,
    /// or all hits a character gave the same object in a row, including the stump it left).
    /// </summary>
    [Serializable]
    public class MapChangeAction
    {
        public string label;
        [Tooltip("Game mode the action was made in (Developer / Default).")]
        public string mode;
        public string time;
        public List<MapChange> changes = new List<MapChange>();
        [Tooltip("Consecutive actions with the same key (> 0) are merged: all hits on one object form one undo step.")]
        public int mergeKey;
    }

    /// <summary>Contents of the edited map file.</summary>
    [Serializable]
    public class EditedMapData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        [Tooltip("Id of the original map this file was derived from.")]
        public string originalId;
        public int seed;
        public int width;
        public int height;
        [Tooltip("Next free object id.")]
        public int nextUid;
        [Tooltip("How many actions of the history are applied (the rest can be redone).")]
        public int appliedCount;
        public List<MapChangeAction> history = new List<MapChangeAction>();
    }
}
