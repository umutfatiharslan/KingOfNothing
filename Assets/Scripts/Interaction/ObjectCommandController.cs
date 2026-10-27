using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.Core;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
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

        static bool IsWorker(Unit u) => u.Villager == null || VillagerRoles.IsWorker(u.Villager.Role);
        static bool IsSoldier(Unit u) => u.Villager != null && VillagerRoles.IsSoldier(u.Villager.Role);

        void BeginMenu(string title, Vector2 screen)
        {
            menuTitle = title;
            menuRect = new Rect(screen.x, Screen.height - screen.y, 220f, 10f);
            expanded = nextExpanded = -1;
            menuOpen = true;
            CapturesMouse = true;
        }

        /// <summary>Opens the actions menu for the object (and the selected objects if it is one of them).</summary>
        public void OpenMenu(WorldObject target, Vector2 screen, IReadOnlyList<Unit> units)
        {
            if (target == null) return;
            menu.Clear();
            menuUnits.Clear();
            foreach (var u in units) if (u != null && !u.IsHidden) menuUnits.Add(u);
            if (target is Building building)
            {
                OpenBuildingMenu(building, screen);
                return;
            }

            var group = new List<WorldObject>();
            if (ObjectSelection.Contains(target)) group.AddRange(ObjectSelection.Selected);
            else group.Add(target);
            group.RemoveAll(o => o == null || o is Building);
            if (group.Count == 0) group.Add(target);
            bool hasUnits = menuUnits.Count > 0;
            bool developer = GameModeManager.IsDeveloperMode;
            var workersAll = menuUnits.FindAll(IsWorker);
            var soldiersAll = menuUnits.FindAll(IsSoldier);

            if (!hasUnits) menu.Add(new MenuItem { Label = "Select characters to give orders", Enabled = false });

            // One entry per kind of work, each only for its own objects (villagers only)
            var byVerb = new Dictionary<string, List<ResourceNode>>();
            var verbs = new List<string>();
            var hittable = new List<ResourceNode>();
            foreach (var o in group)
            {
                if (!(o is ResourceNode node) || !node.CanBeHit) continue;
                hittable.Add(node);
                string verb = VerbFor(node);
                if (!byVerb.TryGetValue(verb, out var list))
                {
                    byVerb[verb] = list = new List<ResourceNode>();
                    verbs.Add(verb);
                }
                list.Add(node);
            }
            // Villagers do every kind of work; soldiers only cut bushes (they do not chop, mine or break anything)
            foreach (var verb in verbs)
            {
                var targets = byVerb[verb];
                var workers = new List<Unit>(workersAll);
                var bushes = targets.FindAll(t => t.Definition != null && t.Definition.Kind == WorldObjectKind.Bush);
                bool soldiersHelp = soldiersAll.Count > 0 && bushes.Count > 0 && bushes.Count == targets.Count;
                if (soldiersHelp) workers.AddRange(soldiersAll);
                if (workers.Count == 0 && soldiersAll.Count > 0)
                {
                    if (bushes.Count == 0 || verb != "Cut") continue; // nothing soldiers may do here
                    workers.AddRange(soldiersAll);
                    targets = bushes;
                }
                bool canWork = workers.Count > 0;
                var item = new MenuItem
                {
                    Label = targets.Count == 1 ? verb : $"{verb} ({targets.Count})",
                    Enabled = canWork,
                    Run = () => AssignWork(workers, targets, verb, false),
                };
                // Individually / together only matter with several characters
                if (workers.Count > 1)
                {
                    item.Children.Add(new MenuItem { Label = $"{verb} individually", Run = () => AssignWork(workers, targets, verb, false) });
                    item.Children.Add(new MenuItem { Label = $"{verb} together", Run = () => AssignWork(workers, targets, verb, true) });
                }
                menu.Add(item);
            }
            if (soldiersAll.Count > 0 && workersAll.Count == 0 && hittable.Count > 0 && menu.TrueForAll(m => !m.Enabled))
                menu.Add(new MenuItem { Label = "Soldiers only cut bushes and attack buildings", Enabled = false });

            // Loot (piles of items): carried in the hands until stocked in a building
            var loot = group.FindAll(IsLoot);
            if (loot.Count > 0)
            {
                var carriers = new List<Unit>(menuUnits);
                menu.Add(new MenuItem
                {
                    Label = loot.Count == 1 ? "Carry loot" : $"Carry loot ({loot.Count})",
                    Enabled = hasUnits,
                    Run = () => AssignCarryLoot(carriers, loot),
                });
            }
            int collectable = group.FindAll(o => o is ResourceNode node && node.IsCollectable && !IsLoot(o)).Count;
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

            AddDeveloperEntries(group, target);
            menu.Add(new MenuItem { Label = "Cancel" });
            BeginMenu(group.Count == 1 ? target.DisplayName : $"{group.Count} objects", screen);
        }

        /// <summary>Piles of items that characters can take in their hands (wood, gold, coins, stones, bones...).</summary>
        static bool IsLoot(WorldObject o) =>
            o is ResourceNode node && node.IsCollectable && o.MovableByCharacters && !o.IsCarried;

        void AddDeveloperEntries(List<WorldObject> group, WorldObject target)
        {
            if (!GameModeManager.IsDeveloperMode) return;
            var all = new List<WorldObject>(group);
            menu.Add(new MenuItem { Label = all.Count == 1 ? "Destroy (Developer Mode)" : $"Destroy (Developer Mode) ({all.Count})", Run = () => DeveloperDestroy(all) });
            menu.Add(new MenuItem { Label = all.Count == 1 ? "Move (Developer Mode)" : $"Move (Developer Mode) ({all.Count})", Run = () => BeginPlacement(all, target, PlacementMode.Developer) });
        }

        // ------------------------------------------------------------------ building menu

        void OpenBuildingMenu(Building b, Vector2 screen)
        {
            var info = b.Info;
            bool hasUnits = menuUnits.Count > 0;
            var workers = menuUnits.FindAll(IsWorker);
            var soldiers = menuUnits.FindAll(IsSoldier);

            // Templates in Default mode first need their materials, carried by villagers
            if (b.RequiresMaterials)
            {
                bool ready = b.MaterialsComplete;
                menu.Add(new MenuItem { Label = (ready ? "Materials ready: " : "Materials: ") + b.MaterialsText(), Enabled = false });
                if (!ready)
                {
                    var suppliers = new List<Unit>(workers);
                    menu.Add(new MenuItem
                    {
                        Label = suppliers.Count == 0 && hasUnits ? "Supply materials - villagers only" : "Supply materials",
                        Enabled = suppliers.Count > 0,
                        Run = () => AssignSupply(suppliers, b),
                    });
                    var carriers = menuUnits.FindAll(u => u.Carrier != null && u.Carrier.IsHolding &&
                                                          b.Missing(StockLootJob.StoredItemOf(u.Carrier.Held.Definition)) > 0);
                    if (carriers.Count > 0)
                        menu.Add(new MenuItem { Label = $"Deliver carried loot ({carriers.Count})", Run = () => AssignStock(carriers, b) });
                }
            }

            if (b.NeedsWork)
            {
                string verb = b.IsComplete ? "Repair" : "Build";
                var builders = new List<Unit>(workers);
                string label = b.LooksDestroyed ? $"Repair (destroyed {b.Health}/{b.MaxHealth})"
                             : b.IsComplete ? $"Repair ({b.Health}/{b.MaxHealth})"
                             : b.IsTemplate ? "Build (start construction)"
                             : $"Build ({b.Health}/{b.MaxHealth})";
                bool materialsReady = b.MaterialsComplete;
                if (!materialsReady) label = "Build (waits for its materials)";
                menu.Add(new MenuItem
                {
                    Label = builders.Count == 0 && hasUnits && materialsReady ? label + " - villagers only" : label,
                    Enabled = builders.Count > 0 && materialsReady,
                    Run = () => AssignBuild(builders, b, verb),
                });
            }
            // A template can be given up at once (first entry); a started site has to be torn down by the
            // characters (Destroy) and leaves part of its materials on the ground
            if (b.IsTemplate)
                menu.Insert(0, new MenuItem
                {
                    Label = b.AnySupplied ? "Give up template (delivered materials back on the ground)" : "Give up template",
                    Run = () => b.CancelConstruction(),
                });
            bool site = !b.IsComplete && !b.IsTemplate;
            if (site)
            {
                var wreckers = new List<Unit>(workers);
                wreckers.AddRange(soldiers);
                int share = Mathf.RoundToInt(MapData.Instance != null ? MapData.Instance.Gameplay.constructionSalvageShare * 100f : 50f);
                menu.Add(new MenuItem
                {
                    Label = $"Destroy (tear down, {share}% of the materials back as loot)",
                    Enabled = wreckers.Count > 0 && b.CanBeHit,
                    Run = () => AssignAttack(wreckers, new List<ResourceNode> { b }),
                });
            }

            // Gold mine: villagers go inside and bring gold out; villagers and soldiers can attack it
            if (info != null && b.Type == BuildingType.GoldMine)
            {
                var miners = new List<Unit>();
                string reason = null;
                foreach (var u in workers)
                    if (b.Accepts(u, out reason)) miners.Add(u);
                string mineLabel = b.MineReserve >= 0 ? $"Mine ({b.MineReserve} gold left)" : "Mine";
                if (miners.Count == 0 && reason != null) mineLabel += $" ({reason})";
                else if (miners.Count == 0 && hasUnits) mineLabel += " - villagers only";
                menu.Add(new MenuItem { Label = mineLabel, Enabled = miners.Count > 0, Run = () => AssignEnter(miners, b) });
            }

            if (info != null && b.Type == BuildingType.Tower)
            {
                var climber = menuUnits.Count > 0 ? menuUnits[0] : null;
                string reason = null;
                bool canClimb = climber != null && b.CanClimb(climber, out reason);
                menu.Add(new MenuItem
                {
                    Label = canClimb || climber == null ? "Climb tower" : $"Climb tower ({reason})",
                    Enabled = canClimb,
                    Run = () => climber.Worker.Assign(new EnterBuildingJob(b, true)),
                });
                if (b.Percher != null)
                    menu.Add(new MenuItem { Label = "Come down from tower", Run = () => b.ComeDown() });
            }
            else if (info != null && b.Capacity != 0 && b.Type != BuildingType.Bridge && b.Type != BuildingType.Furnace && b.Type != BuildingType.GoldMine)
            {
                var entering = new List<Unit>();
                string reason = null;
                foreach (var u in menuUnits)
                    if (b.Accepts(u, out reason)) entering.Add(u);
                string label = entering.Count > 1 ? $"Enter ({entering.Count})" : "Enter";
                if (entering.Count == 0 && hasUnits && reason != null) label = $"Enter ({reason})";
                menu.Add(new MenuItem
                {
                    Label = label,
                    Enabled = entering.Count > 0,
                    Run = () => AssignEnter(entering, b),
                });
            }

            if (info != null && (b.Capacity != 0 || b.Type == BuildingType.Tower) && b.Type != BuildingType.Bridge && b.Type != BuildingType.Furnace)
                menu.Add(new MenuItem { Label = $"Character list ({b.OccupantCount}{(b.Capacity > 0 ? "/" + b.Capacity : "")})", Run = () => Windows.ShowCharacters(b) });

            if (info != null && info.HasInventory)
            {
                menu.Add(new MenuItem { Label = "Inventory", Run = () => Windows.ShowInventory(b) });
                var holders = menuUnits.FindAll(u => u.Carrier != null && u.Carrier.IsHolding);
                string stockLabel = holders.Count > 1 ? $"Stock loot ({holders.Count})" : "Stock loot";
                if (!b.IsUsable) stockLabel += b.IsComplete ? " (destroyed)" : " (not finished)";
                else if (holders.Count == 0) stockLabel += " (nobody carries loot)";
                menu.Add(new MenuItem
                {
                    Label = stockLabel,
                    Enabled = b.IsUsable && holders.Count > 0,
                    Run = () => AssignStock(holders, b),
                });
                if (info.IsFurnace)
                {
                    int coins = b.Inventory.Count(info.furnaceOutput);
                    menu.Add(new MenuItem
                    {
                        Label = $"Collect {info.furnaceOutput.DisplayName} ({coins})",
                        Enabled = hasUnits && coins > 0 && b.IsUsable,
                        Run = () => AssignWithdraw(new List<Unit>(menuUnits), b, info.furnaceOutput),
                    });
                }
            }

            // Soldiers attack buildings; villagers use the same attack on gold mines
            var attackers = new List<Unit>(soldiers);
            // ... and on bridges (bridges can be torn down by everybody)
            bool bridge = info != null && b.Type == BuildingType.Bridge;
            if (info != null && (b.Type == BuildingType.GoldMine || bridge)) attackers.AddRange(workers);
            if (bridge && !site && b.CanBeHit)
            {
                var breakers = new List<Unit>(attackers);
                menu.Add(new MenuItem
                {
                    Label = breakers.Count > 0 ? "Destroy (break the bridge)" : "Destroy (select characters)",
                    Enabled = breakers.Count > 0,
                    Run = () => AssignAttack(breakers, new List<ResourceNode> { b }),
                });
            }
            else if (attackers.Count > 0 && b.CanBeHit && !site)
            {
                menu.Add(new MenuItem { Label = "Attack", Run = () => AssignAttack(attackers, new List<ResourceNode> { b }) });
            }
            if (!hasUnits) menu.Add(new MenuItem { Label = "Select characters to give orders", Enabled = false });

            AddDeveloperEntries(new List<WorldObject> { b }, b);
            menu.Add(new MenuItem { Label = "Cancel" });
            BeginMenu($"{b.DisplayName}  {b.Health}/{b.MaxHealth} HP", screen);
        }

        BuildingWindows Windows
        {
            get
            {
                var w = BuildingWindows.Instance;
                if (w == null) w = gameObject.AddComponent<BuildingWindows>();
                return w;
            }
        }

        // ------------------------------------------------------------------ unit menu

        /// <summary>Right click on a character: tower and loot actions. False if there is nothing to offer.</summary>
        public bool OpenUnitMenu(Unit unit, Vector2 screen)
        {
            if (unit == null) return false;
            menu.Clear();
            menuUnits.Clear();
            if (unit.IsPerched && unit.Host is Building tower)
            {
                menu.Add(new MenuItem { Label = "Come down from tower", Run = () => tower.ComeDown() });
                menu.Add(new MenuItem { Label = "Face left", Run = () => unit.Face(Vector2.left) });
                menu.Add(new MenuItem { Label = "Face right", Run = () => unit.Face(Vector2.right) });
            }
            if (unit.Carrier != null && unit.Carrier.IsHolding)
                menu.Add(new MenuItem { Label = $"Drop loot ({unit.Carrier.Held.DisplayName})", Run = () => unit.Carrier.DropHere() });
            if (menu.Count == 0) return false;
            menu.Add(new MenuItem { Label = "Cancel" });
            BeginMenu(unit.Villager != null ? unit.Villager.DisplayName : unit.name, screen);
            return true;
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

        void AssignAttack(List<Unit> units, List<ResourceNode> targets)
        {
            units.RemoveAll(u => u == null);
            targets.RemoveAll(t => t == null || !t.CanBeHit);
            if (units.Count == 0 || targets.Count == 0) return;
            foreach (var u in units) u.Worker.Assign(new AttackJob(new List<ResourceNode>(targets)));
            if (logActions) Debug.Log($"[Orders] {units.Count} soldier(s): attack {targets.Count} object(s)");
        }

        void AssignBuild(List<Unit> units, Building b, string verb)
        {
            units.RemoveAll(u => u == null);
            foreach (var u in units) u.Worker.Assign(new BuildJob(b));
            if (logActions) Debug.Log($"[Orders] {units.Count} villager(s): {verb} {b.DisplayName}");
        }

        void AssignSupply(List<Unit> units, Building b)
        {
            foreach (var u in units) if (u != null) u.Worker.Assign(new SupplyJob(b));
            if (logActions) Debug.Log($"[Orders] {units.Count} villager(s): supply {b.Definition?.DisplayName} ({b.MaterialsText()})");
        }

        void AssignEnter(List<Unit> units, Building b)
        {
            foreach (var u in units) if (u != null) u.Worker.Assign(new EnterBuildingJob(b, false));
        }

        void AssignStock(List<Unit> units, Building b)
        {
            foreach (var u in units) if (u != null) u.Worker.Assign(new StockLootJob(b));
        }

        void AssignWithdraw(List<Unit> units, Building b, SpawnableDefinition item)
        {
            units.RemoveAll(u => u == null);
            if (units.Count == 0) return;
            PickWorker(units, b.GroundPoint).Worker.Assign(new WithdrawJob(b, item));
        }

        /// <summary>Every pile goes to the nearest free character (one pile per character).</summary>
        void AssignCarryLoot(List<Unit> units, List<WorldObject> piles)
        {
            units.RemoveAll(u => u == null);
            piles.RemoveAll(p => p == null || p.IsCarried);
            var free = new List<Unit>(units);
            foreach (var pile in piles)
            {
                if (free.Count == 0) break;
                Unit best = null;
                float bestDistance = float.MaxValue;
                foreach (var u in free)
                {
                    float d = Vector2.Distance(u.transform.position, pile.GroundPoint);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = u;
                    }
                }
                free.Remove(best);
                best.Worker.Assign(new CarryLootJob(pile));
            }
        }

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
