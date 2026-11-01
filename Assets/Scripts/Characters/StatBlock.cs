using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// Villager stats (stat table v3). The numbers stay fixed so older saves keep their meaning (removed stats:
    /// 5 Greed, 6 Gourmet, 8 Melee, 10 Research).
    /// </summary>
    public enum StatType
    {
        Building = 0,
        Woodcutting = 1,
        Mining = 2,
        Farming = 3,
        Efficiency = 4,
        Archery = 7,
        Defense = 9,
        Hunting = 11,
        Fishing = 12,
        Baking = 13,
        Sword = 14,
        Spear = 15,
        Health = 16,
        Healing = 17,
        WalkSpeed = 18,
        Hunger = 19,
    }

    /// <summary>Groups of the stat table.</summary>
    public enum StatGroup
    {
        /// <summary>Work and production (0 = can never do the job).</summary>
        Work,
        /// <summary>War (0 = can never do it).</summary>
        War,
        /// <summary>Support: healing, priests only.</summary>
        Support,
        /// <summary>Life: innate, always at least 1.</summary>
        Life,
    }

    /// <summary>Lists and names of the stats.</summary>
    public static class StatInfo
    {
        public static readonly StatType[] Work =
        {
            StatType.Building, StatType.Woodcutting, StatType.Mining, StatType.Farming,
            StatType.Hunting, StatType.Fishing, StatType.Baking, StatType.Efficiency,
        };
        public static readonly StatType[] War = { StatType.Archery, StatType.Sword, StatType.Spear, StatType.Defense, StatType.Health };
        public static readonly StatType[] Support = { StatType.Healing };
        public static readonly StatType[] Life = { StatType.WalkSpeed, StatType.Hunger };

        /// <summary>Every stat in table order.</summary>
        public static readonly StatType[] All =
        {
            StatType.Building, StatType.Woodcutting, StatType.Mining, StatType.Farming, StatType.Hunting, StatType.Fishing,
            StatType.Baking, StatType.Efficiency, StatType.Archery, StatType.Sword, StatType.Spear, StatType.Defense,
            StatType.Health, StatType.Healing, StatType.WalkSpeed, StatType.Hunger,
        };

        public static StatGroup GroupOf(StatType type)
        {
            if (Array.IndexOf(Work, type) >= 0) return StatGroup.Work;
            if (Array.IndexOf(War, type) >= 0) return StatGroup.War;
            if (type == StatType.Healing) return StatGroup.Support;
            return StatGroup.Life;
        }

        public static bool IsLife(StatType type) => GroupOf(type) == StatGroup.Life;

        public static string Name(StatType type)
        {
            switch (type)
            {
                case StatType.WalkSpeed: return "Walk Speed";
                case StatType.Sword: return "Sword Fighting";
                case StatType.Spear: return "Spear Fighting";
                default: return type.ToString();
            }
        }

        public static string GroupName(StatGroup group)
        {
            switch (group)
            {
                case StatGroup.Work: return "Work & production";
                case StatGroup.War: return "War";
                case StatGroup.Support: return "Support";
                default: return "Life";
            }
        }

        public static bool IsKnown(StatType type) => Array.IndexOf(All, type) >= 0;
    }

    /// <summary>One stat of one character: level, experience and the school / army training of the current level.</summary>
    [Serializable]
    public class StatProgress
    {
        public StatType type;
        [Range(0, StatBlock.MaxLevel)] public int level;
        [Tooltip("Experience collected towards the next level.")]
        public float xp;
        [Tooltip("Level whose training is finished: its XP multiplier is active while the stat is still at that level (-1 = none).")]
        public int trainedLevel = -1;
        [Tooltip("Level the training progress below belongs to (-1 = none).")]
        public int trainingLevel = -1;
        [Tooltip("Game hours already trained for that level (kept when the training is interrupted).")]
        public float trainingHours;
        [Tooltip("The training cost of that level was already paid.")]
        public bool trainingPaid;

        public StatProgress() { }

        public StatProgress(StatType type, int level)
        {
            this.type = type;
            this.level = level;
        }

        public StatProgress Clone() => (StatProgress)MemberwiseClone();
    }

    /// <summary>
    /// The stats of one character. Every stat goes from 0 to 20; a stat grows by doing its job (experience) and
    /// unlocks a milestone ability every 5 levels. Work and war stats at 0 can never be done or developed; life
    /// stats are always at least 1. Temporary bonuses (the tower's archery) count for the level effects but not
    /// for milestones, experience or training.
    /// </summary>
    [Serializable]
    public class StatBlock
    {
        public const int MaxLevel = 20;
        public const int MilestoneInterval = 5;

        [SerializeField] List<StatProgress> values = new List<StatProgress>();

        readonly Dictionary<StatType, int> bonuses = new Dictionary<StatType, int>();

        /// <summary>(stat, old level, new level) of the real level.</summary>
        public event Action<StatType, int, int> Changed;
        /// <summary>(stat, old level, new level) when experience raised the level.</summary>
        public event Action<StatType, int, int> LevelGained;
        /// <summary>(stat, experience added after the multiplier).</summary>
        public event Action<StatType, float> XpGained;

        public IReadOnlyList<StatProgress> All => values;

        StatProgress Find(StatType type)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].type == type) return values[i];
            return null;
        }

        StatProgress Entry(StatType type)
        {
            var e = Find(type);
            if (e == null)
            {
                e = new StatProgress(type, 0);
                values.Add(e);
            }
            return e;
        }

        /// <summary>The progress record of a stat (created at level 0 if missing).</summary>
        public StatProgress Progress(StatType type) => Entry(type);

        /// <summary>Real level (milestones, experience, training).</summary>
        public int Base(StatType type)
        {
            var e = Find(type);
            return e != null ? e.level : 0;
        }

        /// <summary>Level used by the level effects: the real level plus temporary bonuses (tower...).</summary>
        public int Get(StatType type)
        {
            int level = Base(type);
            if (level <= 0) return 0;
            return bonuses.TryGetValue(type, out int b) ? level + b : level;
        }

        public int Bonus(StatType type) => bonuses.TryGetValue(type, out int b) ? b : 0;

        /// <summary>Sets a temporary bonus (0 removes it).</summary>
        public void SetBonus(StatType type, int amount)
        {
            if (amount == 0) bonuses.Remove(type);
            else bonuses[type] = amount;
        }

        /// <summary>Sets the real level (experience starts from 0 again).</summary>
        public void Set(StatType type, int level)
        {
            level = Mathf.Clamp(level, 0, MaxLevel);
            var e = Entry(type);
            int old = e.level;
            e.level = level;
            if (old != level) e.xp = 0f;
            if (old != level) Changed?.Invoke(type, old, level);
        }

        public void Add(StatType type, int amount) => Set(type, Base(type) + amount);

        /// <summary>Can the character do this at all (0 = never)?</summary>
        public bool CanDo(StatType type) => Base(type) > 0;

        /// <summary>Number of milestone abilities unlocked for the stat (0-4).</summary>
        public int MilestoneTier(StatType type) => Base(type) / MilestoneInterval;

        /// <summary>The milestone of this level (5, 10, 15, 20) is unlocked.</summary>
        public bool Has(StatType type, int milestoneLevel) => Base(type) >= milestoneLevel;

        public float Xp(StatType type)
        {
            var e = Find(type);
            return e != null ? e.xp : 0f;
        }

        /// <summary>The school / army training of the current level is finished (its XP multiplier is active).</summary>
        public bool IsTrained(StatType type)
        {
            var e = Find(type);
            return e != null && e.level > 0 && e.trainedLevel == e.level;
        }

        /// <summary>
        /// Adds experience (already multiplied by the caller). Stats at 0 and at the maximum learn nothing.
        /// Returns the number of levels gained.
        /// </summary>
        public int AddXp(StatType type, float amount, Func<int, float> xpToNext)
        {
            if (amount <= 0f) return 0;
            var e = Find(type);
            if (e == null || e.level <= 0 || e.level >= MaxLevel) return 0;
            e.xp += amount;
            XpGained?.Invoke(type, amount);
            int gained = 0;
            while (e.level < MaxLevel)
            {
                float need = Mathf.Max(1f, xpToNext(e.level));
                if (e.xp < need) break;
                e.xp -= need;
                int old = e.level;
                e.level++;
                gained++;
                Changed?.Invoke(type, old, e.level);
                LevelGained?.Invoke(type, old, e.level);
            }
            if (e.level >= MaxLevel) e.xp = 0f;
            return gained;
        }

        /// <summary>Replaces everything (loading a save).</summary>
        public void Load(IEnumerable<StatProgress> saved)
        {
            values.Clear();
            if (saved == null) return;
            foreach (var s in saved)
                if (s != null && StatInfo.IsKnown(s.type) && Find(s.type) == null) values.Add(s.Clone());
        }

        /// <summary>A copy of every stat record (saving).</summary>
        public List<StatProgress> Snapshot()
        {
            var list = new List<StatProgress>();
            foreach (var v in values) if (v != null && StatInfo.IsKnown(v.type)) list.Add(v.Clone());
            return list;
        }

        /// <summary>Removes stats that no longer exist (older prefabs / saves).</summary>
        public void Clean() => values.RemoveAll(v => v == null || !StatInfo.IsKnown(v.type));
    }
}
