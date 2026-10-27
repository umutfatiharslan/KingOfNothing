using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.Interaction;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Selection and orders with the mouse (Default mode and the developer Select tool):
    ///  - left click: select the character or object under the mouse (by its visible shape); empty ground clears
    ///  - Shift + left click: add / remove that character or object (several one by one)
    ///  - left drag: blue box = select characters; hold Shift at any moment while dragging and the box turns
    ///    orange = select objects (added to the selection)
    ///  - right click on ground: the selected characters walk there (square formation)
    ///  - right drag: RimWorld style line formation along the dragged line (markers show the spots)
    ///  - right click on an object: actions menu (ObjectCommandController)
    /// In developer mode Ctrl + click and an active developer tool own the mouse.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        [Min(0.1f)] [SerializeField] float formationSpacing = 0.8f;
        [SerializeField] Color unitBoxFill = new Color(0.35f, 0.85f, 1f, 0.15f);
        [SerializeField] Color unitBoxBorder = new Color(0.35f, 0.85f, 1f, 0.9f);
        [SerializeField] Color formationMarkerColor = new Color(0.45f, 1f, 0.45f, 0.8f);

        public static SelectionManager Instance { get; private set; }
        public IReadOnlyList<Unit> Selected => selected;

        readonly List<Unit> selected = new List<Unit>();
        readonly List<SpriteRenderer> formationMarkers = new List<SpriteRenderer>();
        readonly List<Vector2> formationSpots = new List<Vector2>();
        Camera cam;
        ObjectCommandController commands;

        Vector2 leftStart, rightStart;
        bool leftPressing, leftDragging, rightPressing, rightDragging;

        static InteractionSettings Settings => MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;

        void Awake()
        {
            Instance = this;
            cam = Camera.main;
            commands = GetComponent<ObjectCommandController>();
            if (commands == null) commands = gameObject.AddComponent<ObjectCommandController>();
            // Build menu, building windows and the day counter live next to the selection
            if (GetComponent<BuildMenuController>() == null) gameObject.AddComponent<BuildMenuController>();
            if (GetComponent<BuildingWindows>() == null) gameObject.AddComponent<BuildingWindows>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            selected.RemoveAll(u => u == null);
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector2 screen = mouse.position.ReadValue();
            var keyboard = Keyboard.current;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool ctrl = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            // Developer tools / the actions menu / panels own the mouse
            bool blocked = DevInteractionController.CapturesMouse || ObjectCommandController.CapturesMouse || BuildMenuController.CapturesMouse ||
                           (GameModeManager.IsDeveloperMode && ctrl) || ScreenUi.IsPointerOver(screen);

            UpdateLeft(mouse, screen, shift, blocked);
            UpdateRight(mouse, screen, blocked);
        }

        // ------------------------------------------------------------------ left: selection

        void UpdateLeft(Mouse mouse, Vector2 screen, bool shift, bool blocked)
        {
            if (mouse.leftButton.wasPressedThisFrame && !blocked)
            {
                leftPressing = true;
                leftDragging = false;
                leftStart = screen;
            }
            if (!leftPressing) return;
            if (!leftDragging && (screen - leftStart).magnitude > Settings.dragThreshold) leftDragging = true;
            if (!mouse.leftButton.wasReleasedThisFrame) return;

            if (leftDragging)
            {
                if (shift) BoxSelectObjects(leftStart, screen);
                else BoxSelectUnits(leftStart, screen);
            }
            else ClickSelect(screen, shift);
            leftPressing = leftDragging = false;
        }

        void ClickSelect(Vector2 screen, bool additive)
        {
            Vector2 world = cam.ScreenToWorldPoint(screen);
            var unit = UnitAt(world);
            WorldObject o = unit == null ? ObjectPicker.Pick(MapData.Instance, world) : null;

            if (!additive)
            {
                ClearSelection();
                ObjectSelection.Clear();
            }
            if (unit != null)
            {
                if (additive && unit.IsSelected) Deselect(unit);
                else Select(unit);
            }
            else if (o != null)
            {
                if (additive) ObjectSelection.Toggle(o);
                else ObjectSelection.Add(o);
            }
        }

        static Unit UnitAt(Vector2 world)
        {
            // Of overlapping units pick the front one (lowest on screen)
            Unit clicked = null;
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var unit = hit.GetComponentInParent<Unit>();
                if (unit != null && (clicked == null || unit.transform.position.y < clicked.transform.position.y))
                    clicked = unit;
            }
            return clicked;
        }

        void BoxSelectUnits(Vector2 a, Vector2 b)
        {
            var rect = ScreenRect(a, b);
            ClearSelection();
            ObjectSelection.Clear();
            foreach (var unit in Unit.All)
            {
                if (unit.IsHidden) continue;
                // Selected if the middle of the body (a bit above the feet) is inside the box
                Vector2 point = cam.WorldToScreenPoint(unit.transform.position + Vector3.up * 0.5f);
                if (rect.Contains(point)) Select(unit);
            }
        }

        void BoxSelectObjects(Vector2 a, Vector2 b)
        {
            Vector2 wa = cam.ScreenToWorldPoint(a), wb = cam.ScreenToWorldPoint(b);
            ObjectSelection.SelectInWorldRect(Rect.MinMaxRect(Mathf.Min(wa.x, wb.x), Mathf.Min(wa.y, wb.y),
                Mathf.Max(wa.x, wb.x), Mathf.Max(wa.y, wb.y)), MapData.Instance, true);
        }

        static Rect ScreenRect(Vector2 a, Vector2 b) =>
            Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        void Select(Unit unit)
        {
            if (selected.Contains(unit)) return;
            selected.Add(unit);
            unit.SetSelected(true);
        }

        public void Deselect(Unit unit)
        {
            selected.Remove(unit);
            unit.SetSelected(false);
        }

        public void ClearSelection()
        {
            foreach (var unit in selected)
                if (unit != null) unit.SetSelected(false);
            selected.Clear();
        }

        // ------------------------------------------------------------------ right: orders

        void UpdateRight(Mouse mouse, Vector2 screen, bool blocked)
        {
            if (mouse.rightButton.wasPressedThisFrame && !blocked)
            {
                rightPressing = true;
                rightDragging = false;
                rightStart = screen;
            }
            if (!rightPressing) return;

            if (!rightDragging && selected.Count > 1 && (screen - rightStart).magnitude > Settings.dragThreshold) rightDragging = true;
            if (rightDragging) UpdateFormation(cam.ScreenToWorldPoint(rightStart), cam.ScreenToWorldPoint(screen));

            if (!mouse.rightButton.wasReleasedThisFrame) return;
            rightPressing = false;
            if (rightDragging)
            {
                rightDragging = false;
                IssueFormationMove();
                HideFormation();
                return;
            }

            Vector2 world = cam.ScreenToWorldPoint(screen);
            // A character on a tower or with loot in its hands has its own menu
            var clickedUnit = UnitAt(world);
            if (clickedUnit != null && commands.OpenUnitMenu(clickedUnit, screen)) return;
            var target = ObjectPicker.Pick(MapData.Instance, world);
            if (target != null && (selected.Count > 0 || GameModeManager.IsDeveloperMode || target is KingOfNothing.World.Buildings.Building))
            {
                commands.OpenMenu(target, screen, selected);
                return;
            }
            if (selected.Count > 0) IssueMove(world);
        }

        void IssueMove(Vector2 target)
        {
            // Unreachable targets (deep water...) are fine: the pathfinder stops at the closest reachable point
            // Several units stand in a square formation around the target
            int count = selected.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)cols);
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector2(i % cols - (cols - 1) / 2f, -(i / cols) + (rows - 1) / 2f) * formationSpacing;
                var spot = target + offset;
                selected[i].Worker?.ClearJobs();
                selected[i].ClearWorkAnimation(); // plain move: Pawn_Run / Pawn_Idle
                selected[i].MoveTo(IsWalkable(spot) ? spot : target);
            }
        }

        /// <summary>Spots evenly along the dragged line (at least the formation spacing apart), like RimWorld.</summary>
        void UpdateFormation(Vector2 from, Vector2 to)
        {
            formationSpots.Clear();
            int count = selected.Count;
            float spacing = Settings.formationSpacing;
            var line = to - from;
            float length = line.magnitude;
            var direction = length > 0.001f ? line / length : Vector2.right;
            float step = count > 1 ? Mathf.Max(spacing, length / (count - 1)) : 0f;
            for (int i = 0; i < count; i++) formationSpots.Add(from + direction * step * i);

            while (formationMarkers.Count < count)
            {
                var marker = new GameObject("Formation Marker").AddComponent<SpriteRenderer>();
                marker.transform.SetParent(transform, false);
                marker.sprite = Unit.RingSprite;
                marker.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                marker.sortingOrder = short.MaxValue - 40;
                formationMarkers.Add(marker);
            }
            for (int i = 0; i < formationMarkers.Count; i++)
            {
                bool used = i < count;
                formationMarkers[i].enabled = used;
                if (!used) continue;
                formationMarkers[i].transform.position = formationSpots[i];
                formationMarkers[i].color = IsWalkable(formationSpots[i]) ? formationMarkerColor : new Color(1f, 0.4f, 0.4f, 0.8f);
            }
        }

        void IssueFormationMove()
        {
            if (formationSpots.Count == 0) return;
            // Units keep their order along the line (no crossing paths): sort both by their position along it
            var direction = formationSpots.Count > 1 ? (formationSpots[formationSpots.Count - 1] - formationSpots[0]).normalized : Vector2.right;
            var units = new List<Unit>(selected);
            units.Sort((a, b) => Vector2.Dot(a.transform.position, direction).CompareTo(Vector2.Dot(b.transform.position, direction)));
            for (int i = 0; i < units.Count && i < formationSpots.Count; i++)
            {
                units[i].Worker?.ClearJobs();
                units[i].ClearWorkAnimation();
                units[i].MoveTo(formationSpots[i]);
            }
        }

        void HideFormation()
        {
            foreach (var marker in formationMarkers) if (marker != null) marker.enabled = false;
        }

        static bool IsWalkable(Vector2 world) =>
            MapData.Instance == null || !MapData.Instance.IsReady || MapData.Instance.IsWalkable(world);

        // ------------------------------------------------------------------ selection box

        void OnGUI()
        {
            if (!leftDragging || Mouse.current == null) return;
            var keyboard = Keyboard.current;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            Vector2 a = leftStart, b = Mouse.current.position.ReadValue();
            // Orange = objects (Shift), blue = characters
            if (shift)
            {
                ObjectSelection.DrawScreenBox(a, b);
                return;
            }

            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                                       Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
            var texture = Texture2D.whiteTexture;
            var old = GUI.color;
            GUI.color = unitBoxFill;
            GUI.DrawTexture(rect, texture);
            GUI.color = unitBoxBorder;
            const float t = 2f;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, t), texture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - t, rect.width, t), texture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, t, rect.height), texture);
            GUI.DrawTexture(new Rect(rect.xMax - t, rect.yMin, t, rect.height), texture);
            GUI.color = old;
        }
    }
}
