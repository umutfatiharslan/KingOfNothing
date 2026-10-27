using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// Build menu (B key or the "Build" button at the bottom left): choose a building, a see-through copy follows
    /// the mouse (red = cannot be built there: not land, objects in the way, castle limit, not enough materials;
    /// bridges go on land or water and may overlap other structures in developer mode), left click places a template (25% see-through). Villagers do not start on their own: select
    /// villagers, right click the template and choose Build. Shift keeps the building for another template; right
    /// click or Escape cancels. In Default mode a template needs its materials first: villagers carry them there
    /// (Supply materials: from piles on the ground or the stores) and only then can it be built; developer mode
    /// builds for free. Also draws the resource bar (top left), the game day and the game over screen.
    /// </summary>
    [DefaultExecutionOrder(-95)] // decides CapturesMouse before SelectionManager reads it
    public class BuildMenuController : MonoBehaviour
    {
        const int WindowId = 0x42554C;

        [SerializeField] MapGenerator generator;

        public static BuildMenuController Instance { get; private set; }
        /// <summary>True while a building is being placed (other systems ignore clicks then).</summary>
        public static bool CapturesMouse { get; private set; }

        bool open;
        BuildingDefinition placing;
        Vector2Int placingCell;
        bool placingValid;
        string placingProblem;
        SpriteRenderer ghost;
        Rect windowRect = new Rect(10f, 0f, 320f, 10f);
        Rect buttonRect;
        Vector2 scroll;
        Camera cam;
        bool gameOver;
        string gameOverText;
        GUIStyle resourceStyle;

        static GameplaySettings Settings => MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;

        void Awake()
        {
            Instance = this;
            GameClock.Ensure();
            Building.AnyFell += OnBuildingFell;
        }

        void Start()
        {
            if (generator == null) generator = FindAnyObjectByType<MapGenerator>();
        }

        void OnDestroy()
        {
            Building.AnyFell -= OnBuildingFell;
            if (Instance == this) Instance = null;
            CapturesMouse = false;
            if (ghost != null) Destroy(ghost.gameObject);
        }

        void Update()
        {
            CapturesMouse = placing != null;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard[Settings.buildMenuKey].wasPressedThisFrame)
            {
                if (placing != null) CancelPlacement();
                else open = !open;
            }
            if (placing == null) return;
            if (cam == null) cam = Camera.main;
            var map = MapData.Instance;
            if (mouse == null || cam == null || map == null || !map.IsReady) return;

            Vector2 screen = mouse.position.ReadValue();
            Vector2 world = cam.ScreenToWorldPoint(screen);
            var footprint = placing.Footprint;
            var mouseCell = MapData.WorldToCell(world);
            placingCell = new Vector2Int(mouseCell.x - footprint.x / 2, mouseCell.y - (placing.isBridge ? footprint.y / 2 : 0));
            placingValid = CanPlace(placing, placingCell, map, out placingProblem);
            UpdateGhost();

            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (mouse.rightButton.wasPressedThisFrame || escape)
            {
                CancelPlacement();
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame && !ScreenUi.IsPointerOver(screen) && placingValid)
            {
                Place(placing, placingCell, map);
                bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                if (!shift) CancelPlacement();
            }
        }

        // ------------------------------------------------------------------ placement

        void BeginPlacement(BuildingDefinition definition)
        {
            CancelPlacement();
            placing = definition;
            CapturesMouse = true;
            var go = new GameObject("Building Preview");
            ghost = go.AddComponent<SpriteRenderer>();
            ghost.sprite = definition.PreviewSprite;
            ghost.sortingOrder = short.MaxValue - 20;
            go.transform.localScale = new Vector3(definition.finishedScale, definition.finishedScale, 1f);
        }

        void CancelPlacement()
        {
            // CapturesMouse stays on for this frame (the click that ended the placement is not a selection click)
            placing = null;
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
        }

        void UpdateGhost()
        {
            if (ghost == null || ghost.sprite == null) return;
            ghost.transform.position = SpritePosition(placing, ghost.sprite, placingCell);
            ghost.color = placingValid ? Settings.placementValidTint : Settings.placementInvalidTint;
        }

        /// <summary>Where the finished sprite of a building on this cell is drawn (same rules as Building).</summary>
        public static Vector3 SpritePosition(BuildingDefinition definition, Sprite sprite, Vector2Int cell)
        {
            float scale = definition.finishedScale;
            var ground = WorldObjectFactory.BasePoint(definition.Footprint, cell, Vector2.zero);
            Vector2 position;
            if (definition.centerOnFootprint)
                position = new Vector2(cell.x + definition.Footprint.x * 0.5f, cell.y + definition.Footprint.y * 0.5f) - (Vector2)sprite.bounds.center * scale;
            else
                position = ground + new Vector2(-sprite.bounds.center.x * scale, (sprite.bounds.extents.y - sprite.bounds.center.y - definition.GroundInset) * scale);
            return new Vector3(position.x, position.y, 0f);
        }

        /// <summary>Default mode templates wait for their materials (carried by villagers); developer mode builds for free.</summary>
        public static bool NeedsMaterials(BuildingDefinition definition) =>
            !GameModeManager.IsDeveloperMode && definition.buildCost != null && definition.buildCost.Exists(c => c != null && c.item != null && c.amount > 0);

        /// <summary>Building rules: land (bridges: over water, end to end), nothing in the way, the limit, the costs.</summary>
        public static bool CanPlace(BuildingDefinition definition, Vector2Int cell, MapData map, out string problem)
        {
            problem = null;
            if (definition.maxCount >= 0 && Building.CountOf(definition) >= definition.maxCount)
            {
                problem = definition.maxCount == 1 ? $"Only one {definition.DisplayName}" : $"At most {definition.maxCount}";
                return false;
            }
            var area = new RectInt(cell, definition.Footprint);
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!map.InBounds(c)) { problem = "Outside the map"; return false; }
                if (!definition.isBridge && !map.IsLand(c)) { problem = "Needs land"; return false; }
            }
            // Bridges may overlap other structures in developer mode
            bool overlapAllowed = definition.isBridge && GameModeManager.IsDeveloperMode;
            var found = new List<WorldObject>();
            map.CollectObjects(area, found);
            foreach (var o in found)
            {
                // Bridges never lie on top of each other (they join by touching), not even in developer mode
                if (definition.isBridge && o is Building other && other.Info != null && other.Info.isBridge)
                {
                    problem = "Bridges join by touching each other, not by overlapping";
                    return false;
                }
                if (!overlapAllowed && BlocksSite(o)) { problem = $"{o.DisplayName} is in the way"; return false; }
            }
            if (definition.isBridge && !BridgeRules.CanPlace(map, area, out problem)) return false;
            return true;
        }

        /// <summary>
        /// Trees, stones, stumps, item piles and other buildings must be removed first; small things that do not
        /// block building (grass, bones, small mushrooms...) are cleared away by the template.
        /// </summary>
        static bool BlocksSite(WorldObject o) =>
            o != null && (o.BlocksBuilding || o is Building || (o.Definition != null && o.Definition.Kind == WorldObjectKind.Item));

        void Place(BuildingDefinition definition, Vector2Int cell, MapData map)
        {
            bool needs = NeedsMaterials(definition);
            var history = generator != null ? generator.History : null;
            history?.BeginAction($"Template: {definition.DisplayName}");
            var parent = generator != null ? generator.EditsRoot : null;
            var cleared = new List<WorldObject>();
            map.CollectObjects(new RectInt(cell, definition.Footprint), cleared);
            foreach (var o in cleared)
                if (o != null && !o.IsCarried && !BlocksSite(o)) o.Remove(); // only small things (grass, bones...)
            int start = Settings.constructionStartHealth;
            var created = WorldObjectFactory.Create(definition, cell, Vector2.zero, parent, map,
                configure: o => (o as Building)?.BeginConstruction(start, needs));
            history?.EndAction();
            if (created != null)
                Debug.Log($"[Buildings] Template of {definition.DisplayName} placed at {cell}." +
                          (needs ? $" It needs {CostText(definition)}: select villagers, right click it and choose Supply materials, then Build."
                                 : " Select villagers, right click it and choose Build."), created);
        }

        // ------------------------------------------------------------------ game over

        void OnBuildingFell(Building building)
        {
            if (building == null || building.Info == null || !building.Info.gameOverWhenDestroyed) return;
            gameOver = true;
            gameOverText = $"{building.Definition.DisplayName} is gone. The kingdom is lost.";
            Debug.Log("[Buildings] GAME OVER: " + gameOverText);
            if (Settings.pauseOnGameOver) Time.timeScale = 0f;
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            if (gameOver)
            {
                DrawGameOver();
                return;
            }
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return;

            DrawResourceBar();

            // Bottom left: build button and the game day
            var clock = GameClock.Instance;
            string day = clock != null ? $"Day {clock.Day} ({Mathf.FloorToInt(clock.DayProgress * 24f):00}:00)" : "";
            buttonRect = new Rect(10f, Screen.height - 40f, 270f, 30f);
            GUILayout.BeginArea(buttonRect);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? $"Close build menu ({Settings.buildMenuKey})" : $"Build ({Settings.buildMenuKey})", GUILayout.Width(150f)))
                {
                    if (placing != null) CancelPlacement();
                    open = !open;
                }
                GUILayout.Label(day);
            }
            GUILayout.EndArea();
            ScreenUi.Block(buttonRect);

            if (placing != null)
            {
                var hint = new Rect(Screen.width * 0.5f - 260f, 50f, 520f, 26f);
                GUI.Box(hint, placingValid
                    ? $"{placing.DisplayName}: left click to place (Shift: more), right click / Esc: cancel" +
                      (NeedsMaterials(placing) ? $"  - needs {CostText(placing)}" : "")
                    : $"{placing.DisplayName}: {placingProblem}");
            }

            if (!open) return;
            windowRect.y = Screen.height - 50f - windowRect.height;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Build", UiStyle.Window, GUILayout.Width(320f));
            ScreenUi.Block(windowRect);
        }

        /// <summary>Top left: what the stores (castle and houses) hold.</summary>
        void DrawResourceBar()
        {
            if (resourceStyle == null)
                resourceStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, fontSize = 14 };
            var contents = Building.TreasuryContents();
            var items = new List<SpawnableDefinition>();
            foreach (var item in Settings.resourceBarItems)
                if (item != null && !items.Contains(item)) items.Add(item);
            foreach (var item in contents.Keys)
                if (!items.Contains(item)) items.Add(item);

            const float cell = 82f, height = 32f;
            var bar = new Rect(10f, 10f, Mathf.Max(1, items.Count) * cell + 10f, height);
            GUI.Box(bar, GUIContent.none, UiStyle.Window);
            if (items.Count == 0) GUI.Label(new Rect(bar.x + 8f, bar.y, 200f, height), "No stores yet", resourceStyle);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                contents.TryGetValue(item, out int count);
                var slot = new Rect(bar.x + 6f + i * cell, bar.y + 3f, cell, height - 6f);
                DrawSprite(new Rect(slot.x, slot.y, 26f, 26f), item.PreviewSprite);
                GUI.Label(new Rect(slot.x + 30f, slot.y, cell - 30f, 26f), new GUIContent(count.ToString(), item.DisplayName), resourceStyle);
            }
            ScreenUi.Block(bar);
        }

        void DrawWindow(int id)
        {
            var menu = Settings.buildMenu;
            bool developer = GameModeManager.IsDeveloperMode;
            GUILayout.Label(developer ? "Developer mode: building is free." : "Villagers carry the materials to the template (Supply materials).");
            if (menu == null || menu.Count == 0)
            {
                GUILayout.Label("No buildings in the build menu.\nRun King of Nothing > Setup > Create Default Map Assets.");
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(440f, menu.Count * 46f + 10f)));
                foreach (var definition in menu)
                {
                    if (definition == null) continue;
                    bool limitReached = definition.maxCount >= 0 && Building.CountOf(definition) >= definition.maxCount;
                    bool pay = NeedsMaterials(definition);
                    GUI.enabled = !limitReached;
                    using (new GUILayout.HorizontalScope())
                    {
                        var icon = definition.MenuIcon;
                        if (icon != null) DrawSprite(GUILayoutUtility.GetRect(36f, 36f, GUILayout.Width(36f)), icon);
                        string cost = pay ? "Needs " + CostText(definition) : "Free";
                        string label = $"{definition.DisplayName}  ({definition.MaxHealth} HP)\n{cost}" + (limitReached ? " - limit reached" : "");
                        if (GUILayout.Button(label, GUILayout.Height(38f)))
                        {
                            open = false;
                            BeginPlacement(definition);
                        }
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Close")) open = false;
        }

        public static string CostText(BuildingDefinition definition)
        {
            if (definition.buildCost == null || definition.buildCost.Count == 0) return "Free";
            var parts = new List<string>();
            foreach (var c in definition.buildCost)
                if (c != null && c.item != null && c.amount > 0) parts.Add($"{c.amount} {c.item.DisplayName}");
            return parts.Count == 0 ? "Free" : string.Join(", ", parts);
        }

        /// <summary>Draws a sprite of a sliced texture into a GUI rect (keeps the aspect).</summary>
        public static void DrawSprite(Rect area, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            var tex = sprite.texture;
            var r = sprite.textureRect;
            var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            float aspect = r.width / Mathf.Max(1f, r.height);
            var fit = area;
            if (aspect > 1f) fit.height = area.width / aspect;
            else fit.width = area.height * aspect;
            fit.center = area.center;
            GUI.DrawTextureWithTexCoords(fit, tex, uv);
        }

        void DrawGameOver()
        {
            var rect = new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f - 70f, 400f, 140f);
            GUILayout.BeginArea(rect, UiStyle.Window);
            GUILayout.Label("GAME OVER");
            GUILayout.Label(gameOverText);
            if (GUILayout.Button("Continue anyway"))
            {
                gameOver = false;
                Time.timeScale = 1f;
            }
            GUILayout.EndArea();
            ScreenUi.Block(rect);
        }
    }
}
