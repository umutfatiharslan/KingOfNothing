using System;
using System.Collections.Generic;
using System.Linq;
using KingOfNothing.Characters;
using KingOfNothing.Core;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// A tab of the character biography window. Add a new tab by writing a class that implements this interface
    /// (with a parameterless constructor): it is found automatically and shown in the order of Order.
    /// </summary>
    public interface IBioTab
    {
        string Title { get; }
        /// <summary>Position among the tabs (smaller = further left).</summary>
        int Order { get; }
        void Draw(Villager villager, BioContext context);
    }

    /// <summary>Shared helpers for bio tabs.</summary>
    public sealed class BioContext
    {
        public Vector2 Scroll;
        public string Tooltip;
    }

    /// <summary>Finds every IBioTab of the project (sorted by Order).</summary>
    public static class BioTabs
    {
        static List<IBioTab> tabs;

        public static IReadOnlyList<IBioTab> All
        {
            get
            {
                if (tabs != null) return tabs;
                tabs = new List<IBioTab>();
                var type = typeof(IBioTab);
                foreach (var t in type.Assembly.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface || !type.IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                    try { tabs.Add((IBioTab)Activator.CreateInstance(t)); }
                    catch (Exception e) { Debug.LogWarning($"[Bio] Could not create the tab {t.Name}: {e.Message}"); }
                }
                tabs.Sort((a, b) => a.Order.CompareTo(b.Order));
                return tabs;
            }
        }
    }

    /// <summary>
    /// The character windows: the character list (Characters button), a character's biography (Bio: tabs Stats,
    /// Journal and any other IBioTab), the day plan of everybody (Schedule: 24 hours painted work / recreation /
    /// sleep), the work priorities (Work: 1-5 per work type) and the training window of a school / army house /
    /// church. Opened from the bottom bar, the character list, a character's right click menu and building windows.
    /// </summary>
    public class CharacterWindows : MonoBehaviour
    {
        const int ListId = 0x43484C;
        const int BioId = 0x42494F;
        const int ScheduleId = 0x534348;
        const int WorkId = 0x574F52;
        const int TrainingId = 0x545241;

        public static CharacterWindows Instance { get; private set; }

        bool listOpen, scheduleOpen, workOpen;
        Villager bio;
        int bioTab;
        readonly BioContext bioContext = new BioContext();
        Villager trainee;
        Building trainingAt;
        Rect listRect = new Rect(10f, 120f, 360f, 10f);
        Rect bioRect = new Rect(380f, 80f, 560f, 10f);
        Rect scheduleRect = new Rect(60f, 140f, 720f, 10f);
        Rect workRect = new Rect(80f, 160f, 700f, 10f);
        Rect trainingRect = new Rect(420f, 120f, 560f, 10f);
        Vector2 listScroll, scheduleScroll, workScroll, trainingScroll;
        ScheduleBlock brush = ScheduleBlock.Work;
        int[] copiedSchedule;
        Villager lastClicked;
        float lastClickTime;
        string trainingMessage;
        GUIStyle small, rich, center;

        static readonly Color WorkColor = new Color(0.95f, 0.6f, 0.2f);
        static readonly Color RecreationColor = new Color(0.65f, 0.4f, 0.9f);
        static readonly Color SleepColor = new Color(0.3f, 0.5f, 0.95f);

        public static CharacterWindows Ensure()
        {
            if (Instance != null) return Instance;
            var host = FindAnyObjectByType<SelectionManager>();
            Instance = host != null ? host.gameObject.AddComponent<CharacterWindows>() : new GameObject("Character Windows").AddComponent<CharacterWindows>();
            return Instance;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool ListOpen => listOpen;
        public bool ScheduleOpen => scheduleOpen;
        public bool WorkOpen => workOpen;

        public void ToggleList() => listOpen = !listOpen;
        public void ToggleSchedule() => scheduleOpen = !scheduleOpen;
        public void ToggleWork() => workOpen = !workOpen;

        public void ShowBio(Villager villager)
        {
            bio = villager;
            bioRect.height = 10f;
        }

        public void ShowTraining(Villager villager, Building building)
        {
            trainee = villager;
            trainingAt = building;
            trainingMessage = null;
            trainingRect.height = 10f;
        }

        void Styles()
        {
            if (small != null) return;
            small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        }

        void OnGUI()
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return;
            Styles();
            if (listOpen)
            {
                listRect = GUILayout.Window(ListId, listRect, DrawList, $"Characters ({Villager.All.Count})", UiStyle.Window, GUILayout.Width(360f));
                ScreenUi.Block(listRect);
            }
            if (scheduleOpen)
            {
                scheduleRect = GUILayout.Window(ScheduleId, scheduleRect, DrawSchedule, "Schedule", UiStyle.Window, GUILayout.Width(720f));
                ScreenUi.Block(scheduleRect);
            }
            if (workOpen)
            {
                workRect = GUILayout.Window(WorkId, workRect, DrawWork, "Work priorities", UiStyle.Window, GUILayout.Width(700f));
                ScreenUi.Block(workRect);
            }
            if (bio != null)
            {
                bioRect = GUILayout.Window(BioId, bioRect, DrawBio, $"Bio - {bio.DisplayName}", UiStyle.Window, GUILayout.Width(560f));
                ScreenUi.Block(bioRect);
            }
            else bio = null;
            if (trainee != null && trainingAt != null)
            {
                trainingRect = GUILayout.Window(TrainingId, trainingRect, DrawTraining, $"Training - {trainee.DisplayName} in {trainingAt.Definition?.DisplayName}",
                    UiStyle.Window, GUILayout.Width(560f));
                ScreenUi.Block(trainingRect);
            }
            else
            {
                trainee = null;
                trainingAt = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        public static void DrawAvatar(Rect r, Villager v)
        {
            var sprite = v != null ? v.Avatar : null;
            if (sprite != null) BuildMenuController.DrawSprite(r, sprite);
            else GUI.Box(r, GUIContent.none);
        }

        static void Bar(Rect r, float share, Color color, string text)
        {
            GUI.Box(r, GUIContent.none);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, Mathf.Max(0f, (r.width - 2f) * Mathf.Clamp01(share)), r.height - 2f), Texture2D.whiteTexture);
            GUI.color = old;
            if (!string.IsNullOrEmpty(text))
            {
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
                GUI.Label(r, text, style);
            }
        }

        static void BarLayout(float share, Color color, string text, float width = -1f, float height = 16f)
        {
            var r = width > 0f ? GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height))
                               : GUILayoutUtility.GetRect(100f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            Bar(r, share, color, text);
        }

        static Color HealthColor(float share) => share > 0.6f ? new Color(0.35f, 0.8f, 0.35f) : share >= 0.25f ? new Color(0.9f, 0.8f, 0.25f) : new Color(0.9f, 0.3f, 0.25f);

        static void Focus(Villager v)
        {
            if (v == null) return;
            var cam = FindAnyObjectByType<KingOfNothing.CameraControl.RTSCameraController>();
            if (cam != null) cam.FocusOn(v.transform.position, false);
            var selection = SelectionManager.Instance;
            if (selection != null && v.Movement != null && !v.Movement.IsHidden) selection.SelectOnly(v.Movement);
        }

        // ------------------------------------------------------------------ character list

        void DrawList(int id)
        {
            GUILayout.Label("Click: select, double click: bio.", small);
            var all = new List<Villager>(Villager.All);
            all.RemoveAll(v => v == null);
            all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(Mathf.Clamp(all.Count * 52f + 8f, 60f, 470f)));
            foreach (var v in all)
            {
                var row = GUILayoutUtility.GetRect(320f, 48f, GUILayout.ExpandWidth(true), GUILayout.Height(48f));
                bool clicked = GUI.Button(row, GUIContent.none);
                DrawAvatar(new Rect(row.x + 4f, row.y + 4f, 40f, 40f), v);
                GUI.Label(new Rect(row.x + 50f, row.y + 2f, row.width - 54f, 18f), $"<b>{v.DisplayName}</b>  {v.Role}", rich);
                var brain = v.Brain;
                GUI.Label(new Rect(row.x + 50f, row.y + 18f, row.width - 130f, 18f), brain != null ? brain.Doing : "", small);
                var h = v.Needs.Health;
                Bar(new Rect(row.xMax - 76f, row.y + 22f, 70f, 12f), h.Normalized, HealthColor(h.Normalized), $"{h.current:0}/{h.max:0}");
                if (!clicked) continue;
                if (lastClicked == v && Time.unscaledTime - lastClickTime < 0.35f) ShowBio(v);
                else Focus(v);
                lastClicked = v;
                lastClickTime = Time.unscaledTime;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) listOpen = false;
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ bio

        void DrawBio(int id)
        {
            var v = bio;
            if (v == null) return;
            using (new GUILayout.HorizontalScope())
            {
                var r = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f), GUILayout.Height(72f));
                DrawAvatar(r, v);
                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Label($"<b>{v.DisplayName}</b>, {v.Identity.age} years, {v.Role}", rich);
                    var map = MapData.Instance;
                    var home = v.Memory.homeUid > 0 && map != null ? map.FindObject(v.Memory.homeUid) as Building : null;
                    GUILayout.Label($"Home: {(home != null ? home.DisplayName : "none")}    Now: {(v.Brain != null ? v.Brain.Doing : "")}", small);
                    var t = v.Training;
                    if (t.active)
                    {
                        var p = v.Stats.Progress(t.stat);
                        GUILayout.Label($"Training {StatInfo.Name(t.stat)}: {p.trainingHours:0.#} / {StatSettings.Current.TrainingHoursFor(p.level):0.#} h", small);
                    }
                    var traits = string.Join(", ", v.Traits.Where(x => x != null && x.revealed && x.definition != null).Select(x => x.definition.DisplayName));
                    if (!string.IsNullOrEmpty(traits)) GUILayout.Label("Traits: " + traits, small);
                }
            }
            var tabs = BioTabs.All;
            if (tabs.Count > 0)
            {
                var titles = tabs.Select(t => t.Title).ToArray();
                int selected = GUILayout.Toolbar(Mathf.Clamp(bioTab, 0, tabs.Count - 1), titles);
                if (selected != bioTab)
                {
                    bioTab = selected;
                    bioContext.Scroll = Vector2.zero;
                }
                bioContext.Tooltip = null;
                tabs[Mathf.Clamp(bioTab, 0, tabs.Count - 1)].Draw(v, bioContext);
            }
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUILayout.Label(GUI.tooltip, small);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show on the map")) Focus(v);
                if (v.Memory.manualHold && GUILayout.Button("Resume routine")) v.Brain?.ResumeRoutine();
                if (GUILayout.Button("Close")) bio = null;
            }
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ schedule

        void DrawSchedule(int id)
        {
            GUILayout.Label("Paint the hours: click or drag over the squares. Characters follow their plan (player orders come first).", small);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Brush:", GUILayout.Width(44f));
                BrushButton(ScheduleBlock.Work, "Work", WorkColor);
                BrushButton(ScheduleBlock.Recreation, "Recreation", RecreationColor);
                BrushButton(ScheduleBlock.Sleep, "Sleep", SleepColor);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Now: {GameClock.CurrentHour:00}:00", small, GUILayout.Width(80f));
            }
            const float nameWidth = 110f, cell = 20f, buttons = 110f;
            // Hour header
            var header = GUILayoutUtility.GetRect(nameWidth + 24 * cell + buttons, 16f);
            for (int h = 0; h < 24; h++)
                GUI.Label(new Rect(header.x + nameWidth + h * cell, header.y, cell, 16f), h.ToString(), center);
            var all = new List<Villager>(Villager.All);
            all.RemoveAll(v => v == null);
            all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
            scheduleScroll = GUILayout.BeginScrollView(scheduleScroll, GUILayout.Height(Mathf.Clamp(all.Count * 24f + 8f, 50f, 420f)));
            var e = Event.current;
            int now = GameClock.CurrentHour;
            foreach (var v in all)
            {
                var row = GUILayoutUtility.GetRect(nameWidth + 24 * cell + buttons, 22f);
                GUI.Label(new Rect(row.x, row.y + 2f, nameWidth - 4f, 18f), v.DisplayName, small);
                for (int h = 0; h < 24; h++)
                {
                    var r = new Rect(row.x + nameWidth + h * cell, row.y + 1f, cell - 2f, 20f);
                    var block = v.BlockAt(h);
                    var old = GUI.color;
                    GUI.color = block == ScheduleBlock.Work ? WorkColor : block == ScheduleBlock.Recreation ? RecreationColor : SleepColor;
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = old;
                    if (h == now) GUI.Box(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), GUIContent.none);
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition))
                    {
                        v.SetBlock(h, brush);
                        e.Use();
                    }
                }
                float bx = row.x + nameWidth + 24 * cell + 4f;
                if (GUI.Button(new Rect(bx, row.y + 1f, 50f, 20f), "Copy")) copiedSchedule = v.ScheduleCopy();
                GUI.enabled = copiedSchedule != null;
                if (GUI.Button(new Rect(bx + 54f, row.y + 1f, 50f, 20f), "Paste")) v.SetSchedule(copiedSchedule);
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Everybody: default plan"))
                {
                    var c = CharacterSettings.Current;
                    foreach (var v in all)
                        for (int h = 0; h < 24; h++) v.SetBlock(h, c.DefaultBlock(h));
                }
                if (GUILayout.Button("Close")) scheduleOpen = false;
            }
            GUI.DragWindow();
        }

        void BrushButton(ScheduleBlock block, string label, Color color)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            if (GUILayout.Toggle(brush == block, label, GUI.skin.button, GUILayout.Width(100f))) brush = block;
            GUI.backgroundColor = old;
        }

        // ------------------------------------------------------------------ work

        static string Short(StatType s)
        {
            switch (s)
            {
                case StatType.Building: return "Build";
                case StatType.Woodcutting: return "Wood";
                case StatType.Mining: return "Mine";
                case StatType.Farming: return "Farm";
                case StatType.Hunting: return "Hunt";
                case StatType.Fishing: return "Fish";
                case StatType.Baking: return "Bake";
                case StatType.Efficiency: return "Haul";
                case StatType.Healing: return "Heal";
                default: return s.ToString();
            }
        }

        void DrawWork(int id)
        {
            GUILayout.Label("1 = first ... 5 = last, empty = never. Left click: next, right click: previous. '-' = cannot (stat 0 or class).", small);
            const float nameWidth = 120f, col = 58f;
            var types = WorkTypes.All;
            var header = GUILayoutUtility.GetRect(nameWidth + types.Length * col, 18f);
            for (int i = 0; i < types.Length; i++)
                GUI.Label(new Rect(header.x + nameWidth + i * col, header.y, col, 18f), new GUIContent(Short(types[i]), WorkTypes.Describe(types[i])), center);
            var all = new List<Villager>(Villager.All);
            all.RemoveAll(v => v == null);
            all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
            workScroll = GUILayout.BeginScrollView(workScroll, GUILayout.Height(Mathf.Clamp(all.Count * 26f + 8f, 50f, 420f)));
            var e = Event.current;
            foreach (var v in all)
            {
                var row = GUILayoutUtility.GetRect(nameWidth + types.Length * col, 24f);
                GUI.Label(new Rect(row.x, row.y + 3f, nameWidth - 4f, 18f), $"{v.DisplayName}", small);
                if (VillagerRoles.IsSoldier(v.Role))
                {
                    GUI.Label(new Rect(row.x + nameWidth, row.y + 3f, types.Length * col, 18f), "Soldier: trains on scarecrows in work hours, patrols in recreation hours.", small);
                    continue;
                }
                for (int i = 0; i < types.Length; i++)
                {
                    var stat = types[i];
                    var r = new Rect(row.x + nameWidth + i * col + 2f, row.y + 1f, col - 4f, 22f);
                    if (!WorkTypes.CanDo(v, stat))
                    {
                        GUI.Label(r, new GUIContent("-", stat == StatType.Fishing ? "Fishing comes later" : $"{StatInfo.Name(stat)} {v.Stats.Base(stat)}"), center);
                        continue;
                    }
                    int p = v.PriorityOf(stat);
                    var content = new GUIContent(p > 0 ? p.ToString() : "", $"{StatInfo.Name(stat)} level {v.Stats.Base(stat)} - {WorkTypes.Describe(stat)}");
                    if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
                    {
                        if (e.button == 0) v.SetPriority(stat, p >= 5 ? 0 : p + 1);
                        else if (e.button == 1) v.SetPriority(stat, p <= 0 ? 5 : p - 1);
                        e.Use();
                    }
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = p == 0 ? Color.gray : Color.Lerp(new Color(0.4f, 1f, 0.4f), new Color(1f, 0.6f, 0.3f), (p - 1) / 4f);
                    GUI.Box(r, content);
                    GUI.backgroundColor = old;
                }
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUILayout.Label(GUI.tooltip, small);
            if (GUILayout.Button("Close")) workOpen = false;
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ training

        void DrawTraining(int id)
        {
            var v = trainee;
            var b = trainingAt;
            if (v == null || b == null) return;
            var s = StatSettings.Current;
            var coin = MapData.Instance != null ? MapData.Instance.Gameplay.goldCoin : null;
            GUILayout.Label("A training does not give experience: when it is done, the experience of that level is multiplied " +
                            "(level 1: x1.1, level 2: x1.2 ...). The cost is taken from this building's storage (Stock loot or ask the haulers).", small);
            if (b.Type == BuildingType.ArmyHouse)
                GUILayout.Label($"Army training hurts: every game hour {Mathf.RoundToInt(s.armyHealthLossChance * 100f)}% chance to lose (target level) health; " +
                                $"below {Mathf.RoundToInt(s.trainingMinHealth * 100f)}% the character goes to the church.", small);
            var h = v.Needs.Health;
            BarLayout(h.Normalized, HealthColor(h.Normalized), $"Health {h.current:0}/{h.max:0}");
            trainingScroll = GUILayout.BeginScrollView(trainingScroll, GUILayout.Height(380f));
            foreach (var stat in TrainingService.StatsOf(b.Type))
            {
                var p = v.Stats.Progress(stat);
                int level = p.level;
                GUILayout.BeginVertical(GUI.skin.box);
                bool trained = v.Stats.IsTrained(stat);
                string status = level <= 0 ? "can never learn it (0)" : level >= StatBlock.MaxLevel ? "maximum level"
                    : trained ? $"trained: XP x{s.TrainingMultiplier(level):0.0#} until level {level + 1}" : $"not trained (training: XP x{s.TrainingMultiplier(level):0.0#})";
                GUILayout.Label($"<b>{StatInfo.Name(stat)}</b> level {level} - {status}", rich);
                if (level > 0 && level < StatBlock.MaxLevel && !trained)
                {
                    bool paid = TrainingService.Paid(v, stat);
                    var cost = TrainingService.Cost(stat, level);
                    var parts = new List<string>();
                    foreach (var c in cost) parts.Add($"{c.item.DisplayName} {b.Inventory.Count(c.item)}/{c.amount}");
                    GUILayout.Label(paid ? "Cost: paid (interrupted training)" : "Cost: " + (parts.Count > 0 ? string.Join(", ", parts) : "free") +
                                    (GameModeManager.IsDeveloperMode ? "  (developer mode: free)" : ""), small);
                    float hours = s.TrainingHoursFor(level);
                    float done = p.trainingLevel == level ? p.trainingHours : 0f;
                    BarLayout(done / hours, new Color(0.45f, 0.7f, 1f), $"{done:0.#} / {hours:0.#} game hours");
                    bool running = v.Training.active && v.Training.stat == stat;
                    using (new GUILayout.HorizontalScope())
                    {
                        if (running)
                        {
                            if (GUILayout.Button("Stop (progress is kept)")) TrainingService.Stop(v);
                        }
                        else
                        {
                            bool can = TrainingService.CanStart(v, b, stat, out string reason);
                            GUI.enabled = can;
                            if (GUILayout.Button(done > 0f ? "Resume" : "Start")) trainingMessage = TrainingService.Start(v, b, stat) ? null : reason;
                            GUI.enabled = true;
                            if (!can) GUILayout.Label(reason, small);
                        }
                        if (!paid && GUILayout.Button("Request materials", GUILayout.Width(140f)))
                        {
                            TrainingService.RequestMaterials(v, b, stat);
                            trainingMessage = "Haulers (Efficiency work) will bring the missing materials from the stores.";
                        }
                        if (GameModeManager.IsDeveloperMode && running && GUILayout.Button("Finish (Dev)", GUILayout.Width(90f))) TrainingService.FinishNow(v, stat);
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(trainingMessage)) GUILayout.Label(trainingMessage, small);
            if (GUILayout.Button("Close"))
            {
                trainee = null;
                trainingAt = null;
            }
            GUI.DragWindow();
        }
    }

    // ================================================================== default bio tabs

    /// <summary>Bio tab: needs, every stat with its level, experience, training and milestones.</summary>
    public sealed class StatsBioTab : IBioTab
    {
        public string Title => "Stats";
        public int Order => 0;

        static GUIStyle small, rich;

        public void Draw(Villager v, BioContext context)
        {
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
                rich = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
            }
            var needs = v.Needs;
            using (new GUILayout.HorizontalScope())
            {
                NeedBar("Health", needs.Health.current, needs.Health.max, new Color(0.35f, 0.8f, 0.35f));
                NeedBar("Satiety", needs.Satiety.current, needs.Satiety.max, new Color(0.9f, 0.7f, 0.3f));
                NeedBar("Morale", needs.Morale.current, needs.Morale.max, new Color(0.6f, 0.5f, 0.95f));
            }
            var s = StatSettings.Current;
            bool developer = GameModeManager.IsDeveloperMode;
            context.Scroll = GUILayout.BeginScrollView(context.Scroll, GUILayout.Height(380f));
            foreach (StatGroup group in Enum.GetValues(typeof(StatGroup)))
            {
                GUILayout.Label($"<b>{StatInfo.GroupName(group)}</b>", rich);
                foreach (var stat in StatInfo.All)
                {
                    if (StatInfo.GroupOf(stat) != group) continue;
                    var stats = v.Stats;
                    int level = stats.Base(stat);
                    int bonus = stats.Bonus(stat);
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(StatInfo.Name(stat), small, GUILayout.Width(105f));
                        string levelText = level <= 0 && stat != StatType.Healing ? "never" : level.ToString() + (bonus > 0 && level > 0 ? $" (+{bonus})" : "");
                        GUILayout.Label(levelText, small, GUILayout.Width(52f));
                        if (level > 0 && level < StatBlock.MaxLevel)
                        {
                            float need = s.XpToNext(level);
                            float xp = stats.Xp(stat);
                            string extra = stats.IsTrained(stat) ? $"  x{s.TrainingMultiplier(level):0.0#}" : "";
                            var r = GUILayoutUtility.GetRect(150f, 14f, GUILayout.Width(150f), GUILayout.Height(14f));
                            GUI.Box(r, GUIContent.none);
                            var old = GUI.color;
                            GUI.color = stats.IsTrained(stat) ? new Color(1f, 0.85f, 0.3f) : new Color(0.45f, 0.75f, 1f);
                            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(xp / need), r.height - 2f), Texture2D.whiteTexture);
                            GUI.color = old;
                            GUI.Label(r, $"{xp:0}/{need:0} XP{extra}", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
                        }
                        else GUILayout.Label(level >= StatBlock.MaxLevel ? "MAX" : "", small, GUILayout.Width(150f));
                        // Milestones 5 / 10 / 15 / 20
                        foreach (var m in Milestones.Of(stat))
                        {
                            bool unlocked = level >= m.Level;
                            var old = GUI.backgroundColor;
                            GUI.backgroundColor = !m.Available ? new Color(0.3f, 0.3f, 0.3f) : unlocked ? new Color(1f, 0.8f, 0.2f) : new Color(0.5f, 0.5f, 0.55f);
                            GUILayout.Box(new GUIContent(m.Level.ToString(), $"{m.Name} ({StatInfo.Name(stat)} {m.Level}){(unlocked ? " - unlocked" : "")}: {m.Description}"),
                                GUILayout.Width(26f), GUILayout.Height(16f));
                            GUI.backgroundColor = old;
                        }
                        if (developer)
                        {
                            if (GUILayout.Button("-", GUILayout.Width(20f))) stats.Set(stat, level - 1);
                            if (GUILayout.Button("+", GUILayout.Width(20f))) stats.Set(stat, level + 1);
                            if (level > 0 && GUILayout.Button("XP", GUILayout.Width(28f))) v.GainXp(stat, s.XpToNext(level) * 0.25f);
                        }
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Hover a milestone square for its ability. Gold = unlocked, dark = needs a later system.", small);
        }

        static void NeedBar(string label, float value, float max, Color color)
        {
            var r = GUILayoutUtility.GetRect(160f, 16f, GUILayout.Width(170f), GUILayout.Height(16f));
            GUI.Box(r, GUIContent.none);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(max > 0f ? value / max : 0f), r.height - 2f), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, $"{label} {value:0}/{max:0}", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
        }
    }

    /// <summary>Bio tab: the character's journal (newest first).</summary>
    public sealed class JournalBioTab : IBioTab
    {
        public string Title => "Journal";
        public int Order => 10;

        static GUIStyle style;

        public void Draw(Villager v, BioContext context)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11, richText = true };
            context.Scroll = GUILayout.BeginScrollView(context.Scroll, GUILayout.Height(420f));
            var journal = v.Journal;
            if (journal.Count == 0) GUILayout.Label("Nothing written yet.", style);
            for (int i = journal.Count - 1; i >= 0; i--)
            {
                var e = journal[i];
                GUILayout.Label($"<b>Day {e.day}, {e.hour:00}:00</b>  {e.text}", style);
            }
            GUILayout.EndScrollView();
        }
    }
}
