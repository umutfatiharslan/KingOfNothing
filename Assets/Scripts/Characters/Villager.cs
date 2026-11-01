using System;
using System.Collections.Generic;
using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// A character of the kingdom: identity (name, age, story), role, stats with experience, needs, traits, day plan,
    /// work priorities, journal and training. Movement / selection is handled by the Unit component on the same
    /// object, the daily routine by CharacterBrain. It can be hit like any combat target, but never dies (health
    /// stays at least 1 until a death system exists).
    /// </summary>
    [RequireComponent(typeof(Unit))]
    [DisallowMultipleComponent]
    public class Villager : MonoBehaviour, ICombatTarget, IStatusHost, IHealthDisplay
    {
        public static readonly List<Villager> All = new List<Villager>();

        [SerializeField] string id;
        [SerializeField] VillagerIdentity identity = new VillagerIdentity();
        [SerializeField] VillagerRole role = VillagerRole.Villager;
        [SerializeField] StatBlock stats = new StatBlock();
        [SerializeField] VillagerNeeds needs = new VillagerNeeds();
        [SerializeField] List<TraitInstance> traits = new List<TraitInstance>();
        [SerializeField] int[] schedule = new int[0];
        [SerializeField] List<WorkPriority> priorities = new List<WorkPriority>();
        [SerializeField] List<JournalEntry> journal = new List<JournalEntry>();
        [SerializeField] VillagerMemory memory = new VillagerMemory();
        [SerializeField] TrainingAssignment training = new TrainingAssignment();

        [Header("Name tag")]
        [SerializeField] bool showNameTag = true;
        [SerializeField] Vector3 nameTagOffset = new Vector3(0f, 1.35f, 0f);
        [Min(0.01f)] [SerializeField] float nameTagCharacterSize = 0.08f;
        [SerializeField] Color nameTagColor = Color.white;

        /// <summary>(villager, trait) when a hidden trait shows up.</summary>
        public static event Action<Villager, TraitInstance> TraitRevealed;
        /// <summary>(villager, stat, new level).</summary>
        public static event Action<Villager, StatType, int> LevelUp;
        public event Action<Villager, VillagerRole> RoleChanged;

        const int MaxJournalEntries = 300;

        Unit unit;
        TextMesh nameTag;
        StatusEffects status;
        float lastHealth = -1f;
        float healthXpPool, defenseXpPool;
        float walkedCells;
        Vector2 lastPosition;
        readonly List<HealOverTime> hots = new List<HealOverTime>();

        sealed class HealOverTime
        {
            public float PerSecond;
            public float Left;
        }

        public string Id => id;
        public VillagerIdentity Identity => identity;
        public string DisplayName => identity.displayName;
        public VillagerRole Role => role;
        public StatBlock Stats => stats;
        public VillagerNeeds Needs => needs;
        public IReadOnlyList<TraitInstance> Traits => traits;
        public Unit Movement => unit;
        public VillagerMemory Memory => memory;
        public TrainingAssignment Training => training;
        public IReadOnlyList<JournalEntry> Journal => journal;
        public IReadOnlyList<WorkPriority> Priorities => priorities;
        public StatusEffects Status => status != null ? status : status = StatusEffects.Of(this);
        public Sprite Avatar => CharacterSettings.Current.AvatarFor(role);
        public CharacterBrain Brain { get; private set; }

        static StatSettings S => StatSettings.Current;
        static CharacterSettings C => CharacterSettings.Current;

        void Awake()
        {
            unit = GetComponent<Unit>();
            WoodcuttingRewards.Ensure();
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            stats.Clean();
            EnsureSchedule();
            EnsurePriorities();
            // Satiety and morale are driven here (stat effects), not by the old fixed rates
            needs.Satiety.changePerSecond = 0f;
            needs.Morale.changePerSecond = 0f;
            needs.Health.changePerSecond = 0f;
            stats.Changed += OnStatChanged;
            stats.LevelGained += OnLevelGained;
            if (showNameTag) CreateNameTag();
            Brain = GetComponent<CharacterBrain>();
            if (Brain == null) Brain = gameObject.AddComponent<CharacterBrain>();
            lastPosition = transform.position;
        }

        void Start()
        {
            if (role != VillagerRole.Villager) ApplyRoleLook();
            UpdateMaxHealth(false);
            lastHealth = needs.Health.current;
        }

        void OnEnable()
        {
            All.Add(this);
            GameClock.DayPassed += OnDayPassed;
        }

        void OnDisable()
        {
            All.Remove(this);
            GameClock.DayPassed -= OnDayPassed;
        }

        void OnDestroy()
        {
            stats.Changed -= OnStatChanged;
            stats.LevelGained -= OnLevelGained;
        }

        // ------------------------------------------------------------------ setup

        /// <summary>Sets up a freshly spawned villager.</summary>
        public void Setup(string displayName, int age, VillagerRole newRole)
        {
            identity.displayName = displayName;
            identity.age = age;
            gameObject.name = displayName;
            SetRole(newRole, false);
            if (nameTag != null) nameTag.text = displayName;
            AddJournal($"{displayName} joined the kingdom as a {newRole}.");
        }

        /// <summary>Restores a saved character (CharacterSave).</summary>
        public void LoadFrom(string savedId, string displayName, int age, VillagerRole savedRole)
        {
            id = string.IsNullOrEmpty(savedId) ? id : savedId;
            identity.displayName = displayName;
            identity.age = age;
            gameObject.name = displayName;
            role = savedRole;
            ApplyRoleLook();
            if (nameTag != null) nameTag.text = displayName;
        }

        public void SetRole(VillagerRole newRole) => SetRole(newRole, true);

        void SetRole(VillagerRole newRole, bool log)
        {
            if (role == newRole) return;
            var old = role;
            role = newRole;
            if (newRole == VillagerRole.Priest) ApplyPriestStats();
            ApplyRoleLook();
            if (log) AddJournal(newRole == VillagerRole.Priest ? "Became a priest." : $"Changed class: {old} -> {newRole}.");
            RoleChanged?.Invoke(this, role);
        }

        /// <summary>
        /// A priest heals (healing starts at 1) and does nothing else: every work stat and every war stat except
        /// defense and health drops to 0. Life stats stay.
        /// </summary>
        void ApplyPriestStats()
        {
            if (stats.Base(StatType.Healing) < 1) stats.Set(StatType.Healing, 1);
            foreach (var s in StatInfo.Work) stats.Set(s, 0);
            stats.Set(StatType.Archery, 0);
            stats.Set(StatType.Sword, 0);
            stats.Set(StatType.Spear, 0);
            training.Stop();
        }

        void EnsureSchedule()
        {
            if (schedule != null && schedule.Length == 24) return;
            schedule = new int[24];
            var c = C;
            for (int h = 0; h < 24; h++) schedule[h] = (int)c.DefaultBlock(h);
        }

        void EnsurePriorities()
        {
            foreach (var stat in WorkTypes.All)
                if (!priorities.Exists(p => p.stat == stat)) priorities.Add(new WorkPriority { stat = stat, priority = 3 });
        }

        // ------------------------------------------------------------------ schedule and work priorities

        public ScheduleBlock BlockAt(int hour)
        {
            EnsureSchedule();
            return (ScheduleBlock)Mathf.Clamp(schedule[Mathf.Clamp(hour, 0, 23)], 0, 2);
        }

        public void SetBlock(int hour, ScheduleBlock block)
        {
            EnsureSchedule();
            schedule[Mathf.Clamp(hour, 0, 23)] = (int)block;
        }

        public int[] ScheduleCopy()
        {
            EnsureSchedule();
            return (int[])schedule.Clone();
        }

        public void SetSchedule(int[] blocks)
        {
            if (blocks == null || blocks.Length != 24) return;
            schedule = (int[])blocks.Clone();
        }

        /// <summary>Priority of a work type: 1 (first) .. 5, 0 = never.</summary>
        public int PriorityOf(StatType stat)
        {
            var p = priorities.Find(x => x.stat == stat);
            return p != null ? p.priority : 0;
        }

        public void SetPriority(StatType stat, int priority)
        {
            var p = priorities.Find(x => x.stat == stat);
            if (p == null) priorities.Add(p = new WorkPriority { stat = stat });
            p.priority = Mathf.Clamp(priority, 0, 5);
        }

        public void LoadPriorities(List<WorkPriority> saved)
        {
            priorities.Clear();
            if (saved != null) foreach (var p in saved) if (p != null) priorities.Add(new WorkPriority { stat = p.stat, priority = p.priority });
            EnsurePriorities();
        }

        public List<WorkPriority> PrioritiesCopy()
        {
            var list = new List<WorkPriority>();
            foreach (var p in priorities) list.Add(new WorkPriority { stat = p.stat, priority = p.priority });
            return list;
        }

        // ------------------------------------------------------------------ journal

        public void AddJournal(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            journal.Add(new JournalEntry { day = GameClock.Today, hour = GameClock.CurrentHour, text = text });
            if (journal.Count > MaxJournalEntries) journal.RemoveRange(0, journal.Count - MaxJournalEntries);
        }

        public void LoadJournal(List<JournalEntry> entries)
        {
            journal.Clear();
            if (entries != null) journal.AddRange(entries);
        }

        public void LoadMemory(VillagerMemory saved)
        {
            if (saved != null) memory = saved;
        }

        public void LoadTraining(TrainingAssignment saved)
        {
            if (saved != null) training = saved;
        }

        // ------------------------------------------------------------------ experience

        /// <summary>
        /// Experience from an action (the training multiplier of the current level and meal buffs are applied).
        /// Stats at 0 never learn. Shows the "+2 Woodcutting XP" popup.
        /// </summary>
        public void GainXp(StatType stat, float amount, bool popup = true)
        {
            if (amount <= 0f || !stats.CanDo(stat) || stats.Base(stat) >= StatBlock.MaxLevel) return;
            float multiplier = stats.IsTrained(stat) ? S.TrainingMultiplier(stats.Base(stat)) : 1f;
            if (memory.masterStat == (int)stat && GameClock.Now < memory.masterUntil) multiplier *= S.masterXpMultiplier;
            float total = amount * multiplier;
            stats.AddXp(stat, total, S.XpToNext);
            if (popup && !unit.IsHidden)
                FloatingText.Show(transform, $"+{total:0.#} {StatInfo.Name(stat)} XP", C.xpColor, C.xpArrow);
        }

        void OnStatChanged(StatType stat, int oldLevel, int newLevel)
        {
            if (stat == StatType.Health) UpdateMaxHealth(true);
        }

        void OnLevelGained(StatType stat, int oldLevel, int newLevel)
        {
            if (newLevel <= oldLevel) return;
            AddJournal($"{StatInfo.Name(stat)} reached level {newLevel}.");
            FloatingText.Show(transform, $"{StatInfo.Name(stat)} {newLevel}!", C.levelUpColor, C.xpArrow);
            for (int m = StatBlock.MilestoneInterval; m <= StatBlock.MaxLevel; m += StatBlock.MilestoneInterval)
            {
                if (oldLevel >= m || newLevel < m) continue;
                var milestone = Milestones.At(stat, m);
                if (milestone == null) continue;
                AddJournal($"Unlocked {milestone.Name} ({StatInfo.Name(stat)} {m}): {milestone.Description}");
                FloatingText.Show(transform, $"Unlocked: {milestone.Name}", C.levelUpColor, C.xpArrow);
                Debug.Log($"[Stats] {DisplayName} unlocked {milestone.Name} ({stat} {m}).", this);
            }
            LevelUp?.Invoke(this, stat, newLevel);
        }

        // ------------------------------------------------------------------ needs, health

        public int MaxHealth => StatEffects.MaxHealth(this, memory.bonusMaxHealth);

        void UpdateMaxHealth(bool keepMissing)
        {
            float max = MaxHealth;
            var h = needs.Health;
            if (Mathf.Approximately(h.max, max)) return;
            float missing = h.max - h.current;
            h.max = max;
            h.current = keepMissing ? Mathf.Clamp(max - missing, 1f, max) : Mathf.Clamp(h.current, 1f, max);
        }

        /// <summary>Work buff of feast meals (1 = none).</summary>
        public float WorkBuffMultiplier => GameClock.Now < memory.feastUntil ? 1f + S.feastWorkBonus15 : 1f;

        void Update()
        {
            float dt = Time.deltaTime;
            float hours = GameClock.HoursFromSeconds(dt);
            UpdateMaxHealth(true);
            TickNeeds(hours);
            TickHealing(dt);
            TrackHealthXp();
            TrackWalking();

            float moveMultiplier = 1f;
            foreach (var trait in traits)
            {
                if (trait.definition == null || !trait.revealed) continue;
                trait.definition.Tick(this, dt);
                moveMultiplier *= trait.definition.MoveSpeedMultiplier;
            }
            // Traits, the walk speed stat and its milestones change how fast the character walks
            unit.SpeedModifier = moveMultiplier * StatEffects.WalkMultiplier(this) * MilestoneSpeed();
        }

        float MilestoneSpeed()
        {
            float m = 1f;
            if (stats.Has(StatType.WalkSpeed, 5) && Time.time < memory.commandBoostUntil) m += S.commandBoost5;
            if (stats.Has(StatType.WalkSpeed, 10) && needs.Health.Normalized < S.lowHealthThreshold10) m += S.lowHealthBoost10;
            return m;
        }

        /// <summary>The player gave an order (Nimble: a short burst of speed).</summary>
        public void OnPlayerOrder()
        {
            if (stats.Has(StatType.WalkSpeed, 5)) memory.commandBoostUntil = Time.time + S.commandBoostSeconds5;
        }

        void TickNeeds(float hours)
        {
            if (hours <= 0f) return;
            var c = C;
            float rate = c.satietyLossPerHour * StatEffects.HungerRate(this);
            if (unit.IsHidden && stats.Has(StatType.Hunger, 5)) rate *= S.insideHungerRate5;
            needs.Satiety.Change(-rate * hours);
            if (needs.Satiety.current <= 0f && needs.Health.current > 1f)
                needs.Health.Change(-Mathf.Min(needs.Health.current - 1f, c.starvingHealthLossPerHour * hours));
            // Second Wind (Health 5): once in the whole game
            if (stats.Has(StatType.Health, 5) && !memory.secondWindUsed && needs.Health.Normalized < S.secondWindThreshold)
            {
                memory.secondWindUsed = true;
                AddHealOverTime(MaxHealth * S.secondWindHeal, S.secondWindSeconds);
                AddJournal("Second Wind: got back on the feet.");
                FloatingText.Show(transform, "Second Wind!", C.levelUpColor);
            }
        }

        void TickHealing(float dt)
        {
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                var h = hots[i];
                float step = Mathf.Min(h.Left, h.PerSecond * dt);
                h.Left -= step;
                needs.Health.Change(step);
                if (h.Left <= 0.001f) hots.RemoveAt(i);
            }
            if (memory.shieldAmount > 0f && Time.time > memory.shieldUntil) memory.shieldAmount = 0f;
        }

        public void AddHealOverTime(float amount, float seconds)
        {
            if (amount <= 0f) return;
            hots.Add(new HealOverTime { Left = amount, PerSecond = amount / Mathf.Max(0.1f, seconds) });
        }

        void TrackHealthXp()
        {
            float now = needs.Health.current;
            if (lastHealth < 0f) lastHealth = now;
            float delta = Mathf.Abs(now - lastHealth);
            lastHealth = now;
            if (delta <= 0f) return;
            healthXpPool += delta * S.healthXpPerPoint;
            if (healthXpPool >= 1f)
            {
                float whole = Mathf.Floor(healthXpPool);
                healthXpPool -= whole;
                GainXp(StatType.Health, whole);
            }
        }

        void TrackWalking()
        {
            Vector2 p = transform.position;
            if (unit.IsMoving && !unit.IsHidden)
            {
                float step = (p - lastPosition).magnitude;
                if (step < 2f) walkedCells += step;
                float per = Mathf.Max(0.1f, S.walkCellsPerXp);
                if (walkedCells >= per)
                {
                    walkedCells -= per;
                    GainXp(StatType.WalkSpeed, 1f);
                }
            }
            lastPosition = p;
        }

        void OnDayPassed(int day)
        {
            // Fresh Morning (Health 10)
            if (stats.Has(StatType.Health, 10)) Heal(MaxHealth * S.morningHeal10, null, false);
            // Forest Keeper (Woodcutting 20): plants a sapling after a busy day
            if (stats.Has(StatType.Woodcutting, 20) && memory.felledDay == day - 1 && memory.felledToday >= S.saplingTreesPerDay)
                WoodcuttingRewards.PlantSapling(this);
        }

        /// <summary>Heals (never above the maximum). Returns how much was healed; the healer learns from it.</summary>
        public float Heal(float amount, Villager healer, bool popup = true)
        {
            if (amount <= 0f) return 0f;
            float before = needs.Health.current;
            needs.Health.Change(amount);
            float healed = needs.Health.current - before;
            if (healed > 0f && healer != null) healer.GainXp(StatType.Healing, healed * S.healingXpPerPoint);
            if (healed > 0.5f && popup && !unit.IsHidden) FloatingText.Show(transform, $"+{healed:0} health", new Color(0.5f, 1f, 0.6f));
            return healed;
        }

        /// <summary>Loses health without combat rules (training, collapses...). Never below 1.</summary>
        public void LoseHealth(float amount)
        {
            if (amount <= 0f) return;
            needs.Health.Change(-Mathf.Min(amount, needs.Health.current - 1f));
        }

        // ------------------------------------------------------------------ meals

        /// <summary>Eats one item: satiety, meal experience and the effects of special meals.</summary>
        public void Eat(SpawnableDefinition food)
        {
            if (food == null) return;
            var c = C;
            float value = food.FoodValue;
            if (food.IsRawFood) value *= S.rawFoodShare10;
            bool wasFull = needs.Satiety.current >= needs.Satiety.max - 0.5f;
            needs.Satiety.Change(value);
            GainXp(StatType.Hunger, S.mealXp);
            // Fine Dine (Hunger 20): eating when full makes the body stronger
            if (wasFull && stats.Has(StatType.Hunger, 20))
            {
                int room = S.overeatMaxHealth20 - MaxHealth;
                int add = Mathf.Min(S.overeatHealth20, Mathf.Max(0, room));
                if (add > 0)
                {
                    memory.bonusMaxHealth += add;
                    UpdateMaxHealth(true);
                    needs.Health.Change(add);
                    AddJournal($"Fine Dine: maximum health +{add} ({MaxHealth}).");
                }
            }
            if (food == c.feastMeat || food == c.masterMeat)
            {
                needs.Morale.Change(S.feastMorale15);
                memory.feastUntil = GameClock.Now + S.feastHours15;
            }
            if (food == c.masterMeat) ApplyMasterMeal();
        }

        void ApplyMasterMeal()
        {
            var candidates = new List<StatType>();
            foreach (var s in StatInfo.All)
                if (stats.CanDo(s) && stats.Base(s) < StatBlock.MaxLevel) candidates.Add(s);
            if (candidates.Count == 0) return;
            var stat = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            memory.masterStat = (int)stat;
            memory.masterUntil = GameClock.Now + S.masterHours;
            float xp = S.XpToNext(stats.Base(stat)) * S.masterXpShare;
            stats.AddXp(stat, xp, S.XpToNext);
            AddJournal($"Ate a master meal: {StatInfo.Name(stat)} +{xp:0} XP and learns it twice as fast for a day.");
            FloatingText.Show(transform, $"Master meal: {StatInfo.Name(stat)}!", C.levelUpColor, C.xpArrow);
        }

        // ------------------------------------------------------------------ combat target

        public bool IsAlive => this != null && isActiveAndEnabled && unit != null && !unit.IsHidden;
        public Rect Hitbox
        {
            get
            {
                Vector2 p = transform.position;
                return new Rect(p.x - 0.3f, p.y - 0.05f, 0.6f, 0.9f);
            }
        }
        public Vector2 GroundPoint => transform.position;
        public TargetKind TargetKind => TargetKind.Character;
        public bool Moves => true;
        public bool ImmuneToStatus => stats.Has(StatType.Defense, 20);
        public float StatusIconHeight => 1.2f;

        public float CurrentHealth => needs.Health.current;
        public float MaximumHealth => needs.Health.max;
        public Vector3 BarAnchor => transform.position + new Vector3(0f, 1.05f, 0f);
        public bool BarHidden => unit == null || unit.IsHidden;

        public void TakeStatusDamage(int amount)
        {
            LoseHealth(amount);
            HealthBar.NotifyUnit(this);
        }

        public int ReceiveHit(HitInfo hit) => CombatRules.HitVillager(this, hit);

        /// <summary>Damage after every rule (called by CombatRules). Never below 1 health.</summary>
        public int ApplyDamage(float damage)
        {
            if (damage <= 0f) return 0;
            // Iron Body shield first
            if (memory.shieldAmount > 0f)
            {
                float absorbed = Mathf.Min(memory.shieldAmount, damage);
                memory.shieldAmount -= absorbed;
                damage -= absorbed;
            }
            if (damage <= 0f) return 0;
            float before = needs.Health.current;
            bool deadly = before - damage < 1f;
            if (deadly)
            {
                int today = GameClock.Today;
                if (stats.Has(StatType.Sword, 20) && memory.lastStandDay != today)
                {
                    memory.lastStandDay = today;
                    AddJournal("Last Stand: survived a deadly hit.");
                }
                else if (stats.Has(StatType.Health, 20) && today - memory.immortalDay >= S.immortalWillDays20)
                {
                    memory.immortalDay = today;
                    AddJournal("Immortal Will: survived a deadly hit.");
                }
            }
            LoseHealth(damage);
            int done = Mathf.RoundToInt(before - needs.Health.current);
            defenseXpPool += damage * S.defenseXpPerDamage;
            if (defenseXpPool >= 1f)
            {
                float whole = Mathf.Floor(defenseXpPool);
                defenseXpPool -= whole;
                GainXp(StatType.Defense, whole);
            }
            HealthBar.NotifyUnit(this);
            return done;
        }

        // ------------------------------------------------------------------ traits and look

        /// <summary>Work speed multiplier after all active traits (1 = normal).</summary>
        public float WorkSpeedMultiplier
        {
            get
            {
                float speed = 1f;
                foreach (var trait in traits)
                    if (trait.definition != null && trait.revealed) speed = trait.definition.ModifyWorkSpeed(this, speed);
                return speed;
            }
        }

        /// <summary>Animations of the role (pawn, warrior, lancer, archer, monk) from the GameplaySettings asset.</summary>
        void ApplyRoleLook()
        {
            if (unit == null) unit = GetComponent<Unit>();
            var map = MapData.Instance;
            if (unit == null || map == null) return;
            var g = map.Gameplay;
            PawnAnimationSet set;
            switch (role)
            {
                case VillagerRole.Warrior: set = g.warriorAnimations; break;
                case VillagerRole.Lancer: set = g.lancerAnimations; break;
                case VillagerRole.Archer: set = g.archerAnimations; break;
                case VillagerRole.Priest: set = g.priestAnimations; break;
                default: set = g.villagerAnimations; break;
            }
            if (set != null) unit.Animations = set;
            unit.ClearWorkAnimation();
        }

        public bool HasTrait(TraitDefinition definition) => traits.Exists(t => t.definition == definition);

        public void AddTrait(TraitDefinition definition, bool revealed)
        {
            if (definition == null || HasTrait(definition)) return;
            var instance = new TraitInstance(definition, revealed || !definition.HiddenUntilTriggered);
            traits.Add(instance);
            if (instance.revealed) OnTraitRevealed(instance);
        }

        /// <summary>Makes a hidden trait visible (it then shows in the character book and becomes active).</summary>
        public bool RevealTrait(TraitDefinition definition)
        {
            var instance = traits.Find(t => t.definition == definition);
            if (instance == null || instance.revealed) return false;
            instance.revealed = true;
            OnTraitRevealed(instance);
            return true;
        }

        void OnTraitRevealed(TraitInstance instance)
        {
            instance.definition.OnRevealed(this);
            AddJournal($"Trait revealed: {instance.definition.DisplayName}.");
            TraitRevealed?.Invoke(this, instance);
        }

        void CreateNameTag()
        {
            var go = KingOfNothing.Utilities.RuntimeChild.Replace(transform, "Name Tag");
            go.transform.localPosition = nameTagOffset;
            nameTag = go.AddComponent<TextMesh>();
            nameTag.text = identity.displayName;
            nameTag.anchor = TextAnchor.LowerCenter;
            nameTag.alignment = TextAlignment.Center;
            nameTag.characterSize = nameTagCharacterSize;
            nameTag.fontSize = 48;
            nameTag.color = nameTagColor;
            var nameTagRenderer = go.GetComponent<MeshRenderer>();
            // Always drawn above the map objects
            nameTagRenderer.sortingOrder = short.MaxValue - 10;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                nameTag.font = font;
                nameTagRenderer.sharedMaterial = font.material;
            }
        }
    }
}
