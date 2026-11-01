using System.Collections.Generic;
using KingOfNothing.Core;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// School / army house / church trainings. A character inside the building trains one stat for one level:
    /// the cost (gold coins + materials, from the building's own storage) is paid when the training of that level
    /// starts, the time is spent inside (CharacterBrain), and when it is done the experience of that level is
    /// multiplied (x1.1 at level 1 ... x2.9 at level 19). Training never gives experience itself. An interrupted
    /// training keeps its progress and payment and can be resumed from the training window.
    /// </summary>
    public static class TrainingService
    {
        static StatSettings S => StatSettings.Current;

        /// <summary>Stats trained in this kind of building.</summary>
        public static List<StatType> StatsOf(BuildingType type)
        {
            var list = new List<StatType>();
            foreach (var t in S.trainings)
                if (t != null && t.building == type && !list.Contains(t.stat)) list.Add(t.stat);
            return list;
        }

        public static bool TrainsHere(Building b) => b != null && StatsOf(b.Type).Count > 0;

        static bool Inside(Villager v, Building b)
        {
            if (v == null || b == null) return false;
            foreach (var u in b.Occupants) if (u == v.Movement) return true;
            return false;
        }

        public static List<DropEntry> Cost(StatType stat, int level)
        {
            var coin = MapData.Instance != null ? MapData.Instance.Gameplay.goldCoin : null;
            return S.TrainingCost(stat, level, coin);
        }

        /// <summary>Already paid for the current level (an interrupted training).</summary>
        public static bool Paid(Villager v, StatType stat)
        {
            var p = v.Stats.Progress(stat);
            return p.trainingLevel == p.level && p.trainingPaid;
        }

        public static bool CanStart(Villager v, Building b, StatType stat, out string reason)
        {
            reason = null;
            if (v == null || b == null) { reason = "Nobody"; return false; }
            if (!b.IsUsable) { reason = "The building is not usable"; return false; }
            var t = S.TrainingFor(stat);
            if (t == null || t.building != b.Type) { reason = "Not trained here"; return false; }
            if (!Inside(v, b)) { reason = "Must be inside"; return false; }
            var stats = v.Stats;
            if (stat == StatType.Healing && v.Role != VillagerRole.Priest) { reason = "Priests only"; return false; }
            if (!stats.CanDo(stat)) { reason = $"{StatInfo.Name(stat)} 0: can never learn it"; return false; }
            if (stats.Base(stat) >= StatBlock.MaxLevel) { reason = "Already at the maximum"; return false; }
            if (stats.IsTrained(stat)) { reason = "This level is trained already"; return false; }
            if (b.Type == BuildingType.ArmyHouse && v.Needs.Health.Normalized < S.trainingMinHealth)
            {
                reason = $"Needs {Mathf.RoundToInt(S.trainingMinHealth * 100f)}% health";
                return false;
            }
            if (!Paid(v, stat) && !GameModeManager.IsDeveloperMode)
                foreach (var c in Cost(stat, stats.Base(stat)))
                    if (b.Inventory.Count(c.item) < c.amount)
                    {
                        reason = $"Needs {c.amount} {c.item.DisplayName} in the storage ({b.Inventory.Count(c.item)})";
                        return false;
                    }
            return true;
        }

        public static bool Start(Villager v, Building b, StatType stat)
        {
            if (!CanStart(v, b, stat, out string reason))
            {
                Debug.Log($"[Training] {v?.DisplayName} cannot train {stat}: {reason}.");
                return false;
            }
            var p = v.Stats.Progress(stat);
            int level = p.level;
            if (p.trainingLevel != level)
            {
                p.trainingLevel = level;
                p.trainingHours = 0f;
                p.trainingPaid = false;
            }
            if (!p.trainingPaid)
            {
                if (!GameModeManager.IsDeveloperMode)
                    foreach (var c in Cost(stat, level)) b.Withdraw(c.item, c.amount);
                p.trainingPaid = true;
            }
            v.Training.Start(b.Uid, stat);
            v.Brain?.ResumeRoutine();
            v.AddJournal($"Started the {StatInfo.Name(stat)} training (level {level} -> XP x{S.TrainingMultiplier(level):0.0#}) in {b.DisplayName}.");
            return true;
        }

        public static void Stop(Villager v)
        {
            if (v == null || !v.Training.active) return;
            v.Training.Stop();
            v.AddJournal($"Stopped the {StatInfo.Name(v.Training.stat)} training (progress kept).");
        }

        /// <summary>Asks haulers to bring what the training of this level still needs to the building's storage.</summary>
        public static void RequestMaterials(Villager v, Building b, StatType stat)
        {
            foreach (var c in Cost(stat, v.Stats.Base(stat)))
            {
                int missing = c.amount - b.Inventory.Count(c.item);
                if (missing > 0) b.Request(c.item, missing);
            }
        }

        /// <summary>Developer: finishes the training at once.</summary>
        public static void FinishNow(Villager v, StatType stat)
        {
            var p = v.Stats.Progress(stat);
            p.trainingLevel = p.level;
            p.trainingHours = S.TrainingHoursFor(p.level);
        }
    }
}
