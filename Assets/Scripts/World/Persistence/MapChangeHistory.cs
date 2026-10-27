using System;
using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World.Persistence
{
    /// <summary>
    /// Records every change of the map objects while playing (both game modes: developer tools and normal play,
    /// including the remaining health of objects that were hit), keeps them as an undo / redo history and saves
    /// them to the edited map file. When the map is loaded, the saved history is applied on top of the original
    /// map, so the edited map comes back exactly as it was left.
    ///
    /// Changes are picked up from the static events of WorldObject / ResourceNode, so any system that creates,
    /// removes, moves or hits objects is recorded without knowing about this class. Changes made in the same
    /// frame form one action; consecutive hits on the same object (until it is cut down and leaves its stump)
    /// are merged into a single undo step. Tools can group their changes with BeginAction / EndAction.
    /// </summary>
    public sealed class MapChangeHistory : IDisposable
    {
        public struct Setup
        {
            public MapData Map;
            public MapSnapshot Original;
            public EditedMapFile File;
            /// <summary>Finds a SpawnableDefinition by asset name.</summary>
            public Func<string, SpawnableDefinition> Resolve;
            /// <summary>Parent transform for objects created by the history.</summary>
            public Func<Transform> Parent;
            /// <summary>Name of the current game mode (stored with every action).</summary>
            public Func<string> Mode;
            /// <summary>Record new changes (false = only load the saved changes, e.g. edit mode preview).</summary>
            public bool Record;
            /// <summary>Seconds to wait after a change before the file is written.</summary>
            public float AutosaveDelay;
        }

        readonly Setup setup;
        EditedMapData data;
        MapChangeAction open;
        bool openIsExplicit;
        bool applying;
        bool subscribed;
        bool dirty;
        float dirtySince;
        float dirtyFirst;

        /// <summary>Raised after every recorded action, undo, redo or load (refresh UI).</summary>
        public event Action<MapChangeHistory> Changed;

        public MapChangeHistory(Setup setup)
        {
            this.setup = setup;
            data = NewData();
        }

        public string FilePath => setup.File.FilePath;
        public bool FileExists => setup.File.Exists;
        public int AppliedCount => data.appliedCount;
        public int HistoryCount => data.history.Count;
        public bool CanUndo => data.appliedCount > 0 || HasOpenChanges;
        public bool CanRedo => data.appliedCount < data.history.Count && !HasOpenChanges;
        public bool HasUnsavedChanges => dirty || HasOpenChanges;
        bool HasOpenChanges => open != null && open.changes.Count > 0;

        /// <summary>Label of the action Undo would revert (null if none).</summary>
        public string UndoLabel => HasOpenChanges ? open.label : data.appliedCount > 0 ? data.history[data.appliedCount - 1].label : null;

        /// <summary>Label of the action Redo would apply (null if none).</summary>
        public string RedoLabel => CanRedo ? data.history[data.appliedCount].label : null;

        EditedMapData NewData() => new EditedMapData
        {
            originalId = setup.Original != null ? setup.Original.Id : "",
            seed = setup.Original != null ? setup.Original.Seed : 0,
            width = setup.Map != null ? setup.Map.Width : 0,
            height = setup.Map != null ? setup.Map.Height : 0,
        };

        // ------------------------------------------------------------------ load

        /// <summary>
        /// Reads the edited map file and applies its applied actions to the (freshly built original) map,
        /// then starts recording. A file made for another original map is archived, never applied.
        /// </summary>
        /// <returns>Short description for the log.</returns>
        public string LoadAndApply()
        {
            string result = "no edited map file (original map)";
            var saved = setup.File.Read();
            if (saved != null)
            {
                bool sameOriginal = setup.Original != null && saved.originalId == setup.Original.Id
                                    && saved.width == setup.Map.Width && saved.height == setup.Map.Height;
                if (!sameOriginal)
                {
                    string archived = setup.File.Archive("outdated");
                    Debug.LogWarning("[MapChangeHistory] The edited map file belongs to another original map; it was not applied" +
                                     (archived != null ? $" and was kept as {archived}" : "") + ".");
                    result = "edited map file was outdated (archived)";
                }
                else
                {
                    data = saved;
                    data.appliedCount = Mathf.Clamp(data.appliedCount, 0, data.history.Count);
                    int failed = 0;
                    applying = true;
                    try
                    {
                        for (int a = 0; a < data.appliedCount; a++)
                            foreach (var change in data.history[a].changes)
                                if (!Apply(change, true)) failed++;
                    }
                    finally
                    {
                        applying = false;
                    }
                    setup.Map.NextUid = Mathf.Max(setup.Map.NextUid, data.nextUid);
                    // Stored items and other kept states (not part of the undo history)
                    if (data.keptStates != null)
                        foreach (var kept in data.keptStates)
                        {
                            var o = kept != null ? setup.Map.FindObject(kept.uid) : null;
                            if (o != null) o.LoadKeptState(kept.state);
                        }
                    result = $"edited map loaded: {data.appliedCount} actions" +
                             (data.history.Count > data.appliedCount ? $" (+{data.history.Count - data.appliedCount} undone)" : "") +
                             (failed > 0 ? $", {failed} changes could not be applied" : "");
                    if (failed > 0) Debug.LogWarning($"[MapChangeHistory] {failed} saved changes could not be applied (objects removed from the settings?).");
                }
            }

            if (setup.Record) Subscribe();
            Changed?.Invoke(this);
            return result;
        }

        // ------------------------------------------------------------------ recording

        void Subscribe()
        {
            if (subscribed) return;
            WorldObject.AnyCreated += OnCreated;
            WorldObject.AnyRemoving += OnRemoving;
            WorldObject.AnyMoved += OnMoved;
            WorldObject.AnyAmountChanged += OnAmountChanged;
            WorldObject.AnyStateChanged += OnStateChanged;
            WorldObject.AnyKeptStateChanged += OnKeptStateChanged;
            ResourceNode.AnyHealthChanged += OnHealthChanged;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            WorldObject.AnyCreated -= OnCreated;
            WorldObject.AnyRemoving -= OnRemoving;
            WorldObject.AnyMoved -= OnMoved;
            WorldObject.AnyAmountChanged -= OnAmountChanged;
            WorldObject.AnyStateChanged -= OnStateChanged;
            WorldObject.AnyKeptStateChanged -= OnKeptStateChanged;
            ResourceNode.AnyHealthChanged -= OnHealthChanged;
            subscribed = false;
        }

        bool Tracks(WorldObject o) => !applying && o != null && o.Uid > 0 && setup.Map != null && setup.Map.FindObject(o.Uid) == o;

        void OnCreated(WorldObject o)
        {
            if (!Tracks(o)) return;
            Add(Snapshot(MapChangeType.Created, o), $"{o.DisplayName} appeared");
        }

        void OnRemoving(WorldObject o)
        {
            if (!Tracks(o)) return;
            Add(Snapshot(MapChangeType.Removed, o), $"{o.DisplayName} removed");
        }

        void OnMoved(WorldObject o, Vector2Int from, Vector2Int to)
        {
            if (!Tracks(o)) return;
            var change = Snapshot(MapChangeType.Moved, o);
            change.cell = from;
            change.targetCell = to;
            Add(change, $"{o.DisplayName} moved");
        }

        void OnAmountChanged(WorldObject o, int before, int after)
        {
            if (!Tracks(o)) return;
            var change = Snapshot(MapChangeType.Amount, o);
            change.previousAmount = before;
            change.amount = after;
            Add(change, $"{o.DisplayName} pile");
        }

        void OnHealthChanged(ResourceNode node, int before, int after)
        {
            if (!Tracks(node)) return;
            var change = Snapshot(MapChangeType.Health, node);
            change.previousHealth = before;
            change.health = after;
            bool building = node.Definition is BuildingDefinition;
            string label = building ? (after > before ? $"Build {node.DisplayName}" : $"Damage {node.DisplayName}") : $"Hit {node.DisplayName}";
            Add(change, label, node.Uid);
        }

        /// <summary>Stored items changed: nothing goes into the history, the file just has to be written again.</summary>
        void OnKeptStateChanged(WorldObject o)
        {
            if (!Tracks(o)) return;
            MarkDirty();
        }

        void OnStateChanged(WorldObject o, string before, string after)
        {
            if (!Tracks(o)) return;
            var change = Snapshot(MapChangeType.State, o);
            change.previousState = before;
            change.state = after;
            Add(change, $"{o.DisplayName} changed", o.Uid);
        }

        static MapChange Snapshot(MapChangeType type, WorldObject o)
        {
            var linked = o.LinkedDefinition;
            return new MapChange
            {
                type = type,
                uid = o.Uid,
                objectId = o.Definition != null ? o.Definition.name : null,
                linkedId = linked != null ? linked.name : null,
                cell = o.Cell,
                targetCell = o.Cell,
                offset = o.Offset,
                health = o is ResourceNode node ? node.Health : 0,
                amount = o.Amount,
                state = o.SaveState(),
            };
        }

        void Add(MapChange change, string label, int mergeKey = 0)
        {
            if (open == null)
            {
                open = NewAction(label, mergeKey);
                openIsExplicit = false;
            }
            AppendCompact(open.changes, change);
        }

        /// <summary>
        /// Adds a change; consecutive health changes of the same object are folded into one (the first "before"
        /// and the last "after" are kept), so long work on an object does not bloat the file.
        /// </summary>
        static void AppendCompact(List<MapChange> changes, MapChange change)
        {
            if ((change.type == MapChangeType.Health || change.type == MapChangeType.Amount || change.type == MapChangeType.State) && changes.Count > 0)
            {
                var last = changes[changes.Count - 1];
                if (last.type == change.type && last.uid == change.uid)
                {
                    last.health = change.health;
                    last.amount = change.amount;
                    last.state = change.state;
                    return;
                }
            }
            changes.Add(change);
        }

        MapChangeAction NewAction(string label, int mergeKey) => new MapChangeAction
        {
            label = label,
            mode = setup.Mode != null ? setup.Mode() : "",
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            mergeKey = mergeKey,
        };

        /// <summary>Groups the following changes into one undo step (closes any automatic one first).</summary>
        public void BeginAction(string label)
        {
            CommitOpen();
            open = NewAction(label, 0);
            openIsExplicit = true;
        }

        public void EndAction()
        {
            if (openIsExplicit) CommitOpen();
        }

        void CommitOpen()
        {
            var action = open;
            open = null;
            openIsExplicit = false;
            if (action == null || action.changes.Count == 0) return;

            // A new action drops everything that was undone
            if (data.appliedCount < data.history.Count)
                data.history.RemoveRange(data.appliedCount, data.history.Count - data.appliedCount);

            var previous = data.appliedCount > 0 ? data.history[data.appliedCount - 1] : null;
            if (action.mergeKey > 0 && previous != null && previous.mergeKey == action.mergeKey && previous.mode == action.mode)
            {
                foreach (var change in action.changes) AppendCompact(previous.changes, change);
                previous.time = action.time;
                if (previous.changes.Exists(c => c.type == MapChangeType.Removed && c.uid == action.mergeKey) &&
                    !previous.label.EndsWith("(cut down)"))
                    previous.label += " (cut down)";
            }
            else
            {
                data.history.Add(action);
                data.appliedCount = data.history.Count;
            }
            MarkDirty();
        }

        void MarkDirty()
        {
            if (!dirty) dirtyFirst = Time.unscaledTime;
            dirty = true;
            dirtySince = Time.unscaledTime;
            Changed?.Invoke(this);
        }

        /// <summary>Call once per frame (LateUpdate): closes the frame's automatic action and autosaves.</summary>
        public void Tick()
        {
            if (open != null && !openIsExplicit) CommitOpen();
            // Save after a quiet moment, and at least every few seconds while changes keep coming
            float now = Time.unscaledTime;
            if (dirty && (now - dirtySince >= setup.AutosaveDelay || now - dirtyFirst >= Mathf.Max(10f, setup.AutosaveDelay * 5f))) Save();
        }

        // ------------------------------------------------------------------ undo / redo

        public bool Undo()
        {
            CommitOpen();
            if (data.appliedCount == 0) return false;
            var action = data.history[data.appliedCount - 1];
            int failed = 0;
            applying = true;
            try
            {
                for (int i = action.changes.Count - 1; i >= 0; i--)
                    if (!Apply(action.changes[i], false)) failed++;
            }
            finally
            {
                applying = false;
            }
            data.appliedCount--;
            if (failed > 0) Debug.LogWarning($"[MapChangeHistory] Undo '{action.label}': {failed} changes could not be reverted.");
            MarkDirty();
            return true;
        }

        public bool Redo()
        {
            CommitOpen();
            if (data.appliedCount >= data.history.Count) return false;
            var action = data.history[data.appliedCount];
            int failed = 0;
            applying = true;
            try
            {
                foreach (var change in action.changes)
                    if (!Apply(change, true)) failed++;
            }
            finally
            {
                applying = false;
            }
            data.appliedCount++;
            if (failed > 0) Debug.LogWarning($"[MapChangeHistory] Redo '{action.label}': {failed} changes could not be applied.");
            MarkDirty();
            return true;
        }

        /// <summary>Applies a change (forward = do / redo) or reverts it (forward = false = undo).</summary>
        bool Apply(MapChange c, bool forward)
        {
            switch (c.type)
            {
                case MapChangeType.Created:
                    return forward ? Spawn(c) : RemoveObject(c.uid);
                case MapChangeType.Removed:
                    return forward ? RemoveObject(c.uid) : Spawn(c);
                case MapChangeType.Moved:
                {
                    var o = setup.Map.FindObject(c.uid);
                    if (o == null) return false;
                    o.MoveTo(forward ? c.targetCell : c.cell);
                    return true;
                }
                case MapChangeType.Amount:
                {
                    var o = setup.Map.FindObject(c.uid);
                    if (o == null) return false;
                    o.Amount = forward ? c.amount : c.previousAmount;
                    return true;
                }
                case MapChangeType.Health:
                {
                    if (!(setup.Map.FindObject(c.uid) is ResourceNode node)) return false;
                    node.SetHealth(forward ? c.health : c.previousHealth, false);
                    return true;
                }
                case MapChangeType.State:
                {
                    var o = setup.Map.FindObject(c.uid);
                    if (o == null) return false;
                    o.LoadState(forward ? c.state : c.previousState);
                    return true;
                }
            }
            return false;
        }

        bool RemoveObject(int uid)
        {
            var o = setup.Map.FindObject(uid);
            if (o == null) return false;
            o.Remove();
            return true;
        }

        bool Spawn(MapChange c)
        {
            var definition = setup.Resolve(c.objectId);
            if (definition == null) return false;
            var linked = string.IsNullOrEmpty(c.linkedId) ? null : setup.Resolve(c.linkedId);
            int health = c.health;
            int amount = Mathf.Max(1, c.amount);
            string state = c.state;
            var created = WorldObjectFactory.Create(definition, c.cell, c.offset, setup.Parent(), setup.Map, c.uid, o =>
            {
                if (linked != null) o.SetLinkedDefinition(linked);
                o.Amount = amount;
                // State first: it can change the maximum health (a destroyed building has its own health bar)
                if (!string.IsNullOrEmpty(state)) o.LoadState(state);
                if (o is ResourceNode node) node.SetHealth(health, false);
            });
            return created != null;
        }

        // ------------------------------------------------------------------ file

        /// <summary>Writes the edited map file now (nothing is written while the map equals the original and has no history).</summary>
        public void Save()
        {
            CommitOpen();
            dirty = false;
            if (data.history.Count == 0 && !setup.File.Exists && !AnyKeptState()) return;
            data.nextUid = setup.Map != null ? setup.Map.NextUid : data.nextUid;
            if (setup.Map != null)
            {
                data.keptStates = new List<KeptObjectState>();
                foreach (var o in setup.Map.AllObjects)
                {
                    string kept = o.SaveKeptState();
                    if (!string.IsNullOrEmpty(kept)) data.keptStates.Add(new KeptObjectState { uid = o.Uid, state = kept });
                }
            }
            data.width = setup.Map != null ? setup.Map.Width : data.width;
            data.height = setup.Map != null ? setup.Map.Height : data.height;
            setup.File.Write(data);
        }

        bool AnyKeptState()
        {
            if (setup.Map == null) return false;
            foreach (var o in setup.Map.AllObjects)
                if (!string.IsNullOrEmpty(o.SaveKeptState())) return true;
            return false;
        }

        /// <summary>Deletes the edited map file and forgets the history (the caller rebuilds the original objects).</summary>
        public void DeleteFile()
        {
            open = null;
            openIsExplicit = false;
            data = NewData();
            dirty = false;
            setup.File.Delete();
            Changed?.Invoke(this);
        }

        /// <summary>Last actions, newest first (for the developer panel).</summary>
        public IEnumerable<MapChangeAction> RecentActions(int count)
        {
            for (int i = data.appliedCount - 1; i >= 0 && count > 0; i--, count--)
                yield return data.history[i];
        }

        public void Dispose()
        {
            Unsubscribe();
            Changed = null;
        }
    }
}
