using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Build or repair a building: walk to a spot beside it and hammer (Pawn_Interact Hammer, the constructing
    /// animation) until it has full health. Every swing adds GameplaySettings.buildPowerPerHit health.
    /// </summary>
    public sealed class BuildJob : UnitJob
    {
        readonly Building target;
        readonly Approach approach = new Approach();
        float swingTime;
        bool swinging, impactDone;

        public BuildJob(Building target) => this.target = target;

        public override string Label => $"{(target != null && target.IsComplete ? "Repair" : "Build")} {(target != null ? target.DisplayName : "?")}";
        public Building Target => target;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            swinging = false;
            unit.SetWorkAnimation(InteractionTool.Hammer, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null || target.IsDepleted) return JobState.Failed;
            if (!target.NeedsWork) return Finish(unit);
            if (!target.MaterialsComplete)
            {
                Debug.Log($"[Buildings] {target.Definition?.DisplayName} still needs materials: {target.MaterialsText()}. Use Supply materials first.", target);
                return JobState.Failed;
            }

            var box = target.WorkBox;
            if (!approach.TickWork(unit, box, out bool failed, target))
            {
                unit.SetWorkAnimation(InteractionTool.Hammer, false);
                if (failed && target.Info != null && target.Info.isBridge)
                    Debug.Log($"[Buildings] {unit.name} cannot reach {target.DisplayName} from dry land (needs the shore or a finished bridge at an end).", unit);
                swinging = false;
                return failed ? JobState.Failed : JobState.Running;
            }

            var map = MapData.Instance;
            var gameplay = map != null ? map.Gameplay : GameplaySettings.Fallback;
            var interaction = map != null ? map.Interaction : InteractionSettings.Fallback;
            float interval = gameplay.buildInterval;
            unit.SetWorkAnimation(InteractionTool.Hammer, true);
            unit.Face(box.center - (Vector2)unit.transform.position);
            if (!swinging)
            {
                swinging = true;
                impactDone = false;
                swingTime = 0f;
                unit.PlaySwing(interval);
            }
            swingTime += deltaTime;
            if (!impactDone && swingTime >= interval * interaction.hitImpactPoint)
            {
                impactDone = true;
                target.Build(gameplay.buildPowerPerHit);
            }
            if (swingTime >= interval) swinging = false;
            return target.NeedsWork ? JobState.Running : Finish(unit);
        }

        JobState Finish(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(InteractionTool.Hammer, false);
            return JobState.Done;
        }

        public override void Cancel(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }

    /// <summary>Walk to the door of a building and go inside, or climb a tower.</summary>
    public sealed class EnterBuildingJob : UnitJob
    {
        readonly Building target;
        readonly bool climb;
        readonly Approach approach = new Approach();

        public EnterBuildingJob(Building target, bool climb)
        {
            this.target = target;
            this.climb = climb;
        }

        public override string Label => $"{(climb ? "Climb" : "Enter")} {(target != null ? target.DisplayName : "?")}";

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.ClearWorkAnimation();
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null) return JobState.Failed;
            string reason;
            bool allowed = climb ? target.CanClimb(unit, out reason) : target.Accepts(unit, out reason);
            if (!allowed)
            {
                Debug.Log($"[Buildings] {unit.name} cannot {(climb ? "climb" : "enter")} {target.DisplayName}: {reason}", unit);
                return JobState.Failed;
            }
            var door = target.DoorPoint;
            var area = new Rect(door.x - 0.3f, door.y - 0.3f, 0.6f, 0.6f);
            if (!approach.Tick(unit, area, 0.35f, out bool failed)) return failed ? JobState.Failed : JobState.Running;
            bool done = climb ? target.Climb(unit) : target.Enter(unit);
            return done ? JobState.Done : JobState.Failed;
        }
    }

    /// <summary>
    /// "Carry loot": walk to a pile and take it in the hands (it stays there while the character is moved). If the
    /// character already carries the same loot, the pile is added to it.
    /// </summary>
    public sealed class CarryLootJob : UnitJob
    {
        readonly WorldObject pile;
        readonly Approach approach = new Approach();

        public CarryLootJob(WorldObject pile) => this.pile = pile;

        public override string Label => $"Carry loot {(pile != null ? pile.DisplayName : "?")}";
        public override bool KeepsLoot => true;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            unit.ClearWorkAnimation();
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (pile == null || pile.IsCarried) return JobState.Failed;
            var area = new Rect(pile.Area.position, pile.Area.size);
            if (!approach.Tick(unit, area, Approach.Reach(unit), out bool failed, pile))
                return failed ? JobState.Failed : JobState.Running;
            var held = unit.Carrier.Held;
            if (held != null && SameLoot(held, pile))
            {
                Merge(unit, held, pile);
                return JobState.Done;
            }
            unit.Carrier.Hold(pile);
            return JobState.Done;
        }

        static bool SameLoot(WorldObject a, WorldObject b) =>
            a != null && b != null && StockLootJob.StoredItemOf(a.Definition) == StockLootJob.StoredItemOf(b.Definition);

        /// <summary>
        /// The same loot is already in the hands: the pile on the ground is added to it (2 wood in the hands + 4 on the
        /// ground = 6 in the hands), up to the item's pile size; what does not fit stays on the ground. The piles are
        /// changed while they lie on the map, so every change is recorded in the edited map.
        /// </summary>
        static void Merge(Unit unit, WorldObject held, WorldObject pile)
        {
            int stack = pile.Definition != null ? pile.Definition.StackSize : 10;
            int total = held.Amount + pile.Amount;
            int carried = Mathf.Min(total, Mathf.Max(stack, held.Amount));
            int left = total - carried;
            // The hands' pile goes onto the map first (recorded), then the amounts move
            var released = unit.Carrier.Release();
            LootCarrier.PutDownNear(released, unit.transform.position);
            if (carried != pile.Amount) pile.AddAmount(carried - pile.Amount);
            if (left > 0) released.AddAmount(left - released.Amount);
            else released.Remove();
            unit.Carrier.Hold(pile);
            if (left > 0) Debug.Log($"[Loot] {unit.name} carries {carried} {pile.Definition?.DisplayName}; {left} did not fit (pile size {stack}).", unit);
        }
    }

    /// <summary>"Stock loot": bring the loot in the hands to a building and put it into its inventory.</summary>
    public sealed class StockLootJob : UnitJob
    {
        readonly Building target;
        readonly Approach approach = new Approach();

        public StockLootJob(Building target) => this.target = target;

        public override string Label => $"Stock loot in {(target != null ? target.DisplayName : "?")}";
        public override bool KeepsLoot => true;

        public override void Begin(Unit unit) => approach.Reset();

        /// <summary>The inventory item a pile stands for (a shining gold pile is plain gold...).</summary>
        public static SpawnableDefinition StoredItemOf(SpawnableDefinition pile)
        {
            if (pile == null) return null;
            var drops = pile.Drops;
            if (pile.IsCollectable && drops != null && drops.Count == 1 && drops[0] != null && drops[0].item != null && drops[0].chance >= 1f)
                return drops[0].item;
            return pile;
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            var pile = unit.Carrier != null ? unit.Carrier.Held : null;
            if (pile == null || target == null) return JobState.Failed;
            var item = StoredItemOf(pile.Definition);
            if (target.SpaceFor(item) <= 0)
            {
                Debug.Log($"[Buildings] {target.DisplayName} has no room for {item?.DisplayName}.", target);
                return JobState.Failed;
            }
            if (!approach.TickWork(unit, target.WorkBox, out bool failed, target))
                return failed ? JobState.Failed : JobState.Running;
            Approach.Release(unit);

            int amount = pile.Amount;
            int added = target.Stock(item, amount);
            if (added <= 0) return JobState.Failed;
            // Put the pile on the map first, so taking it away is recorded like every other change
            var released = unit.Carrier.Release();
            LootCarrier.PutDownNear(released, unit.transform.position);
            if (added >= amount) released.Remove();
            else
            {
                released.AddAmount(-added);
                unit.Carrier.Hold(released);
            }
            Debug.Log($"[Buildings] {added} {item.DisplayName} stocked in {target.DisplayName}.", target);
            return JobState.Done;
        }

        public override void Cancel(Unit unit) => Approach.Release(unit);
    }

    /// <summary>Take items out of a building (gold coins from the furnace...) and carry them as loot.</summary>
    public sealed class WithdrawJob : UnitJob
    {
        readonly Building target;
        readonly SpawnableDefinition item;
        readonly int maxAmount;
        readonly Approach approach = new Approach();

        /// <param name="maxAmount">At most this many (0 = one full pile).</param>
        public WithdrawJob(Building target, SpawnableDefinition item, int maxAmount = 0)
        {
            this.target = target;
            this.item = item;
            this.maxAmount = maxAmount;
        }

        public override string Label => $"Take {(item != null ? item.DisplayName : "?")} from {(target != null ? target.DisplayName : "?")}";
        public override bool KeepsLoot => false;

        public override void Begin(Unit unit) => approach.Reset();

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null || item == null || target.Inventory.Count(item) <= 0) return JobState.Failed;
            if (!approach.TickWork(unit, target.WorkBox, out bool failed, target))
                return failed ? JobState.Failed : JobState.Running;
            Approach.Release(unit);
            var map = MapData.Instance;
            if (map == null) return JobState.Failed;
            var cell = MapData.WorldToCell(unit.transform.position);
            if (map.IsAreaBlocked(new RectInt(cell, item.Footprint)) &&
                !ItemDropper.FindFreeCell(map, new RectInt(cell, item.Footprint), Mathf.Max(1, map.Interaction.dropFallbackRadius), out cell))
                return JobState.Failed;
            int taken = target.Withdraw(item, maxAmount > 0 ? Mathf.Min(maxAmount, item.StackSize) : item.StackSize);
            if (taken <= 0) return JobState.Failed;
            var pile = WorldObjectFactory.Create(item, cell, Vector2.zero, target.transform.parent, map, configure: o => o.Amount = taken);
            if (pile == null) return JobState.Failed;
            unit.Carrier.Hold(pile);
            return JobState.Done;
        }

        public override void Cancel(Unit unit) => Approach.Release(unit);
    }

    /// <summary>
    /// "Supply materials": a villager brings the materials a template still needs, one load after another. Each
    /// load comes from the nearest source of a missing item: a pile on the ground, or a store (castle, house).
    /// Loot already in the hands is delivered first if it is needed. Ends when everything has arrived (then the
    /// construction can start with Build) or when nothing needed can be found.
    /// </summary>
    public sealed class SupplyJob : UnitJob
    {
        const int SearchRadius = 40;
        readonly Building site;
        UnitJob step;
        int failures;
        static readonly List<WorldObject> Nearby = new List<WorldObject>();

        public SupplyJob(Building site) => this.site = site;

        public override string Label => $"Supply {(site != null ? site.Definition?.DisplayName : "?")} ({(site != null ? site.MaterialsText() : "")})";
        public override bool KeepsLoot => true;

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (site == null || !site.RequiresMaterials) return End(unit, JobState.Failed);
            if (site.MaterialsComplete) return End(unit, JobState.Done);
            if (step == null)
            {
                step = NextStep(unit);
                if (step == null)
                {
                    Debug.Log($"[Buildings] {unit.name}: no more needed materials found for {site.Definition?.DisplayName} ({site.MaterialsText()}).", unit);
                    return End(unit, JobState.Failed);
                }
                step.Begin(unit);
            }
            var state = step.Tick(unit, deltaTime);
            if (state == JobState.Running) return JobState.Running;
            step = null;
            if (state == JobState.Failed && ++failures > 6) return End(unit, JobState.Failed);
            return JobState.Running;
        }

        JobState End(Unit unit, JobState state)
        {
            // Loot that is not needed any more goes down at the site
            var held = unit.Carrier != null ? unit.Carrier.Held : null;
            if (held != null && site != null && site.Missing(StockLootJob.StoredItemOf(held.Definition)) <= 0) unit.Carrier.DropHere();
            return state;
        }

        UnitJob NextStep(Unit unit)
        {
            var held = unit.Carrier != null ? unit.Carrier.Held : null;
            if (held != null)
            {
                if (site.Missing(StockLootJob.StoredItemOf(held.Definition)) > 0) return new StockLootJob(site);
                unit.Carrier.DropHere(); // something else in the hands
            }
            var map = MapData.Instance;
            if (map == null) return null;
            Vector2 from = unit.transform.position;
            UnitJob best = null;
            float bestDistance = float.MaxValue;
            foreach (var need in site.MissingMaterials())
            {
                // Piles on the ground around the site and the unit
                Nearby.Clear();
                var center = MapData.WorldToCell(site.GroundPoint);
                map.CollectObjects(new RectInt(center.x - SearchRadius, center.y - SearchRadius, SearchRadius * 2, SearchRadius * 2), Nearby);
                foreach (var o in Nearby)
                {
                    if (o == null || o.IsCarried || !(o is ResourceNode node) || !node.IsCollectable || !o.MovableByCharacters) continue;
                    if (StockLootJob.StoredItemOf(o.Definition) != need.item) continue;
                    float d = Vector2.Distance(from, o.GroundPoint);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = new CarryLootJob(o);
                    }
                }
                // Stores
                foreach (var b in Building.All)
                {
                    if (b == null || b == site || !b.IsUsable || b.Info == null || !b.Info.HasInventory || b.Info.IsFurnace) continue;
                    if (b.Inventory.Count(need.item) <= 0) continue;
                    float d = Vector2.Distance(from, b.GroundPoint);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = new WithdrawJob(b, need.item, need.amount);
                    }
                }
            }
            return best;
        }

        public override void Cancel(Unit unit)
        {
            step?.Cancel(unit);
            Approach.Release(unit);
        }
    }
}
