using System.Collections.Generic;
using KingOfNothing.Animals;
using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>The settlement: the castle (or the start of the map) and the area around it.</summary>
    public static class Settlement
    {
        public static Vector2 Center
        {
            get
            {
                foreach (var b in Building.All)
                    if (b != null && b.Type == BuildingType.Castle && !b.IsDepleted) return b.DoorPoint;
                var spawner = StartingPartySpawner.Instance;
                if (spawner != null && spawner.Generator != null && spawner.Generator.StartCell.x >= 0)
                    return MapData.CellCenter(spawner.Generator.StartCell);
                return Vector2.zero;
            }
        }

        public static bool Contains(Vector2 point) =>
            (point - Center).sqrMagnitude <= CharacterSettings.Current.settlementRadius * (float)CharacterSettings.Current.settlementRadius;

        /// <summary>Where characters relax: the church if there is one, else the castle / start.</summary>
        public static Vector2 RecreationCenter
        {
            get
            {
                foreach (var b in Building.All)
                    if (b != null && b.Type == BuildingType.Church && b.IsUsable) return b.DoorPoint;
                return Center;
            }
        }
    }

    /// <summary>Which character works on what (so two do not go for the same tree), and targets that failed recently.</summary>
    public static class JobTargets
    {
        static readonly Dictionary<object, float> Failed = new Dictionary<object, float>();

        public static int Count(object target, Unit except)
        {
            if (target == null) return 0;
            int n = 0;
            foreach (var u in Unit.All)
                if (u != null && u != except && u.Worker != null && u.Worker.Current != null && ReferenceEquals(u.Worker.Current.TargetObject, target)) n++;
            return n;
        }

        public static bool Taken(object target, Unit except) => Count(target, except) > 0;

        public static void MarkFailed(object target, float seconds = 60f)
        {
            if (target != null) Failed[target] = Time.time + seconds;
        }

        public static bool RecentlyFailed(object target) => target != null && Failed.TryGetValue(target, out float until) && Time.time < until;
    }

    /// <summary>
    /// Finds the next job of a work type for a character (Work panel, RimWorld style): building / repairing,
    /// chopping / mining / harvesting marked objects, gold mines, hunting marked sheep, the furnace, hauling and
    /// supplying, and a priest's healing. Nearest first.
    /// </summary>
    public static class WorkGivers
    {
        static CharacterSettings C => CharacterSettings.Current;

        public static UnitJob Find(Villager v, StatType work)
        {
            if (v == null || !WorkTypes.CanDo(v, work)) return null;
            switch (work)
            {
                case StatType.Building: return Build(v);
                case StatType.Woodcutting: return Designated(v, DesignationType.Chop, "Chop");
                case StatType.Mining: return Designated(v, DesignationType.Mine, "Mine") ?? GoldMine(v);
                case StatType.Farming: return Designated(v, DesignationType.Harvest, "Harvest");
                case StatType.Hunting: return Hunt(v);
                case StatType.Baking: return Furnace(v);
                case StatType.Efficiency: return Haul(v);
                case StatType.Healing: return Heal(v);
                default: return null;
            }
        }

        static float Distance(Villager v, Vector2 p) => Vector2.Distance(v.transform.position, p);

        static UnitJob Build(Villager v)
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            var c = C;
            foreach (var b in Building.All)
            {
                if (b == null || b.IsDepleted || !b.NeedsWork || JobTargets.RecentlyFailed(b)) continue;
                bool site = !b.IsComplete;
                if (site)
                {
                    if (!b.MaterialsComplete) continue;
                }
                else
                {
                    // Repairs only in the settlement, below the repair line or destroyed
                    if (!Settlement.Contains(b.GroundPoint)) continue;
                    if (!b.LooksDestroyed && b.Health >= b.MaxHealth * c.repairBelow) continue;
                }
                if (JobTargets.Count(b, v.Movement) >= c.maxBuildersPerSite) continue;
                float d = Distance(v, b.GroundPoint) + (site ? 0f : 5f);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = b;
                }
            }
            return best != null ? new BuildJob(best) : null;
        }

        static UnitJob Designated(Villager v, DesignationType type, string verb)
        {
            ResourceNode best = null;
            float bestDistance = float.MaxValue;
            foreach (var o in Designations.Marked(type))
            {
                if (!(o is ResourceNode node) || !node.CanBeHit || node.IsCarried || JobTargets.RecentlyFailed(node)) continue;
                if (JobTargets.Taken(node, v.Movement) || !HitObjectJob.CanWork(v.Movement, node)) continue;
                float d = Distance(v, node.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = node;
                }
            }
            return best != null ? new HitObjectJob(best, verb) : null;
        }

        static UnitJob GoldMine(Villager v)
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || b.Info == null || !b.Info.IsMine || !b.IsUsable || b.MineExhausted || JobTargets.RecentlyFailed(b)) continue;
                if (!b.Accepts(v.Movement, out _)) continue;
                float d = Distance(v, b.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = b;
                }
            }
            return best != null ? new EnterBuildingJob(best, false) : null;
        }

        static UnitJob Hunt(Villager v)
        {
            Sheep best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in Sheep.All)
            {
                if (s == null || !s.IsAlive || !s.MarkedForHunt || JobTargets.Taken(s, v.Movement) || JobTargets.RecentlyFailed(s)) continue;
                float d = Distance(v, s.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = s;
                }
            }
            return best != null ? new AttackJob(new List<ICombatTarget> { best }) : null;
        }

        static UnitJob Furnace(Villager v)
        {
            foreach (var b in Building.All)
            {
                if (b == null || b.Info == null || !b.Info.IsFurnace || !b.IsUsable || JobTargets.Taken(b, v.Movement) || JobTargets.RecentlyFailed(b)) continue;
                if (FurnaceJob.ChooseRecipe(b, v) < 0 && !HasOutputs(b)) continue;
                return new FurnaceJob(b);
            }
            return null;
        }

        static bool HasOutputs(Building furnace)
        {
            foreach (var r in furnace.Info.Recipes)
                if (r != null && r.output != null && furnace.Inventory.Count(r.output) > 0) return true;
            var c = C;
            return (c.feastMeat != null && furnace.Inventory.Count(c.feastMeat) > 0) || (c.masterMeat != null && furnace.Inventory.Count(c.masterMeat) > 0);
        }

        static readonly List<WorldObject> Nearby = new List<WorldObject>();

        static UnitJob Haul(Villager v)
        {
            // 1. Templates waiting for materials that exist somewhere
            Building site = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || !b.RequiresMaterials || b.MaterialsComplete || JobTargets.RecentlyFailed(b)) continue;
                if (JobTargets.Count(b, v.Movement) >= 2) continue;
                bool available = false;
                foreach (var need in b.MissingMaterials())
                    if (Building.TreasuryCount(need.item) > 0 || PileExists(need.item)) { available = true; break; }
                if (!available) continue;
                float d = Distance(v, b.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    site = b;
                }
            }
            if (site != null) return new SupplyJob(site);

            // 2. Training storages that asked for materials
            foreach (var b in Building.All)
            {
                if (b == null || !b.IsUsable || b.Requests.Count == 0 || JobTargets.Taken(b, v.Movement) || JobTargets.RecentlyFailed(b)) continue;
                foreach (var r in b.Requests)
                {
                    int want = b.Requested(r.item);
                    if (want <= 0) continue;
                    foreach (var store in Building.All)
                        if (store != null && store.IsPublicStore && store.Inventory.Count(r.item) > 0)
                            return new DeliverJob(store, b, r.item, want);
                }
            }

            // 3. Loot lying in the settlement goes to the stores
            if (!Building.AnyStore()) return null;
            var map = MapData.Instance;
            if (map == null) return null;
            var center = MapData.WorldToCell(Settlement.Center);
            int radius = C.settlementRadius;
            Nearby.Clear();
            map.CollectObjects(new RectInt(center.x - radius, center.y - radius, radius * 2, radius * 2), Nearby);
            WorldObject pile = null;
            bestDistance = float.MaxValue;
            foreach (var o in Nearby)
            {
                if (o == null || o.IsCarried || !(o is ResourceNode node) || !node.IsCollectable || !o.MovableByCharacters) continue;
                if (JobTargets.Taken(o, v.Movement) || JobTargets.RecentlyFailed(o)) continue;
                var item = StockLootJob.StoredItemOf(o.Definition);
                bool room = false;
                foreach (var b in Building.All) if (b != null && b.IsPublicStore && b.SpaceFor(item) > 0) { room = true; break; }
                if (!room) continue;
                float d = Distance(v, o.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    pile = o;
                }
            }
            return pile != null ? new HaulJob(pile) : null;
        }

        static bool PileExists(SpawnableDefinition item)
        {
            var map = MapData.Instance;
            if (map == null) return false;
            var center = MapData.WorldToCell(Settlement.Center);
            int r = 40;
            Nearby.Clear();
            map.CollectObjects(new RectInt(center.x - r, center.y - r, r * 2, r * 2), Nearby);
            foreach (var o in Nearby)
                if (o != null && !o.IsCarried && o is ResourceNode node && node.IsCollectable && StockLootJob.StoredItemOf(o.Definition) == item) return true;
            return false;
        }

        static UnitJob Heal(Villager v)
        {
            Villager best = null;
            float bestDistance = float.MaxValue;
            foreach (var other in Villager.All)
            {
                if (other == null || other.Movement == null || other.Movement.IsHidden) continue;
                if (other.Needs.Health.Normalized >= 0.9f || JobTargets.Taken(other, v.Movement)) continue;
                float d = Distance(v, other.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = other;
                }
            }
            return best != null ? new PriestHealJob(best) : null;
        }

        // ------------------------------------------------------------------ soldiers

        /// <summary>A free scarecrow (one soldier each).</summary>
        public static Building FreeScarecrow(Villager v)
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || b.Type != BuildingType.Scarecrow || !b.IsUsable || JobTargets.Taken(b, v.Movement) || JobTargets.RecentlyFailed(b)) continue;
                float d = Distance(v, b.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = b;
                }
            }
            return best;
        }
    }

    /// <summary>Homes: every character gets a bed (a house with room; soldiers the army house, priests the church; else the castle).</summary>
    public static class Housing
    {
        public static Building BedFor(Villager v)
        {
            var map = MapData.Instance;
            if (map == null || v == null) return null;
            var memory = v.Memory;
            if (memory.homeUid > 0 && map.FindObject(memory.homeUid) is Building home && home.IsUsable && Fits(home, v)) return home;
            memory.homeUid = 0;
            Building best = null;
            float bestScore = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || !b.IsUsable || !Fits(b, v)) continue;
                float score = Vector2.Distance(v.transform.position, b.GroundPoint);
                bool preferred = b.Type == BuildingType.House ||
                                 (b.Type == BuildingType.ArmyHouse && VillagerRoles.IsSoldier(v.Role)) ||
                                 (b.Type == BuildingType.Church && v.Role == VillagerRole.Priest);
                if (b.Type == BuildingType.Castle) score += 1000f;
                else if (!preferred) continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            if (best != null && best.Type != BuildingType.Castle)
            {
                memory.homeUid = best.Uid;
                v.AddJournal($"Moved into {best.DisplayName}.");
            }
            return best;
        }

        static bool Inside(Building b, Villager v)
        {
            foreach (var u in b.Occupants) if (u == v.Movement) return true;
            return false;
        }

        /// <summary>Room for this character (its residents count as inside even when they are out).</summary>
        static bool Fits(Building b, Villager v)
        {
            if (b.Capacity < 0) return b.Accepts(v.Movement, out _) || Inside(b, v);
            if (Inside(b, v)) return true;
            int residents = 0;
            foreach (var other in Villager.All)
                if (other != null && other != v && other.Memory.homeUid == b.Uid) residents++;
            if (residents >= b.Capacity) return false;
            string reason;
            return b.Accepts(v.Movement, out reason) || reason == null || reason.StartsWith("Full");
        }
    }
}
