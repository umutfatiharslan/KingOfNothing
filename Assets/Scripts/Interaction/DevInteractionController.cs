using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using KingOfNothing.Utilities;
using KingOfNothing.World.Persistence;
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
    ///  - Characters: choose a profession and click the map to add a character
    /// The tools only work while the panel is open: closing it with F2 pauses the current tool, opening it again
    /// brings it back. Clicks on other panels and buttons (build menu...) never reach the tools.
    /// Every change (also hits, and everything that happens in Default mode) is saved into the edited map file,
    /// never into the original map. The panel can undo / redo changes and reset the map to the original
    /// (deletes the edited map file). Right click or Escape cancels the tool.
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
            Characters,
        }

        static readonly string[] ToolNames = { "Select", "Hit", "Destroy", "Place", "Move", "Characters" };
        static readonly KingOfNothing.Characters.VillagerRole[] SpawnRoles =
        {
            KingOfNothing.Characters.VillagerRole.Villager, KingOfNothing.Characters.VillagerRole.Warrior,
            KingOfNothing.Characters.VillagerRole.Lancer, KingOfNothing.Characters.VillagerRole.Archer,
            KingOfNothing.Characters.VillagerRole.Priest, KingOfNothing.Characters.VillagerRole.King,
        };
        const int WindowId = 0x4B4F4E;

        [SerializeField] MapGenerator generator;
        [Tooltip("Tall sprites (trees) are drawn above their cell; also look this many cells below the cursor.")]
        [Range(0, 4)] [SerializeField] int searchCellsBelow = 2;
        [SerializeField] bool logActions = true;
        [SerializeField] Key togglePanelKey = Key.F2;
        [SerializeField] bool showPanel = true;

        /// <summary>True while a tool uses the mouse; other systems (unit selection) ignore clicks then.</summary>
        public static bool CapturesMouse { get; private set; }

        DevTool tool;
        SpawnableDefinition placeDefinition;
        List<SpawnableDefinition> sortedPalette;
        int paletteSourceCount = -1;
        int spawnRole;
        SpriteRenderer placeGhost;
        Vector2 paletteScroll;
        Rect windowRect = new Rect(10, 52, 330, 120);
        bool confirmReset;
        float confirmResetUntil;
        Camera cam;

        // Brush strokes (Place / Destroy while the button is held) form one undo step
        bool stroking;
        int strokeCount;
        Vector2 lastBrushPoint;

        // Box selection (Shift + drag in Select, drag in Move)
        bool boxPressing, boxDragging;
        Vector2 boxStart;

        readonly PlacementPreview preview = new PlacementPreview();

        static InteractionSettings Settings => MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;

        /// <summary>Damage of the Hit tool (stored in the InteractionSettings asset).</summary>
        public int HitPower
        {
            get => Settings.developerHitPower;
            set => Settings.developerHitPower = Mathf.Max(1, value);
        }

        void Start()
        {
            if (generator == null) generator = FindAnyObjectByType<MapGenerator>();
        }

        void OnDisable()
        {
            CapturesMouse = false;
            CancelAll();
        }

        void OnDestroy()
        {
            if (placeGhost != null) Destroy(placeGhost.gameObject);
        }

        void Update()
        {
            CapturesMouse = false;
            if (!GameModeManager.IsDeveloperMode)
            {
                CancelAll();
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[togglePanelKey].wasPressedThisFrame) showPanel = !showPanel;
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (preview.IsActive) preview.End();
                    else SetTool(DevTool.None);
                }
                if (keyboard.deleteKey.wasPressedThisFrame) DestroySelection();
            }
            if (mouse == null) return;

            // Closed panel: the current tool rests until the panel is opened again
            if (!showPanel)
            {
                CancelAll();
                return;
            }

            // Over this panel or any other panel / button (build menu, resource bar...): no tool action
            bool overPanel = windowRect.Contains(GuiMousePosition(mouse)) || ScreenUi.IsPointerOver(mouse.position.ReadValue());
            if (stroking && !mouse.leftButton.isPressed) EndStroke();
            CapturesMouse = tool != DevTool.None || overPanel || boxPressing;
            UpdatePlaceGhost(mouse, overPanel);
            if (overPanel && !stroking && !boxPressing) return;

            if (cam == null) cam = Camera.main;
            var map = MapData.Instance;
            if (cam == null || map == null || !map.IsReady) return;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 world = cam.ScreenToWorldPoint(screen);
            bool ctrl = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool left = mouse.leftButton.wasPressedThisFrame;
            bool right = mouse.rightButton.wasPressedThisFrame;

            switch (tool)
            {
                case DevTool.None:
                    // Selecting (click / Shift / boxes) is done by the SelectionManager like in Default mode
                    if (ctrl)
                    {
                        CapturesMouse = true;
                        if (left) HitAt(world);
                        else if (right) DestroyAt(world, true);
                    }
                    return;

                case DevTool.Hit:
                    if (right) SetTool(DevTool.None);
                    else if (left) HitAt(world);
                    return;

                case DevTool.Destroy:
                    if (right) SetTool(DevTool.None);
                    else if (left) BeginStroke("Destroy");
                    if (stroking) DestroyAt(world, false);
                    return;

                case DevTool.Place:
                    if (right) SetTool(DevTool.None);
                    else if (left)
                    {
                        BeginStroke("Place");
                        PlaceAt(world);
                        lastBrushPoint = world;
                    }
                    else if (stroking && Vector2.Distance(world, lastBrushPoint) >= Settings.brushSpacing)
                    {
                        PlaceAt(world);
                        lastBrushPoint = world;
                    }
                    return;

                case DevTool.Characters:
                    if (right) SetTool(DevTool.None);
                    else if (left) SpawnCharacterAt(world);
                    return;

                case DevTool.Move:
                    if (preview.IsActive)
                    {
                        preview.Update(MapData.WorldToCell(world), map, Settings, true);
                        if (right) preview.End();
                        else if (left) ConfirmMove();
                        return;
                    }
                    if (right && !boxPressing)
                    {
                        SetTool(DevTool.None);
                        return;
                    }
                    BoxSelect(mouse, screen, world, map, shift, true);
                    return;
            }
        }

        static Vector2 GuiMousePosition(Mouse mouse)
        {
            Vector2 p = mouse.position.ReadValue();
            return new Vector2(p.x, Screen.height - p.y);
        }

        void SetTool(DevTool newTool)
        {
            if (stroking) EndStroke();
            preview.End();
            tool = newTool;
            // Switching to Move with a selection: choose where the selected objects go
            if (tool == DevTool.Move && ObjectSelection.Count > 0) preview.Begin(ObjectSelection.Selected, null);
        }

        void CancelAll()
        {
            if (stroking) EndStroke();
            preview.End();
            boxPressing = boxDragging = false;
            if (placeGhost != null) placeGhost.enabled = false;
        }

        // ------------------------------------------------------------------ palette and place preview

        /// <summary>The placeable objects in alphabetical order.</summary>
        List<SpawnableDefinition> SortedPalette
        {
            get
            {
                var palette = generator != null ? generator.DeveloperPalette : null;
                int count = palette != null ? palette.Count : 0;
                if (sortedPalette == null || paletteSourceCount != count)
                {
                    paletteSourceCount = count;
                    sortedPalette = new List<SpawnableDefinition>();
                    if (palette != null) foreach (var d in palette) if (d != null) sortedPalette.Add(d);
                    sortedPalette.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
                }
                return sortedPalette;
            }
        }

        /// <summary>See-through copy of the chosen object under the mouse (Place tool), like the move preview.</summary>
        void UpdatePlaceGhost(Mouse mouse, bool overPanel)
        {
            bool show = tool == DevTool.Place && !overPanel && placeDefinition != null && placeDefinition.PreviewSprite != null;
            if (!show)
            {
                if (placeGhost != null) placeGhost.enabled = false;
                return;
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            if (placeGhost == null)
            {
                placeGhost = new GameObject("Place Preview").AddComponent<SpriteRenderer>();
                placeGhost.sortingOrder = short.MaxValue - 20;
            }
            Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            var sprite = placeDefinition.PreviewSprite;
            placeGhost.enabled = true;
            placeGhost.sprite = sprite;
            placeGhost.color = Settings.previewTint;
            var cell = PlaceCell(placeDefinition, world);
            float scale = placeDefinition is BuildingDefinition b ? b.finishedScale : placeDefinition.VisualScale;
            placeGhost.transform.localScale = new Vector3(scale, scale, 1f);
            placeGhost.transform.position = placeDefinition is BuildingDefinition building
                ? BuildMenuController.SpritePosition(building, sprite, cell)
                : WorldObjectFactory.SpritePosition(placeDefinition, sprite, cell, Vector2.zero);
        }

        /// <summary>Cell an object is placed on for the mouse position (big objects are centred on the mouse).</summary>
        static Vector2Int PlaceCell(SpawnableDefinition definition, Vector2 world)
        {
            var cell = MapData.WorldToCell(world);
            var footprint = definition.Footprint;
            return new Vector2Int(cell.x - footprint.x / 2, cell.y);
        }

        // ------------------------------------------------------------------ characters

        void SpawnCharacterAt(Vector2 world)
        {
            var map = MapData.Instance;
            if (map == null || !map.IsWalkable(world)) return;
            var spawner = FindAnyObjectByType<KingOfNothing.Characters.StartingPartySpawner>();
            if (spawner == null)
            {
                Debug.LogWarning("[Dev] No StartingPartySpawner in the scene (it holds the villager prefab).");
                return;
            }
            var role = SpawnRoles[Mathf.Clamp(spawnRole, 0, SpawnRoles.Length - 1)];
            var villager = spawner.SpawnVillager(role, world);
            if (villager != null && logActions) Debug.Log($"[Dev] Added {villager.DisplayName} ({role})");
        }

        MapChangeHistory History => generator != null ? generator.History : null;

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// Press / drag / release handling of a box selection. A click selects the object under the cursor
        /// (toggles it when additive). With startMove the selected objects are then moved (Move tool).
        /// </summary>
        void BoxSelect(Mouse mouse, Vector2 screen, Vector2 world, MapData map, bool additive, bool startMove)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                boxPressing = true;
                boxDragging = false;
                boxStart = screen;
            }
            if (!boxPressing) return;
            if (!boxDragging && (screen - boxStart).magnitude > Settings.dragThreshold) boxDragging = true;
            if (!mouse.leftButton.wasReleasedThisFrame) return;

            bool wasDrag = boxDragging;
            if (wasDrag)
            {
                Vector2 a = cam.ScreenToWorldPoint(boxStart);
                ObjectSelection.SelectInWorldRect(Rect.MinMaxRect(Mathf.Min(a.x, world.x), Mathf.Min(a.y, world.y),
                    Mathf.Max(a.x, world.x), Mathf.Max(a.y, world.y)), map, additive);
            }
            else
            {
                var clicked = FindObject(world);
                if (additive) ObjectSelection.Toggle(clicked);
                else
                {
                    ObjectSelection.Clear();
                    if (clicked != null) ObjectSelection.Add(clicked);
                }
            }
            boxPressing = boxDragging = false;

            if (startMove && ObjectSelection.Count > 0)
            {
                var anchor = wasDrag ? null : FindObject(world);
                preview.Begin(ObjectSelection.Selected, ObjectSelection.Contains(anchor) ? anchor : null);
            }
        }

        void DestroySelection()
        {
            var targets = new List<WorldObject>(ObjectSelection.Selected);
            if (targets.Count == 0) return;
            ObjectSelection.Clear();
            var history = History;
            history?.BeginAction($"Destroy {targets.Count} objects");
            foreach (var o in targets) if (o != null) o.Remove();
            history?.EndAction();
            if (logActions) Debug.Log($"[Dev] Destroyed {targets.Count} selected objects");
        }

        // ------------------------------------------------------------------ tools

        WorldObject FindObject(Vector2 world) => ObjectSelection.FindAt(MapData.Instance, world, searchCellsBelow);

        void HitAt(Vector2 world)
        {
            var target = FindObject(world);
            if (!(target is IDamageable damageable) || !damageable.CanBeHit) return;
            int power = HitPower;
            damageable.ApplyHit(power, null);
            if (logActions)
            {
                string health = target is ResourceNode node ? $" ({node.Health}/{node.MaxHealth} HP)" : "";
                Debug.Log($"[Dev] Hit {target.DisplayName} for {power}{health}");
            }
        }

        void BeginStroke(string label)
        {
            if (stroking) EndStroke();
            stroking = true;
            strokeCount = 0;
            History?.BeginAction(label);
        }

        void EndStroke()
        {
            stroking = false;
            History?.EndAction();
            if (logActions && strokeCount > 0) Debug.Log($"[Dev] {(tool == DevTool.Place ? "Placed" : "Destroyed")} {strokeCount} objects");
        }

        /// <summary>Destroys the object under the cursor (inside a brush stroke, or as its own undo step).</summary>
        void DestroyAt(Vector2 world, bool ownAction)
        {
            var target = FindObject(world);
            if (target == null) return;
            ObjectSelection.Remove(target);
            var history = History;
            if (ownAction) history?.BeginAction($"Destroy {target.DisplayName}");
            target.Remove();
            if (ownAction) history?.EndAction();
            strokeCount++;
        }

        /// <summary>Places the palette object on the cell under the cursor if it is free (brush skips occupied cells).</summary>
        void PlaceAt(Vector2 world)
        {
            var definition = placeDefinition;
            var map = MapData.Instance;
            if (definition == null || map == null) return;
            var cell = PlaceCell(definition, world);
            // Developer mode: objects may be placed into / onto each other - except loot, which stacks
            if (definition == null || !map.InBounds(cell)) return;
            if (IsLoot(definition))
            {
                PlaceLoot(definition, cell, map);
                return;
            }
            if (WorldObjectFactory.Create(definition, cell, Vector2.zero, generator.EditsRoot, map) != null) strokeCount++;
        }

        /// <summary>Piles of items (wood, gold, coins, small stones / mushrooms...).</summary>
        static bool IsLoot(SpawnableDefinition definition) =>
            definition.Kind == WorldObjectKind.Item || (definition.IsCollectable && definition.MovableByCharacters);

        static readonly List<WorldObject> LootNearby = new List<WorldObject>();

        /// <summary>
        /// Loot placed with the developer tools stacks like dropped loot: it is added to a pile of the same item on
        /// the cell (up to the pile size); a full pile sends it to the nearest free cell next to it.
        /// </summary>
        void PlaceLoot(SpawnableDefinition definition, Vector2Int cell, MapData map)
        {
            LootNearby.Clear();
            map.CollectObjects(new RectInt(cell, Vector2Int.one), LootNearby);
            foreach (var o in LootNearby)
            {
                if (o == null || o.IsCarried || o.Definition != definition || o.Amount >= definition.StackSize) continue;
                o.AddAmount(1);
                strokeCount++;
                return;
            }
            bool occupied = LootNearby.Exists(o => o != null && !o.IsCarried);
            if (occupied && !ItemDropper.FindFreeCell(map, new RectInt(cell, Vector2Int.one), 1, out cell)) return;
            if (WorldObjectFactory.Create(definition, cell, Vector2.zero, generator.EditsRoot, map) != null) strokeCount++;
        }

        /// <summary>Developer move: instant, overlapping other objects is allowed.</summary>
        void ConfirmMove()
        {
            var objects = new List<WorldObject>(preview.Objects);
            var targets = new List<Vector2Int>(preview.Targets);
            preview.End();
            var history = History;
            history?.BeginAction(objects.Count == 1 && objects[0] != null ? $"Move {objects[0].DisplayName}" : $"Move {objects.Count} objects");
            for (int i = 0; i < objects.Count; i++)
                if (objects[i] != null) objects[i].MoveTo(targets[i]);
            history?.EndAction();
            ObjectSelection.Clear();
            if (logActions) Debug.Log($"[Dev] Moved {objects.Count} objects");
        }


        // ------------------------------------------------------------------ panel

        void OnGUI()
        {
            if (!GameModeManager.IsDeveloperMode) return;
            if (boxDragging && Mouse.current != null)
                ObjectSelection.DrawScreenBox(boxStart, Mouse.current.position.ReadValue());
            if (!showPanel) return;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Developer Tools (F2)", UiStyle.Window);
            ScreenUi.Block(windowRect); // no map zoom / clicks through the panel
        }

        void DrawWindow(int id)
        {
            int selected = GUILayout.SelectionGrid((int)tool, ToolNames, 3);
            if (selected != (int)tool) SetTool((DevTool)selected);

            switch (tool)
            {
                case DevTool.None:
                    GUILayout.Label("Click / Shift+click / Shift+drag: select  |  Del: delete selected");
                    GUILayout.Label("Right click on objects: actions (Destroy / Move Developer Mode)");
                    GUILayout.Label("Ctrl+Left: hit  |  Ctrl+Right: destroy");
                    break;
                case DevTool.Hit:
                    GUILayout.Label($"Hit power: {HitPower}");
                    HitPower = Mathf.RoundToInt(GUILayout.HorizontalSlider(HitPower, 1, 200));
                    break;
                case DevTool.Destroy:
                    GUILayout.Label("Click, or hold and drag to delete many.");
                    break;
                case DevTool.Place:
                    GUILayout.Label("Click, or hold and drag to paint (overlapping allowed).");
                    DrawPalette();
                    break;
                case DevTool.Characters:
                    DrawCharacters();
                    break;
                case DevTool.Move:
                    GUILayout.Label(preview.IsActive
                        ? $"Click where to put {preview.Objects.Count} object(s) (overlap allowed). Right click: cancel."
                        : "Click an object, or drag a box over several.");
                    break;
            }
            int count = ObjectSelection.Count;
            if (count > 0)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Selected: {count}");
                    if (GUILayout.Button("Delete")) DestroySelection();
                    if (GUILayout.Button("Clear")) ObjectSelection.Clear();
                }
            }

            GUILayout.Space(6);
            DrawEditedMap();
            GUI.DragWindow();
        }

        void DrawEditedMap()
        {
            var history = History;
            if (history == null)
            {
                GUILayout.Label(generator != null && !generator.ShowsOriginalMap
                    ? "Test map (not the original): changes are not saved."
                    : "Changes are not saved (see MapGenerator > Edited map).");
                return;
            }

            GUILayout.Label($"Edited map: {history.AppliedCount} changes" +
                            (history.HistoryCount > history.AppliedCount ? $" ({history.HistoryCount - history.AppliedCount} undone)" : "") +
                            (history.FileExists ? "" : " - no file yet (= original)"));

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = history.CanUndo;
                if (GUILayout.Button(new GUIContent("Undo", history.UndoLabel))) Undo();
                GUI.enabled = history.CanRedo;
                if (GUILayout.Button(new GUIContent("Redo", history.RedoLabel))) Redo();
                GUI.enabled = true;
            }
            string undo = history.UndoLabel;
            if (undo != null) GUILayout.Label("Last: " + undo);

            if (confirmReset && Time.unscaledTime > confirmResetUntil) confirmReset = false;
            if (!confirmReset)
            {
                if (GUILayout.Button("Reset map to original")) 
                {
                    confirmReset = true;
                    confirmResetUntil = Time.unscaledTime + 5f;
                }
            }
            else
            {
                GUILayout.Label("Deletes the edited map file. Sure?");
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Yes, reset"))
                    {
                        confirmReset = false;
                        CancelAll();
                        generator.ResetToOriginal();
                    }
                    if (GUILayout.Button("Cancel")) confirmReset = false;
                }
            }
            if (GUILayout.Button(new GUIContent("Open saves folder", history.FilePath))) OpenSavesFolder();
        }

        void Undo()
        {
            CancelAll();
            var history = History;
            if (history == null) return;
            string label = history.UndoLabel;
            if (history.Undo() && logActions) Debug.Log($"[Dev] Undo: {label}");
        }

        void Redo()
        {
            CancelAll();
            var history = History;
            if (history == null) return;
            string label = history.RedoLabel;
            if (history.Redo() && logActions) Debug.Log($"[Dev] Redo: {label}");
        }

        static void OpenSavesFolder()
        {
            string folder = EditedMapFile.Folder;
            System.IO.Directory.CreateDirectory(folder);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(folder);
#else
            Application.OpenURL("file://" + folder);
#endif
        }

        void DrawPalette()
        {
            var palette = SortedPalette;
            if (palette.Count == 0)
            {
                GUILayout.Label("No objects in the settings.");
                return;
            }
            if (placeDefinition == null || !palette.Contains(placeDefinition)) placeDefinition = palette[0];
            paletteScroll = GUILayout.BeginScrollView(paletteScroll, GUILayout.Height(220));
            foreach (var definition in palette)
            {
                bool on = GUILayout.Toggle(definition == placeDefinition, definition.DisplayName, "Button");
                if (on) placeDefinition = definition;
            }
            GUILayout.EndScrollView();
        }

        void DrawCharacters()
        {
            GUILayout.Label("Choose a profession, then click the map to add a character.");
            var names = new string[SpawnRoles.Length];
            for (int i = 0; i < names.Length; i++) names[i] = SpawnRoles[i].ToString();
            spawnRole = GUILayout.SelectionGrid(Mathf.Clamp(spawnRole, 0, names.Length - 1), names, 3);
        }
    }
}
