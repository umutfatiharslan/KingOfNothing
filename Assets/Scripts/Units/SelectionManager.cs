using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.Interaction;
using KingOfNothing.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Units
{
    /// <summary>
    /// RTS unit selection:
    ///  - left click: select a unit (clicking empty ground clears the selection)
    ///  - left drag: select every unit inside the box
    ///  - Shift: add to / remove from the current selection
    ///  - right click: move the selected units there
    /// In developer mode Ctrl + click is reserved for the developer tools.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        [Min(1f)] [SerializeField] float dragThreshold = 6f;
        [Min(0.1f)] [SerializeField] float formationSpacing = 0.8f;
        [SerializeField] Color boxFill = new Color(0.35f, 0.85f, 1f, 0.15f);
        [SerializeField] Color boxBorder = new Color(0.35f, 0.85f, 1f, 0.9f);

        public IReadOnlyList<Unit> Selected => selected;

        readonly List<Unit> selected = new List<Unit>();
        Camera cam;
        Vector2 dragStart;
        bool pressing, dragging;

        void Awake() => cam = Camera.main;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            var keyboard = Keyboard.current;
            bool additive = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            // Developer tools own the mouse while one is active (or Ctrl is held in developer mode)
            bool devModifier = DevInteractionController.CapturesMouse ||
                               (GameModeManager.IsDeveloperMode && keyboard != null &&
                                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed));

            if (mouse.leftButton.wasPressedThisFrame && !devModifier)
            {
                pressing = true;
                dragging = false;
                dragStart = mousePos;
            }

            if (pressing && !dragging && (mousePos - dragStart).magnitude > dragThreshold)
                dragging = true;

            if (pressing && mouse.leftButton.wasReleasedThisFrame)
            {
                if (dragging) BoxSelect(dragStart, mousePos, additive);
                else ClickSelect(mousePos, additive);
                pressing = dragging = false;
            }

            if (mouse.rightButton.wasPressedThisFrame && selected.Count > 0 && !devModifier)
                IssueMove(mousePos);

            selected.RemoveAll(u => u == null);
        }

        // ------------------------------------------------------------------ selection

        void ClickSelect(Vector2 screenPos, bool additive)
        {
            Vector2 world = cam.ScreenToWorldPoint(screenPos);

            // Of overlapping units pick the front one (lowest on screen)
            Unit clicked = null;
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var unit = hit.GetComponentInParent<Unit>();
                if (unit != null && (clicked == null || unit.transform.position.y < clicked.transform.position.y))
                    clicked = unit;
            }

            if (!additive) ClearSelection();
            if (clicked == null) return;

            if (additive && clicked.IsSelected) Deselect(clicked);
            else Select(clicked);
        }

        void BoxSelect(Vector2 a, Vector2 b, bool additive)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (!additive) ClearSelection();

            foreach (var unit in Unit.All)
            {
                // Selected if the middle of the body (a bit above the feet) is inside the box
                Vector2 screen = cam.WorldToScreenPoint(unit.transform.position + Vector3.up * 0.5f);
                if (rect.Contains(screen)) Select(unit);
            }
        }

        void Select(Unit unit)
        {
            if (selected.Contains(unit)) return;
            selected.Add(unit);
            unit.SetSelected(true);
        }

        void Deselect(Unit unit)
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

        // ------------------------------------------------------------------ commands

        void IssueMove(Vector2 screenPos)
        {
            // Unreachable targets (deep water...) are fine: the pathfinder stops at the closest reachable point
            Vector2 target = cam.ScreenToWorldPoint(screenPos);

            // Several units stand in a square formation around the target
            int count = selected.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)cols);
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector2(i % cols - (cols - 1) / 2f, -(i / cols) + (rows - 1) / 2f) * formationSpacing;
                var spot = target + offset;
                selected[i].MoveTo(IsWalkable(spot) ? spot : target);
            }
        }

        static bool IsWalkable(Vector2 world) =>
            MapData.Instance == null || !MapData.Instance.IsReady || MapData.Instance.IsWalkable(world);

        // ------------------------------------------------------------------ selection box

        void OnGUI()
        {
            if (!dragging || Mouse.current == null) return;

            Vector2 a = dragStart, b = Mouse.current.position.ReadValue();
            // GUI coordinates have y pointing down
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                                       Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));

            var texture = Texture2D.whiteTexture;
            var old = GUI.color;
            GUI.color = boxFill;
            GUI.DrawTexture(rect, texture);
            GUI.color = boxBorder;
            const float t = 2f;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, t), texture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - t, rect.width, t), texture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, t, rect.height), texture);
            GUI.DrawTexture(new Rect(rect.xMax - t, rect.yMin, t, rect.height), texture);
            GUI.color = old;
        }
    }
}
