using System;
using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.Units;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>
    /// A building on the map (castle, school, army house, church, houses, tower, bridges, furnace, gold mine). It
    /// is a map object with health like a tree or a stone, so it is saved in the edited map, can be selected,
    /// damaged and undone like every other object. It goes through three phases, each with its own health bar:
    ///  - construction: placed from the build menu as a see-through template (1 health) until villagers are
    ///    ordered to build it; every hammer swing adds health; the construction looks are shown until the last
    ///    construction stage is passed, then the finished sprite; complete (usable) at full health;
    ///  - complete: the full health; soldiers can damage it and it loses health every game day;
    ///  - destroyed: when the full health is gone it collapses into its destroyed look with the destroyed health
    ///    (full again). Characters inside come out injured. It can still be repaired: when the destroyed health is
    ///    full the building comes back with 1 health. At 0 destroyed health it is gone (castle: game over).
    /// Also: characters inside (capacity), the character on top of a tower, the inventory (kept out of undo),
    /// the furnace, gold mining and bridges that join end to end. All numbers come from its BuildingDefinition
    /// asset and the GameplaySettings asset.
    /// </summary>
    public class Building : ResourceNode, IUnitHost
    {
        public static readonly List<Building> All = new List<Building>();

        /// <summary>A built building is gone for good (its destroyed health reached 0), not removed by the developer.</summary>
        public static event Action<Building> AnyFell;
        /// <summary>A building lost its full health and is now destroyed.</summary>
        public static event Action<Building> AnyCollapsed;
        /// <summary>Occupants, inventory, phase or look changed (refresh windows).</summary>
        public static event Action<Building> AnyChanged;

        /// <summary>Changes whenever a bridge appears, disappears or becomes walkable (bridges redraw their joints).</summary>
        static int bridgeVersion;

        [SerializeField] BuildingPhase phase = BuildingPhase.Complete;

        readonly BuildingInventory inventory = new BuildingInventory();
        readonly List<Unit> occupants = new List<Unit>();
        readonly List<SpriteRenderer> visuals = new List<SpriteRenderer>();
        /// <summary>Bridges: the pieces laid over a crossing bridge's edge at the start / end (no gap at the joint).</summary>
        readonly SpriteRenderer[] joints = new SpriteRenderer[2];
        Unit percher;
        float perchSince;
        int archeryApplied;
        bool towerBonus;
        bool collapsing;
        bool needsMaterials;
        readonly List<InventorySlot> supplied = new List<InventorySlot>();
        int mineReserve = -1;
        float mineTimer;
        float furnaceProgress;
        bool furnaceWorking;
        SmokeEmitter smoke;
        bool lastWalkable;
        int lastHealth = int.MinValue;
        BuildingPhase lastPhase;
        bool lastActive;
        bool lastTemplate;
        int lastBridgeVersion = -1;
        Sprite shownSprite;

        public BuildingDefinition Info => Definition as BuildingDefinition;
        public BuildingType Type => Info != null ? Info.buildingType : BuildingType.House;
        public BuildingPhase Phase => phase;

        /// <summary>Was finished once (complete or destroyed): it is repaired, not built.</summary>
        public bool IsComplete => phase != BuildingPhase.Construction;
        /// <summary>A placed building nobody has worked on yet (see-through).</summary>
        public bool IsTemplate => phase == BuildingPhase.Construction && Health <= 1;
        /// <summary>Its full health is gone: the destroyed look, unusable until repaired.</summary>
        public bool LooksDestroyed => phase == BuildingPhase.Destroyed;
        /// <summary>Complete and not destroyed: characters can enter, items can be stored.</summary>
        public bool IsUsable => phase == BuildingPhase.Complete && !IsDepleted;
        /// <summary>Needs villagers: still being built, damaged, or destroyed (repairable until it is gone).</summary>
        public bool NeedsWork => !IsDepleted && (phase == BuildingPhase.Destroyed || Health < MaxHealth);
        public BuildingInventory Inventory => inventory;
        public IReadOnlyList<Unit> Occupants => occupants;
        public Unit Percher => percher != null ? percher : null;
        public bool FurnaceWorking => furnaceWorking;
        public float FurnaceProgress => Info != null && Info.furnaceSeconds > 0f ? furnaceProgress / Info.furnaceSeconds : 0f;
        public string HostName => DisplayName;
        public Vector2 DoorPoint => GroundPoint + (Info != null ? Info.doorOffset : new Vector2(0f, -0.35f));
        /// <summary>Gold left in a mine (-1 = unlimited).</summary>
        public int MineReserve => Info == null || Info.mineReserve <= 0 ? -1 : mineReserve < 0 ? Info.mineReserve : mineReserve;
        public bool MineExhausted => Info != null && Info.IsMine && MineReserve == 0;
        /// <summary>
        /// The template waits for its materials (the building cost): villagers carry them here, and construction can
        /// only start once everything has arrived. Developer mode templates need nothing.
        /// </summary>
        public bool RequiresMaterials => phase == BuildingPhase.Construction && needsMaterials && Info != null && Info.buildCost != null && Info.buildCost.Count > 0;
        /// <summary>All materials have arrived (always true when none are needed).</summary>
        public bool MaterialsComplete
        {
            get
            {
                if (!RequiresMaterials) return true;
                foreach (var c in Info.buildCost)
                    if (c != null && c.item != null && Supplied(c.item) < c.amount) return false;
                return true;
            }
        }
        /// <summary>Materials already delivered (any delivered at all).</summary>
        public bool AnySupplied => supplied.Exists(x => x.count > 0);

        /// <summary>How many of the item have been delivered to the template.</summary>
        public int Supplied(SpawnableDefinition item)
        {
            foreach (var x in supplied) if (x.item == item) return x.count;
            return 0;
        }

        /// <summary>How many of the item are still missing (0 if it is not needed).</summary>
        public int Missing(SpawnableDefinition item)
        {
            if (!RequiresMaterials || item == null) return 0;
            int needed = 0;
            foreach (var c in Info.buildCost) if (c != null && c.item == item) needed += c.amount;
            return Mathf.Max(0, needed - Supplied(item));
        }

        /// <summary>Materials still missing (item, amount).</summary>
        public List<DropEntry> MissingMaterials()
        {
            var list = new List<DropEntry>();
            if (!RequiresMaterials) return list;
            foreach (var c in Info.buildCost)
            {
                if (c == null || c.item == null) continue;
                int missing = Missing(c.item);
                if (missing > 0 && !list.Exists(d => d.item == c.item)) list.Add(new DropEntry(c.item, missing));
            }
            return list;
        }

        /// <summary>"Wood 10/30, Rock 0/10" (what was delivered so far).</summary>
        public string MaterialsText()
        {
            if (!RequiresMaterials) return "";
            var parts = new List<string>();
            foreach (var c in Info.buildCost)
                if (c != null && c.item != null && c.amount > 0) parts.Add($"{c.item.DisplayName} {Mathf.Min(Supplied(c.item), c.amount)}/{c.amount}");
            return string.Join(", ", parts);
        }

        /// <summary>Number of characters inside (or on top).</summary>
        public int OccupantCount => occupants.Count + (percher != null ? 1 : 0);
        /// <summary>-1 = unlimited.</summary>
        public int Capacity => Info == null ? 0 : Type == BuildingType.Tower ? 1 : Info.capacity;

        /// <summary>Area villagers work on (the whole footprint for bridges, otherwise the hitbox).</summary>
        public Rect WorkBox => Info != null && Info.isBridge ? new Rect(Cell.x, Cell.y, Footprint.x, Footprint.y) : Hitbox;

        static GameplaySettings Gameplay => MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;

        public override string DisplayName
        {
            get
            {
                string name = Definition != null ? Definition.DisplayName : this.name;
                if (IsTemplate) return name + " (template)";
                if (phase == BuildingPhase.Construction) return name + " (construction)";
                if (phase == BuildingPhase.Destroyed) return name + " (destroyed)";
                if (MineExhausted) return name + " (exhausted)";
                return name;
            }
        }

        public override float BaseAlpha => IsTemplate ? Gameplay.templateAlpha : 1f;

        // ------------------------------------------------------------------ setup

        public override void Initialize(SpawnableDefinition source, Vector2Int originCell, Vector2 cellOffset, MapData map)
        {
            base.Initialize(source, originCell, cellOffset, map);
            var info = Info;
            phase = BuildingPhase.Complete;
            if (info != null)
            {
                inventory.Configure(info.inventorySlots, info.slotCapacity);
                // Ruins found on the map (gold mines) start destroyed
                if (info.spawnDestroyed && info.destroyedHealth > 0)
                {
                    phase = BuildingPhase.Destroyed;
                    ApplyPhaseMax(false);
                    SetHealth(info.destroyedHealth, false);
                }
            }
            inventory.Changed += OnInventoryChanged;
            CreateVisuals();
            if (info != null && info.IsFurnace)
            {
                smoke = gameObject.AddComponent<SmokeEmitter>();
                smoke.Setup(info);
            }
            if (!All.Contains(this)) All.Add(this);
            if (info != null && info.isBridge) bridgeVersion++;
            RefreshLook(true);
        }

        /// <summary>Turns a freshly created building into a template (call from the factory's configure step).</summary>
        public void BeginConstruction(int startHealth, bool materialsNeeded)
        {
            phase = BuildingPhase.Construction;
            needsMaterials = materialsNeeded;
            supplied.Clear();
            ApplyPhaseMax(false);
            SetHealth(Mathf.Clamp(startHealth, 1, MaxHealth), false);
            RefreshLook(true);
        }

        protected override void OnDestroy()
        {
            All.Remove(this);
            inventory.Changed -= OnInventoryChanged;
            if (Info != null && Info.isBridge) bridgeVersion++;
            ReleaseAll();
            base.OnDestroy();
        }

        /// <summary>Maximum health of the current phase (destroyed: the destroyed health; a manned tower is stronger).</summary>
        int PhaseMax
        {
            get
            {
                var info = Info;
                if (info == null || Definition == null) return MaxHealth;
                if (phase == BuildingPhase.Destroyed) return Mathf.Max(1, info.destroyedHealth);
                return Definition.MaxHealth + (towerBonus ? info.mannedHealthBonus : 0);
            }
        }

        void ApplyPhaseMax(bool keepMissing, bool notify = false) => SetMaxHealth(PhaseMax, keepMissing, notify);

        // ------------------------------------------------------------------ saved state

        /// <summary>Phase etc. (part of the undo history).</summary>
        [Serializable]
        class SavedState
        {
            public int phase = -1;
            public bool complete = true; // older files
            public int reserve = -1;
            public bool paid; // older files
            public bool needsMaterials;
            public List<string> supplyItems = new List<string>();
            public List<int> supplyCounts = new List<int>();
            public List<string> items = new List<string>(); // older files kept the inventory here
            public List<int> counts = new List<int>();
        }

        /// <summary>Stored items (kept out of undo).</summary>
        [Serializable]
        class KeptState
        {
            public List<string> items = new List<string>();
            public List<int> counts = new List<int>();
        }

        public override string SaveState()
        {
            bool defaultPhase = phase == (Info != null && Info.spawnDestroyed ? BuildingPhase.Destroyed : BuildingPhase.Complete);
            if (defaultPhase && mineReserve < 0 && !needsMaterials && supplied.Count == 0) return null;
            var state = new SavedState { phase = (int)phase, complete = phase != BuildingPhase.Construction, reserve = mineReserve, needsMaterials = needsMaterials };
            foreach (var x in supplied)
            {
                if (x.item == null || x.count <= 0) continue;
                state.supplyItems.Add(x.item.name);
                state.supplyCounts.Add(x.count);
            }
            return JsonUtility.ToJson(state);
        }

        public override void LoadState(string json)
        {
            SavedState state = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { state = JsonUtility.FromJson<SavedState>(json); }
                catch (Exception e) { Debug.LogWarning($"[Building] Could not read the state of {name}: {e.Message}", this); }
            }
            var info = Info;
            if (state == null) phase = info != null && info.spawnDestroyed && info.destroyedHealth > 0 ? BuildingPhase.Destroyed : BuildingPhase.Complete;
            else if (state.phase >= 0) phase = (BuildingPhase)state.phase;
            else phase = state.complete ? BuildingPhase.Complete : BuildingPhase.Construction;
            mineReserve = state != null ? state.reserve : -1;
            needsMaterials = state != null && state.needsMaterials;
            supplied.Clear();
            if (state != null && state.supplyItems != null)
                for (int i = 0; i < state.supplyItems.Count && i < state.supplyCounts.Count; i++)
                {
                    var item = BuildingInventory.Resolver != null ? BuildingInventory.Resolver(state.supplyItems[i]) : null;
                    if (item != null && state.supplyCounts[i] > 0) supplied.Add(new InventorySlot { item = item, count = state.supplyCounts[i] });
                }
            ApplyPhaseMax(false);
            // Files of the previous version kept the stored items in the history
            if (state != null && state.items != null && state.items.Count > 0 && inventory.UsedSlots == 0)
                inventory.Load(state.items, state.counts);
            RefreshLook(true);
            AnyChanged?.Invoke(this);
        }

        public override string SaveKeptState()
        {
            if (inventory.UsedSlots == 0) return null;
            var kept = new KeptState();
            inventory.Save(kept.items, kept.counts);
            return JsonUtility.ToJson(kept);
        }

        public override void LoadKeptState(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var kept = JsonUtility.FromJson<KeptState>(json);
                inventory.Load(kept.items, kept.counts);
            }
            catch (Exception e) { Debug.LogWarning($"[Building] Could not read the stored items of {name}: {e.Message}", this); }
        }

        void OnInventoryChanged()
        {
            RaiseKeptStateChanged();
            AnyChanged?.Invoke(this);
        }

        // ------------------------------------------------------------------ construction, repair, damage

        /// <summary>A villager's hammer swing: adds health (building or repairing). False if there is nothing to do.</summary>
        public bool Build(int amount)
        {
            if (IsDepleted || amount <= 0) return false;
            if (!MaterialsComplete) return false; // construction starts once every material has arrived
            if (phase == BuildingPhase.Destroyed)
            {
                SetHealth(Mathf.Min(MaxHealth, Health + amount));
                HitShake.Play(this);
                HealthBar.Notify(this);
                if (Health >= MaxHealth) Restore();
                RefreshLook(false);
                return true;
            }
            if (Health >= MaxHealth)
            {
                if (phase == BuildingPhase.Construction) MarkComplete();
                return false;
            }
            SetHealth(Mathf.Min(MaxHealth, Health + amount));
            HitShake.Play(this);
            HealthBar.Notify(this);
            if (Health >= MaxHealth && phase == BuildingPhase.Construction) MarkComplete();
            RefreshLook(false);
            return true;
        }

        void MarkComplete()
        {
            string before = SaveState();
            phase = BuildingPhase.Complete;
            needsMaterials = false;
            supplied.Clear();
            RaiseStateChanged(before);
            RefreshLook(true);
            Debug.Log($"[Buildings] {DisplayName} is complete.", this);
            AnyChanged?.Invoke(this);
        }

        /// <summary>The destroyed building is repaired: it comes back with 1 health and the repair goes on.</summary>
        void Restore()
        {
            string before = SaveState();
            phase = BuildingPhase.Complete;
            ApplyPhaseMax(false);
            RaiseStateChanged(before);
            SetHealth(1);
            RefreshLook(true);
            Debug.Log($"[Buildings] {Definition?.DisplayName} stands again (1 health), keep repairing it.", this);
            AnyChanged?.Invoke(this);
        }

        /// <summary>Its full health is gone: destroyed look with the destroyed health; characters inside come out injured.</summary>
        void Collapse()
        {
            collapsing = true;
            InjureOccupants();
            ReleaseAll();
            string before = SaveState();
            phase = BuildingPhase.Destroyed;
            ApplyPhaseMax(false);
            RaiseStateChanged(before);
            SetHealth(MaxHealth);
            collapsing = false;
            HealthBar.Notify(this);
            RefreshLook(true);
            Debug.Log($"[Buildings] {Definition?.DisplayName} collapsed (destroyed: {Health} health left, repairable).", this);
            AnyCollapsed?.Invoke(this);
            AnyChanged?.Invoke(this);
        }

        /// <summary>Characters inside a collapsing building drop to 1-25 health (always lower than before).</summary>
        void InjureOccupants()
        {
            var all = new List<Unit>(occupants);
            if (percher != null) all.Add(percher);
            foreach (var u in all)
            {
                var health = u != null && u.Villager != null ? u.Villager.Needs.Health : null;
                if (health == null) continue;
                // 1-25; a character who already has 25 or less always ends up lower than now (at least 1)
                int now = Mathf.FloorToInt(health.current);
                float wanted = now > 25 ? UnityEngine.Random.Range(1, 26) : now > 1 ? UnityEngine.Random.Range(1, now) : 1;
                health.Change(wanted - health.current);
                Debug.Log($"[Buildings] {u.Villager.DisplayName} was hurt in the collapse ({Mathf.RoundToInt(health.current)} health).", u);
            }
        }

        public override void ApplyHit(int power, GameObject instigator)
        {
            base.ApplyHit(power, instigator);
            if (this == null || IsDepleted) return;
            RefreshLook(false);
        }

        /// <summary>Loses health by itself (one game day passed).</summary>
        public void Decay(int amount)
        {
            if (IsDepleted || amount <= 0) return;
            int health = Health - amount;
            if (health <= 0)
            {
                SetHealth(0);
                Deplete(null);
                return;
            }
            SetHealth(health);
            RefreshLook(false);
        }

        protected override void OnDepleted(GameObject instigator)
        {
            var info = Info;
            if (phase == BuildingPhase.Complete && info != null && info.destroyedHealth > 0)
            {
                Collapse();
                return;
            }
            bool built = phase != BuildingPhase.Construction;
            InjureOccupants();
            ReleaseAll();
            // A construction site torn down leaves part of its materials on the ground
            if (!built) DropSalvage();
            Debug.Log($"[Buildings] {Definition?.DisplayName} is gone.", this);
            if (built) AnyFell?.Invoke(this);
            Remove();
        }

        /// <summary>Share of the building cost (GameplaySettings: 50%) as loot piles around the site.</summary>
        void DropSalvage()
        {
            var info = Info;
            var map = MapData.Instance;
            if (info == null || map == null || info.buildCost == null) return;
            float share = Gameplay.constructionSalvageShare;
            var drops = new List<DropEntry>();
            foreach (var c in info.buildCost)
            {
                if (c == null || c.item == null) continue;
                int amount = Mathf.FloorToInt(c.amount * share);
                if (amount > 0) drops.Add(new DropEntry(c.item, amount));
            }
            if (drops.Count == 0) return;
            ItemDropper.Drop(drops, Area, transform.parent, map);
            Debug.Log($"[Buildings] {Definition?.DisplayName} site torn down: {Mathf.RoundToInt(share * 100f)}% of its materials dropped.", this);
        }

        /// <summary>
        /// Removes a template; the materials delivered so far are put back on the ground as loot. Started sites are
        /// torn down with Destroy instead.
        /// </summary>
        public void CancelConstruction()
        {
            if (!IsTemplate) return;
            var map = MapData.Instance;
            if (supplied.Count > 0 && map != null)
            {
                var drops = new List<DropEntry>();
                foreach (var x in supplied) if (x.item != null && x.count > 0) drops.Add(new DropEntry(x.item, x.count));
                ItemDropper.Drop(drops, Area, transform.parent, map);
            }
            supplied.Clear();
            ReleaseAll();
            Remove();
        }

        // ------------------------------------------------------------------ look

        protected override int SortingOrderAt(Vector2 basePoint)
        {
            var info = Info;
            if (info != null && info.drawUnderCharacters) return Gameplay.underCharactersSortingOrder;
            return base.SortingOrderAt(basePoint);
        }

        public override bool MakesGroundWalkable
        {
            get
            {
                var info = Info;
                return info != null && info.isBridge && !IsDepleted && phase != BuildingPhase.Destroyed && !IsTemplate &&
                       (phase == BuildingPhase.Complete || info.ConstructionStageFor(Health) == null);
            }
        }

        void CreateVisuals()
        {
            var info = Info;
            int count = info != null && info.isBridge ? Mathf.Max(1, info.CellsAlongLength) : 1;
            for (int i = visuals.Count; i < count; i++)
            {
                var go = new GameObject(i == 0 ? "Sprite" : $"Sprite {i}");
                go.transform.SetParent(transform, false);
                visuals.Add(go.AddComponent<SpriteRenderer>());
            }
            if (info != null && info.isBridge)
                for (int e = 0; e < 2; e++)
                {
                    if (joints[e] != null) continue;
                    var go = new GameObject(e == 0 ? "Joint Start" : "Joint End");
                    go.transform.SetParent(transform, false);
                    joints[e] = go.AddComponent<SpriteRenderer>();
                    joints[e].enabled = false;
                }
            InvalidateRenderers();
        }

        void Update()
        {
            bool active = MineActive;
            if (Health != lastHealth || phase != lastPhase || active != lastActive || IsTemplate != lastTemplate || (Info != null && Info.isBridge && lastBridgeVersion != bridgeVersion))
                RefreshLook(false);
            occupants.RemoveAll(u => u == null);
            if (percher == null && towerBonus) RemoveTowerBonus(null);
            UpdateTower();
            UpdateFurnace(Time.deltaTime);
            UpdateMine(Time.deltaTime);
        }

        bool MineActive => Info != null && Info.IsMine && IsUsable && occupants.Count > 0 && !MineExhausted;

        /// <summary>Shows the template / construction / finished / active / destroyed look.</summary>
        void RefreshLook(bool force)
        {
            var info = Info;
            bool active = MineActive;
            bool stateChanged = phase != lastPhase || active != lastActive;
            lastHealth = Health;
            lastPhase = phase;
            lastActive = active;
            lastBridgeVersion = bridgeVersion;
            bool walkable = MakesGroundWalkable;
            if (walkable != lastWalkable)
            {
                lastWalkable = walkable;
                bridgeVersion++;
                lastBridgeVersion = bridgeVersion;
                if (MapData.Instance != null) MapData.Instance.RefreshCells(Area);
            }
            if (info == null || visuals.Count == 0) return;

            BuildingStage stage = null;
            if (phase == BuildingPhase.Destroyed) stage = info.DestroyedStageFor(Health);
            else if (phase == BuildingPhase.Construction && !IsTemplate) stage = info.ConstructionStageFor(Health);

            var sprite = stage != null ? stage.sprite : active && info.activeSprite != null ? info.activeSprite : info.PreviewSprite;
            float scale = stage != null ? stage.scale : info.finishedScale;
            float inset = stage != null ? stage.groundInset : info.GroundInset;
            bool repeat = stage != null && stage.repeatPerCell;
            bool tiles = stage == null && info.isBridge && info.HasBridgeTiles;
            var ground = GroundPoint;
            int order = SortingOrderAt(ground);
            float alpha = BaseAlpha;
            // Only touch the opacity when the template state changes (the proximity fade owns it otherwise)
            bool template = IsTemplate;
            bool setAlpha = force || template != lastTemplate;
            lastTemplate = template;

            for (int i = 0; i < visuals.Count; i++)
            {
                var r = visuals[i];
                bool used = sprite != null && (i == 0 || repeat || tiles);
                r.enabled = used;
                if (!used) continue;
                var cellSprite = tiles ? TileFor(i, visuals.Count) : sprite;
                r.sprite = cellSprite;
                r.sortingOrder = order;
                r.transform.localScale = new Vector3(scale, scale, 1f);
                if (setAlpha)
                {
                    var c = r.color;
                    r.color = new Color(c.r, c.g, c.b, alpha);
                }
                Vector2 local;
                if (repeat || tiles)
                {
                    var cellCenter = CellCenterAlong(i);
                    local = cellCenter - ground - (Vector2)cellSprite.bounds.center * scale;
                }
                else if (info.centerOnFootprint)
                {
                    var center = new Vector2(Cell.x + Footprint.x * 0.5f, Cell.y + Footprint.y * 0.5f);
                    local = center - ground - (Vector2)cellSprite.bounds.center * scale;
                }
                else
                {
                    // Stand the sprite's visible base on the ground point
                    local = new Vector2(-cellSprite.bounds.center.x * scale, (cellSprite.bounds.extents.y - cellSprite.bounds.center.y - inset) * scale);
                }
                r.transform.localPosition = new Vector3(local.x, local.y, 0f);
            }
            UpdateJoints(tiles, ground, order, setAlpha ? alpha : -1f);
            if (percher != null) percher.SetPerchSortingOrder(order + 1);
            if (force || stateChanged || sprite != shownSprite) AnyChanged?.Invoke(this);
            shownSprite = sprite;
        }

        /// <summary>
        /// Where this bridge's end meets a bridge running the other way, a middle tile is laid under both bridges from
        /// this end into the other bridge's cell, so the two join without a gap and the other bridge keeps its posts.
        /// </summary>
        void UpdateJoints(bool tiles, Vector2 ground, int order, float alpha)
        {
            var info = Info;
            var map = MapData.Instance;
            for (int e = 0; e < 2; e++)
            {
                var joint = joints[e];
                if (joint == null) continue;
                bool show = false;
                if (tiles && map != null && info.tileMiddle != null)
                {
                    var endCell = e == 0 ? BridgeRules.StartCell(Area) : BridgeRules.EndCell(Area);
                    var outward = e == 0 ? BridgeRules.StartOutward(Area) : BridgeRules.EndOutward(Area);
                    var ahead = BridgeRules.AheadOf(map, endCell, outward, this);
                    if (ahead != null && BridgeRules.IsHorizontal(ahead.Area) != BridgeRules.IsHorizontal(Area))
                    {
                        show = true;
                        var sprite = info.tileMiddle;
                        joint.sprite = sprite;
                        joint.sortingOrder = order - 1; // under both bridges: their posts and planks stay on top
                        joint.transform.localScale = Vector3.one;
                        // From this end's centre to the middle of the other bridge's cell, so the planks meet without a gap
                        var center = (Vector2)MapData.CellCenter(endCell) + (Vector2)outward * 0.5f;
                        var local = center - ground - (Vector2)sprite.bounds.center;
                        joint.transform.localPosition = new Vector3(local.x, local.y, 0f);
                        if (alpha >= 0f)
                        {
                            var c = joint.color;
                            joint.color = new Color(c.r, c.g, c.b, alpha);
                        }
                    }
                }
                joint.enabled = show;
            }
        }

        Vector2 CellCenterAlong(int i)
        {
            bool horizontal = Footprint.x >= Footprint.y;
            return horizontal ? new Vector2(Cell.x + i + 0.5f, Cell.y + 0.5f) : new Vector2(Cell.x + 0.5f, Cell.y + i + 0.5f);
        }

        /// <summary>Bridge tile of cell i: the ends have their posts, unless another bridge joins there.</summary>
        Sprite TileFor(int i, int count)
        {
            var info = Info;
            if (count <= 1) return info.tileMiddle;
            var map = MapData.Instance;
            if (i == 0) return map != null && BridgeRules.JoinedAt(map, Area, BridgeRules.StartCell(Area), BridgeRules.StartOutward(Area), this) ? info.tileMiddle : info.tileStart;
            if (i == count - 1) return map != null && BridgeRules.JoinedAt(map, Area, BridgeRules.EndCell(Area), BridgeRules.EndOutward(Area), this) ? info.tileMiddle : info.tileEnd;
            return info.tileMiddle;
        }

        // ------------------------------------------------------------------ characters inside

        public bool Accepts(Unit unit, out string reason)
        {
            reason = null;
            var info = Info;
            if (info == null || unit == null) { reason = "Nobody can enter"; return false; }
            if (phase == BuildingPhase.Construction) { reason = "Not finished yet"; return false; }
            if (phase == BuildingPhase.Destroyed) { reason = "Destroyed: repair it first"; return false; }
            if (Capacity == 0 || Type == BuildingType.Tower || Type == BuildingType.Bridge || Type == BuildingType.Furnace) { reason = "Nobody can enter"; return false; }
            if (MineExhausted) { reason = "Exhausted"; return false; }
            var role = unit.Villager != null ? unit.Villager.Role : VillagerRole.Villager;
            bool allowed = VillagerRoles.IsSoldier(role) ? info.soldiersCanEnter
                         : role == VillagerRole.Priest ? info.priestsCanEnter
                         : info.villagersCanEnter;
            if (!allowed) { reason = $"{role}s cannot enter"; return false; }
            if (occupants.Contains(unit)) { reason = "Already inside"; return false; }
            if (Capacity > 0 && OccupantCount >= Capacity) { reason = $"Full ({OccupantCount}/{Capacity})"; return false; }
            return true;
        }

        public bool Enter(Unit unit)
        {
            if (!Accepts(unit, out _)) return false;
            occupants.Add(unit);
            unit.EnterHost(this, true, Vector2.zero, 0);
            AnyChanged?.Invoke(this);
            return true;
        }

        public void Release(Unit unit)
        {
            if (unit == null) return;
            if (unit == percher)
            {
                ComeDown();
                return;
            }
            if (!occupants.Remove(unit)) return;
            unit.LeaveHost(ExitPoint());
            AnyChanged?.Invoke(this);
        }

        /// <summary>Everybody out (destroyed, removed).</summary>
        public void ReleaseAll()
        {
            if (percher != null) ComeDown();
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                var u = occupants[i];
                occupants.RemoveAt(i);
                if (u != null) u.LeaveHost(ExitPoint());
            }
            AnyChanged?.Invoke(this);
        }

        Vector2 ExitPoint() => DoorPoint + new Vector2(UnityEngine.Random.Range(-0.35f, 0.35f), -UnityEngine.Random.Range(0f, 0.3f));

        // ------------------------------------------------------------------ tower

        Vector2 PerchPoint => GroundPoint + (Info != null ? Info.perchOffset : new Vector2(0f, 2.3f));

        public bool CanClimb(Unit unit, out string reason)
        {
            reason = null;
            if (Type != BuildingType.Tower) { reason = "Not a tower"; return false; }
            if (phase == BuildingPhase.Construction) { reason = "Not finished yet"; return false; }
            if (phase == BuildingPhase.Destroyed) { reason = "Destroyed: repair it first"; return false; }
            if (percher != null && percher != unit) { reason = $"{NameOf(percher)} is on top (1/1)"; return false; }
            if (percher == unit) { reason = "Already on top"; return false; }
            return true;
        }

        public bool Climb(Unit unit)
        {
            if (!CanClimb(unit, out _)) return false;
            percher = unit;
            perchSince = Time.time;
            archeryApplied = 0;
            unit.EnterHost(this, false, PerchPoint, SortingOrderAt(GroundPoint) + 1);
            unit.Face(Vector2.right);
            // Fear of heights shows itself up there
            var villager = unit.Villager;
            if (villager != null)
                foreach (var t in villager.Traits)
                    if (t.definition != null && t.definition.UselessOnTowers) villager.RevealTrait(t.definition);
            if (IsUsefulArcher(unit)) ApplyTowerBonus();
            AnyChanged?.Invoke(this);
            return true;
        }

        public void ComeDown()
        {
            var unit = percher;
            if (unit == null) return;
            RemoveTowerBonus(unit);
            percher = null;
            unit.LeaveHost(DoorPoint);
            AnyChanged?.Invoke(this);
        }

        /// <summary>An archer without fear of heights: shoots from the tower and gets its bonuses.</summary>
        public static bool IsUsefulArcher(Unit unit)
        {
            var villager = unit != null ? unit.Villager : null;
            if (villager == null || villager.Role != VillagerRole.Archer) return false;
            return !HasFearOfHeights(unit);
        }

        public static bool HasFearOfHeights(Unit unit)
        {
            var villager = unit != null ? unit.Villager : null;
            if (villager == null) return false;
            foreach (var t in villager.Traits)
                if (t.definition != null && t.definition.UselessOnTowers) return true;
            return false;
        }

        /// <summary>Range multiplier of a unit (an archer on this tower shoots twice as far).</summary>
        public float RangeMultiplierFor(Unit unit) =>
            unit != null && unit == percher && IsUsefulArcher(unit) && Info != null ? Info.archerRangeMultiplier : 1f;

        /// <summary>Archery points the archer on top got from the tower so far.</summary>
        public int ArcheryBonus => archeryApplied;

        void ApplyTowerBonus()
        {
            if (towerBonus || Info == null || Definition == null || phase != BuildingPhase.Complete) return;
            towerBonus = true;
            ApplyPhaseMax(true, true);
        }

        void RemoveTowerBonus(Unit unit)
        {
            if (unit != null && unit.Villager != null && archeryApplied != 0)
                unit.Villager.Stats.Add(StatType.Archery, -archeryApplied);
            archeryApplied = 0;
            if (!towerBonus || Definition == null) return;
            towerBonus = false;
            if (phase != BuildingPhase.Complete) return;
            ApplyPhaseMax(true, true);
            if (Health < 1 && !collapsing) SetHealth(1);
        }

        void UpdateTower()
        {
            if (percher == null || Info == null || !IsUsefulArcher(percher)) return;
            if (Info.archeryBonusPerStep <= 0 || archeryApplied >= Info.archeryBonusMax) return;
            int steps = Mathf.FloorToInt((Time.time - perchSince) / Mathf.Max(1f, Info.archeryBonusInterval));
            int wanted = Mathf.Min(Info.archeryBonusMax, steps * Info.archeryBonusPerStep);
            if (wanted <= archeryApplied) return;
            var stats = percher.Villager.Stats;
            int before = stats.Get(StatType.Archery);
            stats.Add(StatType.Archery, wanted - archeryApplied);
            int gained = stats.Get(StatType.Archery) - before;
            archeryApplied = gained == 0 ? wanted : archeryApplied + gained; // stat already at its maximum
            AnyChanged?.Invoke(this);
        }

        static string NameOf(Unit unit) => unit != null && unit.Villager != null ? unit.Villager.DisplayName : unit != null ? unit.name : "?";

        // ------------------------------------------------------------------ inventory

        /// <summary>Can this item be stored here (furnace: only its inputs)?</summary>
        public bool AcceptsItem(SpawnableDefinition item)
        {
            var info = Info;
            if (info == null || !info.HasInventory || item == null) return false;
            if (info.IsFurnace) return item == info.furnaceInputA || item == info.furnaceInputB;
            return true;
        }

        /// <summary>
        /// Stores items (only in usable buildings) or delivers materials to a template. Returns how many were taken.
        /// </summary>
        public int Stock(SpawnableDefinition item, int amount)
        {
            if (RequiresMaterials)
            {
                int take = Mathf.Min(amount, Missing(item));
                if (take <= 0) return 0;
                string before = SaveState();
                var slot = supplied.Find(x => x.item == item);
                if (slot == null) supplied.Add(slot = new InventorySlot { item = item });
                slot.count += take;
                RaiseStateChanged(before);
                AnyChanged?.Invoke(this);
                if (MaterialsComplete) Debug.Log($"[Buildings] All materials of {Definition?.DisplayName} have arrived: construction can start.", this);
                return take;
            }
            if (!IsUsable || !AcceptsItem(item)) return 0;
            return inventory.Add(item, amount);
        }

        /// <summary>Takes items out. Returns how many were taken.</summary>
        public int Withdraw(SpawnableDefinition item, int amount)
        {
            if (!IsUsable) return 0;
            return inventory.Take(item, amount);
        }

        /// <summary>Space left for the item here (0 if it is not accepted).</summary>
        public int SpaceFor(SpawnableDefinition item) =>
            RequiresMaterials ? Missing(item) : IsUsable && AcceptsItem(item) ? inventory.SpaceFor(item) : 0;

        // ------------------------------------------------------------------ treasury (stores of castle and houses)

        static bool IsStore(Building b) => b != null && b.IsUsable && b.Info != null && b.Info.HasInventory && !b.Info.IsFurnace;

        /// <summary>True if the kingdom has at least one finished store (castle or house).</summary>
        public static bool AnyStore() => All.Exists(IsStore);

        /// <summary>Items of a kind in all stores (castle first, then houses...).</summary>
        public static int TreasuryCount(SpawnableDefinition item)
        {
            int total = 0;
            foreach (var b in All) if (IsStore(b)) total += b.inventory.Count(item);
            return total;
        }

        /// <summary>Every item kind in the stores with its total (resource bar).</summary>
        public static Dictionary<SpawnableDefinition, int> TreasuryContents()
        {
            var result = new Dictionary<SpawnableDefinition, int>();
            foreach (var b in All)
            {
                if (!IsStore(b)) continue;
                foreach (var slot in b.inventory.Slots)
                {
                    if (slot.item == null) continue;
                    result.TryGetValue(slot.item, out int n);
                    result[slot.item] = n + slot.count;
                }
            }
            return result;
        }

        static List<Building> StoresCastleFirst()
        {
            var stores = All.FindAll(IsStore);
            stores.Sort((a, b) => (a.Type == BuildingType.Castle ? 0 : 1).CompareTo(b.Type == BuildingType.Castle ? 0 : 1));
            return stores;
        }

        /// <summary>Takes items from the stores (castle first). False (nothing taken) if there are not enough.</summary>
        public static bool SpendFromTreasury(SpawnableDefinition item, int amount)
        {
            if (amount <= 0) return true;
            if (item == null || TreasuryCount(item) < amount) return false;
            foreach (var b in StoresCastleFirst())
            {
                if (amount <= 0) break;
                amount -= b.Withdraw(item, amount);
            }
            return true;
        }

        /// <summary>Puts items into the stores (castle first). Returns how many did not fit.</summary>
        public static int Deposit(SpawnableDefinition item, int amount)
        {
            foreach (var b in StoresCastleFirst())
            {
                if (amount <= 0) break;
                amount -= b.Stock(item, amount);
            }
            return Mathf.Max(0, amount);
        }

        public static bool CanAfford(IReadOnlyList<DropEntry> cost)
        {
            if (cost == null) return true;
            foreach (var c in cost)
                if (c != null && c.item != null && c.amount > 0 && TreasuryCount(c.item) < c.amount) return false;
            return true;
        }

        public static bool Pay(IReadOnlyList<DropEntry> cost)
        {
            if (!CanAfford(cost)) return false;
            if (cost != null)
                foreach (var c in cost)
                    if (c != null && c.item != null && c.amount > 0) SpendFromTreasury(c.item, c.amount);
            return true;
        }

        /// <summary>Number of buildings of a definition (sites included).</summary>
        public static int CountOf(BuildingDefinition definition)
        {
            int n = 0;
            foreach (var b in All) if (b != null && b.Definition == definition && !b.IsDepleted) n++;
            return n;
        }

        /// <summary>Number of buildings of a type (sites included).</summary>
        public static int CountOf(BuildingType type, bool onlyUsable = false)
        {
            int n = 0;
            foreach (var b in All)
                if (b != null && b.Type == type && !b.IsDepleted && (!onlyUsable || b.IsUsable)) n++;
            return n;
        }

        // ------------------------------------------------------------------ furnace

        void UpdateFurnace(float deltaTime)
        {
            var info = Info;
            if (info == null || !info.IsFurnace) return;
            bool canWork = IsUsable
                           && info.furnaceInputA != null && inventory.Count(info.furnaceInputA) >= info.furnaceInputAAmount
                           && info.furnaceInputB != null && inventory.Count(info.furnaceInputB) >= info.furnaceInputBAmount
                           && inventory.SpaceFor(info.furnaceOutput) >= info.furnaceOutputAmount;
            furnaceWorking = canWork;
            if (smoke != null) smoke.Emitting = canWork;
            if (!canWork)
            {
                furnaceProgress = 0f;
                return;
            }
            furnaceProgress += deltaTime;
            if (furnaceProgress < info.furnaceSeconds) return;
            furnaceProgress = 0f;
            inventory.Take(info.furnaceInputA, info.furnaceInputAAmount);
            inventory.Take(info.furnaceInputB, info.furnaceInputBAmount);
            inventory.Add(info.furnaceOutput, info.furnaceOutputAmount);
        }

        // ------------------------------------------------------------------ gold mine

        void UpdateMine(float deltaTime)
        {
            var info = Info;
            if (info == null || !info.IsMine || occupants.Count == 0 || !IsUsable) return;
            if (MineExhausted)
            {
                Debug.Log($"[Buildings] {Definition.DisplayName} is exhausted.", this);
                ReleaseAll();
                return;
            }
            mineTimer += deltaTime * occupants.Count;
            float interval = Mathf.Max(0.1f, info.mineInterval);
            while (mineTimer >= interval && !MineExhausted)
            {
                mineTimer -= interval;
                if (info.mineReserve > 0)
                {
                    string before = SaveState();
                    mineReserve = MineReserve - 1;
                    RaiseStateChanged(before);
                }
                DropAtDoor(info.mineOutput, 1);
            }
        }

        /// <summary>Puts items on the pile at the door (a new pile when it is full: piles of the item's stack size).</summary>
        void DropAtDoor(SpawnableDefinition item, int amount)
        {
            var map = MapData.Instance;
            if (map == null || item == null) return;
            var door = MapData.WorldToCell(DoorPoint);
            var nearby = new List<WorldObject>();
            map.CollectObjects(new RectInt(door.x - 1, door.y - 1, 3, 3), nearby);
            foreach (var o in nearby)
            {
                if (o == null || o.IsCarried || o.Definition != item || o.Amount >= item.StackSize) continue;
                int add = Mathf.Min(amount, item.StackSize - o.Amount);
                o.AddAmount(add);
                amount -= add;
                if (amount <= 0) return;
            }
            while (amount > 0)
            {
                if (!ItemDropper.FindFreeCell(map, new RectInt(door, Vector2Int.one), 2, out var cell)) return;
                int pile = Mathf.Min(amount, item.StackSize);
                var offset = new Vector2(UnityEngine.Random.Range(-0.15f, 0.15f), UnityEngine.Random.Range(-0.08f, 0.08f));
                WorldObjectFactory.Create(item, cell, offset, transform.parent, map, configure: o => o.Amount = pile);
                amount -= pile;
            }
        }
    }
}
