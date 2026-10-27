using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using KingOfNothing.World.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// Actions menu of map objects (opened by SelectionManager with a right click on an object) and choosing where
    /// objects are moved to. Works in Default and developer mode.
    ///  - Every kind of work in the clicked group gets its own entry (trees: Chop, gold / stones: Mine...), each
    ///    working only on its objects. Hovering an entry shows "... individually" (one character per object, each
    ///    takes the nearest one) and "... together" (all selected characters on the same object until the group is done).
    ///  - Collect: objects with health -1 (works once the inventory exists).
    ///  - Move: characters carry the objects (Default mode rules: no overlapping, red preview = not allowed).
    ///  - Developer mode adds "Destroy (Developer Mode)" and "Move (Developer Mode)" (instant, overlapping allowed).
    /// Escape or right click cancels the menu / the placement.
    /// </summary>
    [DefaultExecutionOrder(-90)] // decides CapturesMouse before SelectionManager reads it
    public class ObjectCommandController : MonoBehaviour
    {
        sealed class MenuItem
        {
            public string Label;
            public bool Enabled = true;
            public System.Action Run;
            public readonly List<MenuItem> Children = new List<MenuItem>();
        }

        enum PlacementMode
        {
            None,
            Carry,
            Developer,
        }

        const int MenuWindowId = 0x4B4F4F;

        [SerializeField] MapGenerator generator;
        [SerializeField] bool logActions = true;

        /// <summary>True while the menu or a placement uses the mouse (other systems ignore clicks then).</summary>
        public static bool CapturesMouse { get; private set; }

        readonly List<MenuItem> menu = new List<MenuItem>();
        readonly PlacementPreview preview = new PlacementPreview();
        readonly List<Unit> menuUnits = new List<Unit>();
        PlacementMode placement;
        bool menuOpen;
        string menuTitle;
        Rect menuRect;
        int expanded = -1, nextExpanded = -1;
        Camera cam;

        static InteractionSettings Settings => MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;

        void Start()
        {
            if (generator == null) generator = FindAnyObjectByType<MapGenerator>();
        }

        void OnDisable()
        {
            CapturesMouse = false;
            preview.End();
        }

        void Update()
        {
            CapturesMouse = menuOpen || preview.IsActive;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var map = MapData.Instance;
            if (mouse == null || map == null || !map.IsReady) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            if (preview.IsActive)
            {
                // Leaving developer mode cancels a developer move
                if (placement == PlacementMode.Developer && !GameModeManager.IsDeveloperMode)
                {
                    EndPlacement();
                    return;
                }
                Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
                bool allowOverlap = placement == PlacementMode.Developer;
                preview.Update(MapData.WorldToCell(world), map, Settings, allowOverlap);
                if (mouse.rightButton.wasPressedThisFrame || escape) EndPlacement();
                else if (mouse.leftButton.wasPressedThisFrame && !ScreenUi.IsPointerOver(mouse.position.ReadValue()))
                {
                    if (placement == PlacementMode.Developer) ConfirmDeveloperMove();
                    else if (!preview.AnyBlocked) ConfirmCarry();
                }
                return;
            }

            if (menuOpen && escape) menuOpen = false;
        }

        void EndPlacement()
        {
            preview.End();
            placement = PlacementMode.None;
        }

        // ------------------------------------------------------------------ menu

        /// <summary>Opens the actions menu for the object (and the selected objects if it is one of them).</summary>
        public void OpenMenu(WorldObject target, Vector2 screen, IReadOnlyList<Unit> units)
        {
            if (target == null) return;
            menu.Clear();
            menuUnits.Clear();
            foreach (var u in units) if (u != null) menuUnits.Add(u);

            var group = new List<WorldObject>();
            if (ObjectSelection.Contains(target)) group.AddRange(ObjectSelection.Selected);
            else group.Add(target);
            bool hasUnits = menuUnits.Count > 0;
            bool developer = GameModeManager.IsDeveloperMode;
            menuTitle = group.Count == 1 ? target.DisplayName : $"{group.Count} objects";

            if (!hasUnits) menu.Add(new MenuItem { Label = "Select characters to give orders", Enabled = false });

            // One entry per kind of work, each only for its own objects
            var byVerb = new Dictionary<string, List<ResourceNode>>();
            var verbs = new List<string>();
            foreach (var o in group)
            {
                if (!(o is ResourceNode node) || !node.CanBeHit) continue;
                string verb = VerbFor(node);
                if (!byVerb.TryGetValue(verb, out var list))
                {
                    byVerb[verb] = list = new List<ResourceNode>();
                    verbs.Add(verb);
                }
                list.Add(node);
            }
            foreach (var verb in verbs)
            {
                var targets = byVerb[verb];
                var workers = new List<Unit>(menuUnits);
                var item = new MenuItem
                {
                    Label = targets.Count == 1 ? verb : $"{verb} ({targets.Count})",
                    Enabled = hasUnits,
                    Run = () => AssignWork(workers, targets, verb, false),
                };
                // Individually / together only matter with several characters
                if (workers.Count > 1)
                {
                    item.Children.Add(new MenuItem { Label = $"{verb} individually", Enabled = hasUnits, Run = () => AssignWork(workers, targets, verb, false) });
                    item.Children.Add(new MenuItem { Label = $"{verb} together", Enabled = hasUnits, Run = () => AssignWork(workers, targets, verb, true) });
                }
                menu.Add(item);
            }

            int collectable = group.FindAll(o => o is ResourceNode node && node.IsCollectable).Count;
            if (collectable > 0)
                menu.Add(new MenuItem { Label = collectable == 1 ? "Collect (needs the inventory)" : $"Collect ({collectable}) (needs the inventory)", Enabled = false });

            var movable = group.FindAll(o => o.MovableByCharacters);
            if (movable.Count > 0)
            {
                var anchor = movable.Contains(target) ? target : movable[0];
                menu.Add(new MenuItem
                {
                    Label = movable.Count == 1 ? "Move" : $"Move ({movable.Count})",
                    Enabled = hasUnits,
                    Run = () => BeginPlacement(movable, anchor, PlacementMode.Carry),
                });
            }
            else if (!developer) menu.Add(new MenuItem { Label = "Cannot be moved by characters", Enabled = false });

            if (developer)
            {
                var all = new List<WorldObject>(group);
                menu.Add(new MenuItem { Label = all.Count == 1 ? "Destroy (Developer Mode)" : $"Destroy (Developer Mode) ({all.Count})", Run = () => DeveloperDestroy(all) });
                menu.Add(new MenuItem { Label = all.Count == 1 ? "Move (Developer Mode)" : $"Move (Developer Mode) ({all.Count})", Run = () => BeginPlacement(all, target, PlacementMode.Developer) });
            }

            menu.Add(new MenuItem { Label = "Cancel" });
            menuRect = new Rect(screen.x, Screen.height - screen.y, 220f, 10f);
            expanded = nextExpanded = -1;
            menuOpen = true;
            CapturesMouse = true;
        }

        static string VerbFor(ResourceNode node)
        {
            var kind = node.Definition != null ? node.Definition.Kind : WorldObjectKind.Decoration;
            switch (kind)
            {
                case WorldObjectKind.Tree:
                case WorldObjectKind.Stump:
                    return "Chop";
                case WorldObjectKind.Stone:
                case WorldObjectKind.GoldStone:
                case WorldObjectKind.GoldMine:
                case WorldObjectKind.WaterRock:
                    return "Mine";
                case WorldObjectKind.Bush:
                case WorldObjectKind.Grass:
                case WorldObjectKind.Mushroom:
                    return "Cut";
                default:
                    return "Attack";
            }
        }

        void OnGUI()
        {
            if (!menuOpen) return;
            var e = Event.current;
            if (e.type == EventType.MouseDown && !menuRect.Contains(e.mousePosition))
            {
                menuOpen = false;
                return;
            }
            // The expanded entry only changes between frames, so Layout and Repaint see the same menu
            if (e.type == EventType.Layout) expanded = nextExpanded;
            menuRect = GUILayout.Window(MenuWindowId, menuRect, DrawMenu, menuTitle, UiStyle.Window, GUILayout.MinWidth(200f));
            ScreenUi.Block(menuRect);
        }

        void DrawMenu(int id)
        {
            var e = Event.current;
            bool repaint = e.type == EventType.Repaint;
            int hovered = -1;
            MenuItem clicked = null;
            for (int i = 0; i < menu.Count; i++)
            {
                var item = menu[i];
                GUI.enabled = item.Enabled;
                if (GUILayout.Button(item.Label)) clicked = item;
                // The entry stays open while the mouse is over it or over its sub-options, and closes otherwise
                var area = repaint ? GUILayoutUtility.GetLastRect() : Rect.zero;
                if (i == expanded)
                {
                    foreach (var child in item.Children)
                    {
                        GUI.enabled = child.Enabled;
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Space(16f);
                            if (GUILayout.Button(child.Label)) clicked = child;
                        }
                        if (repaint)
                        {
                            var childRect = GUILayoutUtility.GetLastRect();
                            area = Rect.MinMaxRect(Mathf.Min(area.xMin, childRect.xMin), Mathf.Min(area.yMin, childRect.yMin),
                                                   Mathf.Max(area.xMax, childRect.xMax), Mathf.Max(area.yMax, childRect.yMax));
                        }
                    }
                }
                if (repaint && item.Children.Count > 0 && area.Contains(e.mousePosition)) hovered = i;
            }
            GUI.enabled = true;
            if (repaint) nextExpanded = hovered;
            if (clicked != null) Run(clicked);
        }

        void Run(MenuItem item)
        {
            menuOpen = false;
            item.Run?.Invoke();
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Individually: each character works on its own object (the nearest free one). Together: everybody on the same one.</summary>
        void AssignWork(List<Unit> units, List<ResourceNode> targets, string verb, bool together)
        {
            units.RemoveAll(u => u == null);
            targets.RemoveAll(t => t == null || !t.CanBeHit);
            if (units.Count == 0 || targets.Count == 0) return;
            var group = new WorkGroup(targets, together);
            foreach (var u in units)
            {
                u.Worker.ClearJobs();
                u.Worker.Assign(new GroupHitJob(group, verb), true);
            }
            if (logActions) Debug.Log($"[Orders] {units.Count} character(s): {verb} {targets.Count} object(s) {(together ? "together" : "individually")}");
        }

        void BeginPlacement(List<WorldObject> objects, WorldObject anchor, PlacementMode mode)
        {
            objects.RemoveAll(o => o == null);
            if (objects.Count == 0) return;
            placement = mode;
            preview.Begin(objects, anchor);
        }

        void ConfirmCarry()
        {
            var units = new List<Unit>(menuUnits);
            units.RemoveAll(u => u == null);
            if (units.Count == 0)
            {
                EndPlacement();
                return;
            }
            foreach (var u in units) u.Worker.ClearJobs();
            var objects = new List<WorldObject>(preview.Objects);
            var targets = new List<Vector2Int>(preview.Targets);
            EndPlacement();
            // Every object goes to the character with the fewest jobs; the nearest one on a tie
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (o == null || o.IsCarried) continue;
                PickWorker(units, o.GroundPoint).Worker.Assign(new CarryObjectJob(o, targets[i]), true);
            }
            ObjectSelection.Clear();
        }

        void ConfirmDeveloperMove()
        {
            var objects = new List<WorldObject>(preview.Objects);
            var targets = new List<Vector2Int>(preview.Targets);
            EndPlacement();
            var history = History;
            history?.BeginAction(objects.Count == 1 && objects[0] != null ? $"Move {objects[0].DisplayName}" : $"Move {objects.Count} objects");
            for (int i = 0; i < objects.Count; i++)
                if (objects[i] != null && !objects[i].IsCarried) objects[i].MoveTo(targets[i]);
            history?.EndAction();
            ObjectSelection.Clear();
            if (logActions) Debug.Log($"[Dev] Moved {objects.Count} objects");
        }

        void DeveloperDestroy(List<WorldObject> objects)
        {
            objects.RemoveAll(o => o == null);
            if (objects.Count == 0) return;
            foreach (var o in objects) ObjectSelection.Remove(o);
            var history = History;
            history?.BeginAction(objects.Count == 1 ? $"Destroy {objects[0].DisplayName}" : $"Destroy {objects.Count} objects");
            foreach (var o in objects) if (o != null && !o.IsCarried) o.Remove();
            history?.EndAction();
            if (logActions) Debug.Log($"[Dev] Destroyed {objects.Count} objects");
        }

        MapChangeHistory History => generator != null ? generator.History : null;

        static Unit PickWorker(List<Unit> units, Vector2 near)
        {
            Unit best = units[0];
            float bestScore = float.MaxValue;
            foreach (var u in units)
            {
                float score = u.Worker.QueuedCount * 1000f + Vector2.Distance(u.transform.position, near);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = u;
                }
            }
            return best;
        }
    }
}
