using System.Collections.Generic;
using KingOfNothing.Animals;
using KingOfNothing.Characters;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>Where food can be taken from.</summary>
    public static class FoodSources
    {
        static readonly List<WorldObject> Nearby = new List<WorldObject>();

        /// <summary>Can this character eat the item (raw meat needs Iron Stomach, Hunger 10)?</summary>
        public static bool CanEat(Villager v, SpawnableDefinition item) =>
            item != null && item.IsFood && (!item.IsRawFood || (v != null && v.Stats.Has(StatType.Hunger, 10)));

        /// <summary>The nearest store with food, or the nearest food pile on the ground (stores first, as agreed).</summary>
        public static bool Find(Villager v, out Building store, out SpawnableDefinition storeItem, out WorldObject pile)
        {
            store = null;
            storeItem = null;
            pile = null;
            Vector2 from = v.transform.position;
            float best = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || !b.IsPublicStore) continue;
                foreach (var slot in b.Inventory.Slots)
                {
                    if (slot == null || slot.count <= 0 || !CanEat(v, slot.item)) continue;
                    float d = Vector2.Distance(from, b.GroundPoint);
                    if (d < best)
                    {
                        best = d;
                        store = b;
                        storeItem = slot.item;
                    }
                }
            }
            if (store != null) return true;
            var map = MapData.Instance;
            if (map == null) return false;
            int r = CharacterSettings.Current.foodSearchRadius;
            var cell = MapData.WorldToCell(from);
            Nearby.Clear();
            map.CollectObjects(new RectInt(cell.x - r, cell.y - r, r * 2, r * 2), Nearby);
            foreach (var o in Nearby)
            {
                if (o == null || o.IsCarried || !(o is ResourceNode node) || !node.IsCollectable) continue;
                var item = StockLootJob.StoredItemOf(o.Definition);
                if (!CanEat(v, item)) continue;
                float d = Vector2.Distance(from, o.GroundPoint);
                if (d < best)
                {
                    best = d;
                    pile = o;
                }
            }
            return pile != null;
        }
    }

    /// <summary>Goes to a store (or a food pile), takes one meal and eats it. Fine Dine (Hunger 20) eats one more when full.</summary>
    public sealed class EatJob : UnitJob
    {
        readonly Approach approach = new Approach();
        Building store;
        SpawnableDefinition storeItem;
        WorldObject pile;
        SpawnableDefinition eating;
        float eatTime = -1f;
        bool extraMeal;

        public override string Label => eating != null ? $"Eating {eating.DisplayName}" : "Going to eat";
        public override object TargetObject => pile;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.ClearWorkAnimation();
            var v = unit.Villager;
            if (v != null) FoodSources.Find(v, out store, out storeItem, out pile);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            var v = unit.Villager;
            if (v == null) return JobState.Failed;
            if (eating != null)
            {
                eatTime -= deltaTime;
                if (eatTime > 0f) return JobState.Running;
                v.Eat(eating);
                eating = null;
                // Fine Dine: one more meal when the belly is full
                if (!extraMeal && v.Stats.Has(StatType.Hunger, 20) && v.Needs.Satiety.current >= v.Needs.Satiety.max - 0.5f)
                {
                    extraMeal = true;
                    if (TakeFood(v)) return JobState.Running;
                }
                return JobState.Done;
            }
            if (store == null && pile == null) return JobState.Failed;
            if (store != null)
            {
                if (!store.IsUsable || storeItem == null || store.Inventory.Count(storeItem) <= 0) return JobState.Failed;
                if (!approach.TickWork(unit, store.WorkBox, out bool failed, store)) return failed ? JobState.Failed : JobState.Running;
                Approach.Release(unit);
            }
            else
            {
                if (pile == null || pile.IsCarried) return JobState.Failed;
                var area = new Rect(pile.Area.position, pile.Area.size);
                if (!approach.Tick(unit, area, Approach.Reach(unit), out bool failed, pile)) return failed ? JobState.Failed : JobState.Running;
            }
            return TakeFood(v) ? JobState.Running : JobState.Failed;
        }

        bool TakeFood(Villager v)
        {
            if (store != null)
            {
                if (storeItem == null || store.Withdraw(storeItem, 1) <= 0) return false;
                eating = storeItem;
            }
            else
            {
                if (pile == null || pile.IsCarried) return false;
                eating = StockLootJob.StoredItemOf(pile.Definition);
                if (pile.Amount > 1) pile.AddAmount(-1);
                else
                {
                    pile.Remove();
                    pile = null;
                }
            }
            eatTime = CharacterSettings.Current.mealSeconds;
            return eating != null;
        }

        public override void Cancel(Unit unit) => Approach.Release(unit);
    }

    /// <summary>Goes home (house, army house for soldiers, church for priests, else the castle) and sleeps inside; without a bed on the ground.</summary>
    public sealed class SleepJob : UnitJob
    {
        readonly Building bed;
        readonly Approach approach = new Approach();
        Vector2 spot;
        bool asleep;

        public SleepJob(Building bed, Vector2 groundSpot)
        {
            this.bed = bed;
            spot = groundSpot;
        }

        public override string Label => asleep ? "Sleeping on the ground" : bed != null ? $"Going to sleep in {bed.DisplayName}" : "Going to sleep";
        public override object TargetObject => bed;
        public bool AsleepOnGround => asleep;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.ClearWorkAnimation();
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (asleep) return JobState.Running;
            if (bed != null)
            {
                if (!bed.Accepts(unit, out _)) return JobState.Failed;
                var door = bed.DoorPoint;
                if (!approach.Tick(unit, new Rect(door.x - 0.3f, door.y - 0.3f, 0.6f, 0.6f), 0.35f, out bool failed))
                    return failed ? JobState.Failed : JobState.Running;
                // Entering clears the jobs: the brain sees the character sleeping inside
                bed.Enter(unit);
                return JobState.Done;
            }
            if (!approach.Tick(unit, new Rect(spot.x - 0.2f, spot.y - 0.2f, 0.4f, 0.4f), 0.4f, out bool lost))
            {
                if (!lost) return JobState.Running;
            }
            asleep = true;
            unit.Stop();
            return JobState.Running;
        }

        public override void Cancel(Unit unit) => asleep = false;
    }

    /// <summary>Recreation: wanders around the castle / church and chats with others who are free.</summary>
    public sealed class RecreationJob : UnitJob
    {
        readonly Vector2 center;
        readonly float radius;
        readonly bool idle;
        float pauseUntil;
        float chatUntil;
        Unit chatPartner;
        bool walking;

        public RecreationJob(Vector2 center, float radius, bool idle = false)
        {
            this.center = center;
            this.radius = radius;
            this.idle = idle;
        }

        public override string Label => idle ? "Idle" : chatPartner != null && Time.time < chatUntil ? "Chatting" : "Relaxing";
        public bool Chatting => chatPartner != null && Time.time < chatUntil;

        public override void Begin(Unit unit) => unit.ClearWorkAnimation();

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (Chatting)
            {
                if (chatPartner != null) unit.Face(chatPartner.transform.position - unit.transform.position);
                return JobState.Running;
            }
            chatPartner = null;
            if (walking)
            {
                if (unit.IsMoving) return JobState.Running;
                walking = false;
                pauseUntil = Time.time + Random.Range(2f, 5f);
                if (!idle) TryChat(unit);
                return JobState.Running;
            }
            if (Time.time < pauseUntil) return JobState.Running;
            var map = MapData.Instance;
            for (int i = 0; i < 6; i++)
            {
                var p = center + Random.insideUnitCircle * radius;
                if (map != null && map.IsReady && !map.IsWalkable(MapData.WorldToCell(p))) continue;
                unit.MoveTo(p);
                walking = true;
                break;
            }
            if (!walking) pauseUntil = Time.time + 2f;
            return JobState.Running;
        }

        void TryChat(Unit unit)
        {
            foreach (var other in Unit.All)
            {
                if (other == null || other == unit || other.IsHidden || other.Worker == null) continue;
                if (!(other.Worker.Current is RecreationJob job) || job.idle || job.Chatting) continue;
                if (Vector2.Distance(other.transform.position, unit.transform.position) > 3f) continue;
                float seconds = CharacterSettings.Current.chatSeconds;
                chatPartner = other;
                chatUntil = Time.time + seconds;
                job.chatPartner = unit;
                job.chatUntil = chatUntil;
                job.walking = false;
                other.Stop();
                var icon = CharacterSettings.Current.recreationIcon;
                FloatingText.Show(unit.transform, "...", Color.white, icon);
                FloatingText.Show(other.transform, "...", Color.white, icon);
                return;
            }
        }
    }

    /// <summary>
    /// Soldiers walk a ring around the settlement and watch out for enemies (attacking any hostile within the
    /// detection radius; there are no enemies in the game yet).
    /// </summary>
    public sealed class PatrolJob : UnitJob
    {
        readonly Vector2 center;
        float pauseUntil;
        bool walking;
        AttackJob fight;

        /// <summary>Hostile things soldiers attack on sight (filled when enemies exist).</summary>
        public static readonly List<ICombatTarget> Hostiles = new List<ICombatTarget>();

        public PatrolJob(Vector2 center) => this.center = center;

        public override string Label => fight != null ? fight.Label : "Patrolling";

        public override void Begin(Unit unit) => unit.ClearWorkAnimation();

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (fight != null)
            {
                var state = fight.Tick(unit, deltaTime);
                if (state == JobState.Running) return JobState.Running;
                fight = null;
            }
            var c = CharacterSettings.Current;
            var enemy = NearestHostile(unit, c.detectRadius);
            if (enemy != null)
            {
                fight = new AttackJob(new List<ICombatTarget> { enemy });
                fight.Begin(unit);
                return JobState.Running;
            }
            if (walking)
            {
                if (unit.IsMoving) return JobState.Running;
                walking = false;
                pauseUntil = Time.time + c.patrolPause;
            }
            if (Time.time < pauseUntil) return JobState.Running;
            var map = MapData.Instance;
            for (int i = 0; i < 8; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(c.patrolRadiusMin, c.patrolRadiusMax);
                if (map != null && map.IsReady && !map.IsWalkable(MapData.WorldToCell(p))) continue;
                unit.MoveTo(p);
                walking = true;
                break;
            }
            return JobState.Running;
        }

        static ICombatTarget NearestHostile(Unit unit, float radius)
        {
            Hostiles.RemoveAll(h => !CombatTargets.Valid(h));
            ICombatTarget best = null;
            float bestDistance = radius;
            foreach (var h in Hostiles)
            {
                float d = Vector2.Distance(h.GroundPoint, unit.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = h;
                }
            }
            return best;
        }

        public override void Cancel(Unit unit) => fight?.Cancel(unit);
    }

    /// <summary>Walks to the church (the hospital after an army training) and pays for healing: 40 coins, +20 health, once a day.</summary>
    public sealed class HospitalJob : UnitJob
    {
        readonly Building church;
        readonly Approach approach = new Approach();

        public HospitalJob(Building church) => this.church = church;

        public override string Label => "Going to the church to be healed";
        public override object TargetObject => church;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.ClearWorkAnimation();
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            var v = unit.Villager;
            if (church == null || !church.IsUsable || v == null) return JobState.Failed;
            var door = church.DoorPoint;
            if (!approach.Tick(unit, new Rect(door.x - 0.3f, door.y - 0.3f, 0.6f, 0.6f), 0.5f, out bool failed))
                return failed ? JobState.Failed : JobState.Running;
            if (church.TryChurchHeal(v, out string reason)) return JobState.Done;
            Debug.Log($"[Church] {v.DisplayName} could not be healed: {reason}.", unit);
            return JobState.Failed;
        }
    }

    /// <summary>
    /// A priest heals an injured character: every couple of seconds a wave of healing = the healing level; Prayer
    /// (5) heals the same again over 5 s, Holy Halo (10) heals everybody in the radius, Purification (15) removes
    /// status effects.
    /// </summary>
    public sealed class PriestHealJob : UnitJob
    {
        readonly Villager patient;
        readonly Approach approach = new Approach();
        float nextWave;
        float giveUpAt;

        public PriestHealJob(Villager patient) => this.patient = patient;

        public override string Label => $"Healing {(patient != null ? patient.DisplayName : "?")}";
        public override object TargetObject => patient;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            giveUpAt = Time.time + 40f;
            unit.SetWorkAnimation(InteractionTool.Heal, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            var priest = unit.Villager;
            if (priest == null || patient == null || patient.Movement == null || patient.Movement.IsHidden) return JobState.Failed;
            if (patient.Needs.Health.current >= patient.Needs.Health.max - 0.5f) return Finish(unit);
            if (Time.time > giveUpAt) return Finish(unit);
            float reach = Mathf.Max(1f, StatEffects.HealingRadius(priest));
            float distance = Vector2.Distance(unit.transform.position, patient.transform.position);
            if (distance > reach)
            {
                unit.SetWorkAnimation(InteractionTool.Heal, false);
                if (!unit.IsMoving || Time.time > nextWave) unit.MoveTo(patient.transform.position);
                nextWave = Time.time + 0.5f;
                return JobState.Running;
            }
            if (unit.IsMoving) unit.Stop();
            unit.SetWorkAnimation(InteractionTool.Heal, true);
            unit.Face(patient.transform.position - unit.transform.position);
            if (Time.time < nextWave) return JobState.Running;
            var s = StatSettings.Current;
            nextWave = Time.time + s.healingInterval;
            unit.PlaySwing(Mathf.Min(1f, s.healingInterval * 0.8f));
            float amount = Mathf.Max(1, priest.Stats.Get(StatType.Healing));
            var healed = new List<Villager> { patient };
            if (priest.Stats.Has(StatType.Healing, 10))
                foreach (var other in Villager.All)
                    if (other != null && other != patient && !other.Movement.IsHidden &&
                        Vector2.Distance(other.transform.position, unit.transform.position) <= StatEffects.HealingRadius(priest))
                        healed.Add(other);
            foreach (var h in healed)
            {
                h.Heal(amount, priest);
                if (priest.Stats.Has(StatType.Healing, 5)) h.AddHealOverTime(amount, s.prayerSeconds);
                if (priest.Stats.Has(StatType.Healing, 15)) h.Status.ClearAll();
                HealSparkles.Burst(new Rect(h.transform.position.x - 0.4f, h.transform.position.y, 0.8f, 0.9f), new Color(0.5f, 1f, 0.6f));
            }
            return JobState.Running;
        }

        JobState Finish(Unit unit)
        {
            unit.SetWorkAnimation(InteractionTool.Heal, false);
            return JobState.Done;
        }
    }

    /// <summary>Holy Halo (Healing 10): a priest makes a stump grow back (100%).</summary>
    public sealed class RegrowStumpJob : UnitJob
    {
        readonly StumpObject stump;
        readonly Approach approach = new Approach();

        public RegrowStumpJob(StumpObject stump) => this.stump = stump;

        public override string Label => "Making a stump grow back";
        public override object TargetObject => stump;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.SetWorkAnimation(InteractionTool.Heal, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (stump == null || stump.IsRegrowing) return JobState.Done;
            if (!approach.Tick(unit, stump.Hitbox, 1.2f, out bool failed, stump)) return failed ? JobState.Failed : JobState.Running;
            unit.SetWorkAnimation(InteractionTool.Heal, true);
            unit.PlaySwing(1f);
            stump.BeginRegrow(StatSettings.Current.regrowHours, 0f);
            unit.Villager?.GainXp(StatType.Healing, 5f);
            return JobState.Done;
        }
    }

    /// <summary>
    /// A baker runs the furnace: brings the inputs of a recipe from the stores, works the furnace (it only runs with
    /// a baker), and carries the gold coins / meals to a store when the furnace is full or nothing is left to make.
    /// Cooks when the stores hold fewer meals than CharacterSettings.foodStockTarget, otherwise makes coins.
    /// </summary>
    public sealed class FurnaceJob : UnitJob
    {
        readonly Building furnace;
        readonly Approach approach = new Approach();
        UnitJob step;
        int recipe = -1;
        int failures;
        float idleSince = -1f;

        public FurnaceJob(Building furnace) => this.furnace = furnace;

        public override string Label => recipe >= 0 && furnace != null && furnace.Info != null && recipe < furnace.Info.Recipes.Count
            ? $"Furnace: {furnace.Info.Recipes[recipe].name}" : "Working the furnace";
        public override object TargetObject => furnace;
        public override bool KeepsLoot => true;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.SetWorkAnimation(InteractionTool.Hammer, false);
        }

        /// <summary>Recipe to make next (-1 = nothing possible).</summary>
        public static int ChooseRecipe(Building furnace, Villager baker)
        {
            if (furnace == null || furnace.Info == null || !furnace.IsUsable) return -1;
            var recipes = furnace.Info.Recipes;
            int meals = 0;
            foreach (var pair in Building.TreasuryContents()) if (pair.Key != null && pair.Key.IsFood && !pair.Key.IsRawFood) meals += pair.Value;
            bool wantFood = meals < CharacterSettings.Current.foodStockTarget;
            int fallback = -1;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (!Available(furnace, recipes[i], baker)) continue;
                if (recipes[i].cooking == wantFood) return i;
                if (fallback < 0) fallback = i;
            }
            return fallback;
        }

        /// <summary>The inputs are in the furnace or in the stores.</summary>
        static bool Available(Building furnace, FurnaceRecipe r, Villager baker)
        {
            if (r == null || r.output == null) return false;
            foreach (var i in r.inputs)
            {
                if (i == null || i.item == null) continue;
                int need = Building.InputAmount(r, i, baker);
                if (furnace.Inventory.Count(i.item) + Building.TreasuryCount(i.item) < need) return false;
            }
            return true;
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            var v = unit.Villager;
            if (furnace == null || !furnace.IsUsable || v == null) return JobState.Failed;
            if (step != null)
            {
                var state = step.Tick(unit, deltaTime);
                if (state == JobState.Running) return JobState.Running;
                step = null;
                if (state == JobState.Failed && ++failures > 6) return JobState.Failed;
            }
            if (recipe < 0 || !Available(furnace, furnace.Info.Recipes[recipe], v)) recipe = ChooseRecipe(furnace, v);
            var info = furnace.Info;
            // Full of outputs or nothing more to make: carry the outputs out
            bool nothing = recipe < 0;
            var outputs = OutputsInside();
            if (outputs != null && (nothing || furnace.Inventory.SpaceFor(outputs) <= 1 || furnace.Inventory.Count(outputs) >= outputs.StackSize))
            {
                var store = NearestStore(unit, outputs);
                if (store == null) return JobState.Failed;
                step = new DeliverJob(furnace, store, outputs, outputs.StackSize);
                step.Begin(unit);
                return JobState.Running;
            }
            if (nothing) return JobState.Done;
            var r = info.Recipes[recipe];
            // Bring a missing input from the stores
            foreach (var i in r.inputs)
            {
                if (i == null || i.item == null) continue;
                int need = Building.InputAmount(r, i, v);
                if (furnace.Inventory.Count(i.item) >= need) continue;
                var source = NearestStoreWith(unit, i.item);
                if (source == null) return JobState.Failed;
                step = new DeliverJob(source, furnace, i.item, Mathf.Max(need * 4, need));
                step.Begin(unit);
                return JobState.Running;
            }
            // Work the furnace
            if (!approach.TickWork(unit, furnace.WorkBox, out bool failed, furnace))
            {
                unit.SetWorkAnimation(InteractionTool.Hammer, false);
                return failed ? JobState.Failed : JobState.Running;
            }
            unit.SetWorkAnimation(InteractionTool.Hammer, true);
            if (idleSince < 0f || Time.time - idleSince > 1.2f)
            {
                idleSince = Time.time;
                unit.PlaySwing(1f);
            }
            if (!furnace.CanRun(r, v))
            {
                recipe = -1;
                return JobState.Running;
            }
            furnace.OperateFurnace(recipe, v, deltaTime);
            return JobState.Running;
        }

        SpawnableDefinition OutputsInside()
        {
            foreach (var r in furnace.Info.Recipes)
            {
                if (r == null) continue;
                var c = CharacterSettings.Current;
                foreach (var item in new[] { r.output, r.cooking ? c.feastMeat : null, r.cooking ? c.masterMeat : null })
                    if (item != null && furnace.Inventory.Count(item) > 0) return item;
            }
            return null;
        }

        static Building NearestStore(Unit unit, SpawnableDefinition item)
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || !b.IsPublicStore || b.SpaceFor(item) <= 0) continue;
                float d = Vector2.Distance(unit.transform.position, b.GroundPoint);
                if (d < bestDistance) { bestDistance = d; best = b; }
            }
            return best;
        }

        static Building NearestStoreWith(Unit unit, SpawnableDefinition item)
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || !b.IsPublicStore || b.Inventory.Count(item) <= 0) continue;
                float d = Vector2.Distance(unit.transform.position, b.GroundPoint);
                if (d < bestDistance) { bestDistance = d; best = b; }
            }
            return best;
        }

        public override void Cancel(Unit unit)
        {
            step?.Cancel(unit);
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }

    /// <summary>Carries items from one building to another (store -> furnace, furnace -> store, store -> training storage).</summary>
    public sealed class DeliverJob : UnitJob
    {
        readonly Building from, to;
        readonly SpawnableDefinition item;
        readonly int amount;
        UnitJob step;
        int phase;

        public DeliverJob(Building from, Building to, SpawnableDefinition item, int amount)
        {
            this.from = from;
            this.to = to;
            this.item = item;
            this.amount = amount;
        }

        public override string Label => $"Carrying {(item != null ? item.DisplayName : "?")} to {(to != null ? to.DisplayName : "?")}";
        public override object TargetObject => to;
        public override bool KeepsLoot => true;

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (from == null || to == null || item == null) return JobState.Failed;
            if (step == null)
            {
                var held = unit.Carrier != null ? unit.Carrier.Held : null;
                if (phase == 0 && held != null && StockLootJob.StoredItemOf(held.Definition) == item) phase = 1;
                if (phase == 0)
                {
                    if (held != null) unit.Carrier.DropHere();
                    step = new WithdrawJob(from, item, amount);
                }
                else step = new StockLootJob(to);
                step.Begin(unit);
            }
            var state = step.Tick(unit, deltaTime);
            if (state == JobState.Running) return JobState.Running;
            step = null;
            if (state == JobState.Failed) return JobState.Failed;
            if (phase == 0)
            {
                phase = 1;
                return JobState.Running;
            }
            return JobState.Done;
        }

        public override void Cancel(Unit unit) => step?.Cancel(unit);
    }

    /// <summary>Hauling: takes a pile from the ground and stocks it in the nearest store with room.</summary>
    public sealed class HaulJob : UnitJob
    {
        readonly WorldObject pile;
        UnitJob step;
        bool carrying;

        public HaulJob(WorldObject pile) => this.pile = pile;

        public override string Label => $"Hauling {(pile != null ? pile.DisplayName : "?")}";
        public override object TargetObject => pile;
        public override bool KeepsLoot => true;

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (step == null)
            {
                if (!carrying)
                {
                    if (pile == null || pile.IsCarried) return JobState.Failed;
                    step = new CarryLootJob(pile);
                }
                else
                {
                    var held = unit.Carrier != null ? unit.Carrier.Held : null;
                    if (held == null) return JobState.Done;
                    var item = StockLootJob.StoredItemOf(held.Definition);
                    Building best = null;
                    float bestDistance = float.MaxValue;
                    foreach (var b in Building.All)
                    {
                        if (b == null || !b.IsPublicStore || b.SpaceFor(item) <= 0) continue;
                        float d = Vector2.Distance(unit.transform.position, b.GroundPoint);
                        if (d < bestDistance) { bestDistance = d; best = b; }
                    }
                    if (best == null)
                    {
                        unit.Carrier.DropHere();
                        return JobState.Failed;
                    }
                    step = new StockLootJob(best);
                }
                step.Begin(unit);
            }
            var state = step.Tick(unit, deltaTime);
            if (state == JobState.Running) return JobState.Running;
            step = null;
            if (state == JobState.Failed)
            {
                if (carrying && unit.Carrier != null && unit.Carrier.IsHolding) unit.Carrier.DropHere();
                return JobState.Failed;
            }
            if (!carrying)
            {
                carrying = true;
                return JobState.Running;
            }
            return JobState.Done;
        }

        public override void Cancel(Unit unit)
        {
            step?.Cancel(unit);
            if (unit.Carrier != null && unit.Carrier.IsHolding) unit.Carrier.DropHere();
        }
    }

    /// <summary>Shepherd (Hunting 5): walks to a herd, then to a spot; the sheep around follow and stay there.</summary>
    public sealed class HerdJob : UnitJob
    {
        readonly Sheep sheep;
        readonly Vector2 destination;
        bool leading;

        public HerdJob(Sheep sheep, Vector2 destination)
        {
            this.sheep = sheep;
            this.destination = destination;
        }

        public override string Label => "Herding sheep";

        public override void Begin(Unit unit) => unit.ClearWorkAnimation();

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (sheep == null) return JobState.Failed;
            if (!leading)
            {
                if (Vector2.Distance(unit.transform.position, sheep.transform.position) > 1.5f)
                {
                    if (!unit.IsMoving) unit.MoveTo(sheep.transform.position);
                    return JobState.Running;
                }
                leading = true;
                foreach (var s in Sheep.All)
                    if (s != null && s.Herd == sheep.Herd && Vector2.Distance(s.transform.position, sheep.transform.position) < 5f)
                        s.Follow(unit, destination);
                unit.MoveTo(destination);
                return JobState.Running;
            }
            return unit.IsMoving ? JobState.Running : JobState.Done;
        }
    }
}
