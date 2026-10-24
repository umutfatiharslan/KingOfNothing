using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// Developer tools (developer mode only), shown as a panel in the top left corner (toggle with F2):
    ///  - Hit: left click hits the object under the cursor with Hit Power
    ///  - Destroy: one left click deletes the object under the cursor
    ///  - Place: left click places the object chosen in the panel
    ///  - Move: click an object, then click the cell it should move to
    /// Destroy / Place / Move are permanent: they are written to the Map Edit Log and replayed every time the
    /// same map is generated, so they survive leaving Play mode. Right click or Escape cancels the tool.
    /// Shortcuts without a tool: Ctrl + left click = hit, Ctrl + right click = destroy.
    /// </summary>
    [DefaultExecutionOrder(-100)] // decides CapturesMouse before SelectionManager reads it
    public class DevInteractionController : MonoBehaviour
    {
        public enum DevTool
        {
            None,
            Hit,
            Destroy,
            Place,
            Move,
        }

        static readonly string[] ToolNames = { "Select", "Hit", "Destroy", "Place", "Move" };
        const int WindowId = 0x4B4F4E;

        [SerializeField] MapGenerator generator;
        [Min(1)] [SerializeField] int hitPower = 5;
        [Tooltip("Tall sprites (trees) are drawn above their cell; also look this many cells below the cursor.")]
        [Range(0, 4)] [SerializeField] int searchCellsBelow = 2;
        [SerializeField] bool logActions = true;
        [SerializeField] Key togglePanelKey = Key.F2;
        [SerializeField] bool showPanel = true;
        [SerializeField] Color moveHighlight = new Color(1f, 0.85f, 0.3f, 1f);

        /// <summary>True while a tool uses the mouse; other systems (unit selection) ignore clicks then.</summary>
        public static bool CapturesMouse { get; private set; }

        DevTool tool;
        int paletteIndex;
        Vector2 paletteScroll;
        Rect windowRect = new Rect(10, 10, 260, 120);
        WorldObject moving;
        Color movingOriginalColor;
        Camera cam;

        public int HitPower
        {
            get => hitPower;
            set => hitPower = Mathf.Max(1, value);
        }

        void Start()
        {
            if (generator == null) generator = FindAnyObjectByType<MapGenerator>();
        }

        void OnDisable() => CapturesMouse = false;

        void Update()
        {
            CapturesMouse = false;
            if (!GameModeManager.IsDeveloperMode)
            {
                CancelMove();
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[togglePanelKey].wasPressedThisFrame) showPanel = !showPanel;
                if (keyboard.escapeKey.wasPressedThisFrame) SetTool(DevTool.None);
            }
            if (mouse == null) return;

            bool overPanel = showPanel && windowRect.Contains(GuiMousePosition(mouse));
            CapturesMouse = tool != DevTool.None || overPanel;
            if (overPanel) return;

            if (cam == null) cam = Camera.main;
            if (cam == null || MapData.Instance == null || !MapData.Instance.IsReady) return;

            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            bool ctrl = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool left = mouse.leftButton.wasPressedThisFrame;
            bool right = mouse.rightButton.wasPressedThisFrame;

            if (tool == DevTool.None)
            {
                // Quick shortcuts
                if (ctrl && left) HitAt(world);
                else if (ctrl && right) DestroyAt(world);
                return;
            }

            if (right)
            {
                if (tool == DevTool.Move && moving != null) CancelMove();
                else SetTool(DevTool.None);
                return;
            }
            if (!left) return;

            switch (tool)
            {
                case DevTool.Hit: HitAt(world); break;
                case DevTool.Destroy: DestroyAt(world); break;
                case DevTool.Place: PlaceAt(world); break;
                case DevTool.Move: MoveAt(world); break;
            }
        }

        static Vector2 GuiMousePosition(Mouse mouse)
        {
            Vector2 p = mouse.position.ReadValue();
            return new Vector2(p.x, Screen.height - p.y);
        }

        void SetTool(DevTool newTool)
        {
            if (tool == DevTool.Move && newTool != DevTool.Move) CancelMove();
            tool = newTool;
        }

        // ------------------------------------------------------------------ tools

        WorldObject FindObject(Vector2 world)
        {
            var cell = MapData.WorldToCell(world);
            for (int dy = 0; dy <= searchCellsBelow; dy++)
            {
                var o = MapData.Instance.GetObjectAt(new Vector2Int(cell.x, cell.y - dy));
                if (o != null) return o;
            }
            return null;
        }

        void HitAt(Vector2 world)
        {
            var target = FindObject(world);
            if (!(target is IDamageable damageable) || !damageable.CanBeHit) return;
            damageable.ApplyHit(hitPower, null);
            if (logActions)
            {
                string health = target is ResourceNode node ? $" ({node.Health}/{node.MaxHealth} HP)" : "";
                Debug.Log($"[Dev] Hit {target.DisplayName} for {hitPower}{health}");
            }
        }

        void DestroyAt(Vector2 world)
        {
            var target = FindObject(world);
            if (target == null) return;
            Record(MapEditType.Remove, target, target.Cell, target.Cell);
            if (logActions) Debug.Log($"[Dev] Destroyed {target.DisplayName} at {target.Cell}");
            target.Remove();
        }

        void PlaceAt(Vector2 world)
        {
            var palette = generator != null ? generator.DeveloperPalette : null;
            if (palette == null || palette.Count == 0) return;
            paletteIndex = Mathf.Clamp(paletteIndex, 0, palette.Count - 1);
            var definition = palette[paletteIndex];
            var cell = MapData.WorldToCell(world);
            if (!MapData.Instance.InBounds(cell) || MapData.Instance.GetObjectAt(cell) != null) return;

            var created = WorldObjectFactory.Create(definition, cell, Vector2.zero, generator.EditsRoot, MapData.Instance);
            if (created == null) return;
            Record(MapEditType.Add, created, cell, cell);
            if (logActions) Debug.Log($"[Dev] Placed {definition.DisplayName} at {cell}");
        }

        void MoveAt(Vector2 world)
        {
            if (moving == null)
            {
                moving = FindObject(world);
                if (moving == null) return;
                var sr = moving.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    movingOriginalColor = sr.color;
                    sr.color = moveHighlight;
                }
                return;
            }

            var cell = MapData.WorldToCell(world);
            var occupant = MapData.Instance.GetObjectAt(cell);
            if (!MapData.Instance.InBounds(cell) || (occupant != null && occupant != moving)) return;

            var definition = moving.Definition;
            var from = moving.Cell;
            Record(MapEditType.Move, moving, from, cell);
            moving.Remove();
            moving = null;
            WorldObjectFactory.Create(definition, cell, Vector2.zero, generator != null ? generator.EditsRoot : null, MapData.Instance);
            if (logActions && definition != null) Debug.Log($"[Dev] Moved {definition.DisplayName} {from} -> {cell}");
        }

        void CancelMove()
        {
            if (moving == null) return;
            var sr = moving.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.color = movingOriginalColor;
            moving = null;
        }

        void Record(MapEditType type, WorldObject target, Vector2Int cell, Vector2Int targetCell)
        {
            if (generator == null || target == null || target.Definition == null) return;
            generator.RecordEdit(new MapEdit { type = type, objectId = target.Definition.name, cell = cell, targetCell = targetCell });
        }

        // ------------------------------------------------------------------ panel

        void OnGUI()
        {
            if (!showPanel || !GameModeManager.IsDeveloperMode) return;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Developer Tools (F2)");
        }

        void DrawWindow(int id)
        {
            int selected = GUILayout.Toolbar((int)tool, ToolNames);
            if (selected != (int)tool) SetTool((DevTool)selected);

            switch (tool)
            {
                case DevTool.None:
                    GUILayout.Label("Ctrl+Left: hit  |  Ctrl+Right: destroy");
                    break;
                case DevTool.Hit:
                    GUILayout.Label($"Hit power: {hitPower}");
                    hitPower = Mathf.RoundToInt(GUILayout.HorizontalSlider(hitPower, 1, 100));
                    break;
                case DevTool.Destroy:
                    GUILayout.Label("Click an object to delete it (saved).");
                    break;
                case DevTool.Place:
                    DrawPalette();
                    break;
                case DevTool.Move:
                    GUILayout.Label(moving == null ? "Click the object to move." : $"Click the new cell for {moving.DisplayName}.");
                    break;
            }

            var log = generator != null ? generator.EditLog : null;
            GUILayout.Space(4);
            GUILayout.Label(log != null ? $"Saved map edits: {log.Count}" : "No Map Edit Log assigned (edits are not saved)");
            if (log != null && log.Count > 0 && GUILayout.Button("Clear saved edits (regenerate to see)"))
                generator.ClearEdits();
            GUI.DragWindow();
        }

        void DrawPalette()
        {
            var palette = generator != null ? generator.DeveloperPalette : null;
            if (palette == null || palette.Count == 0)
            {
                GUILayout.Label("No objects in the settings.");
                return;
            }
            paletteScroll = GUILayout.BeginScrollView(paletteScroll, GUILayout.Height(220));
            for (int i = 0; i < palette.Count; i++)
            {
                if (palette[i] == null) continue;
                bool on = GUILayout.Toggle(i == paletteIndex, palette[i].DisplayName, "Button");
                if (on) paletteIndex = i;
            }
            GUILayout.EndScrollView();
        }
    }
}
