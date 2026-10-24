using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>Villager stats from the GDD (chapter 7).</summary>
    public enum StatType
    {
        Building,
        Woodcutting,
        Mining,
        Farming,
        Efficiency,
        Greed,
        Gourmet,
        Archery,
        Melee,
        Defense,
        Research,
    }

    [Serializable]
    public struct StatValue
    {
        public StatType type;
        [Range(0, StatBlock.MaxLevel)] public int level;

        public StatValue(StatType type, int level)
        {
            this.type = type;
            this.level = level;
        }
    }

    /// <summary>
    /// The stats of one character. Every stat goes from 0 to 20 and unlocks a special ability every
    /// 5 levels (GDD: e.g. Woodcutting 5 = carry two items, 10 = 25% chance of a double hit...).
    /// A stat of 0 means the character can never do that job.
    /// </summary>
    [Serializable]
    public class StatBlock
    {
        public const int MaxLevel = 20;
        public const int MilestoneInterval = 5;

        [SerializeField] List<StatValue> values = new List<StatValue>();

        /// <summary>(stat, old level, new level)</summary>
        public event Action<StatType, int, int> Changed;

        public int Get(StatType type)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].type == type) return values[i].level;
            return 0;
        }

        public void Set(StatType type, int level)
        {
            level = Mathf.Clamp(level, 0, MaxLevel);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].type != type) continue;
                int old = values[i].level;
                if (old == level) return;
                values[i] = new StatValue(type, level);
                Changed?.Invoke(type, old, level);
                return;
            }
            values.Add(new StatValue(type, level));
            if (level != 0) Changed?.Invoke(type, 0, level);
        }

        public void Add(StatType type, int amount) => Set(type, Get(type) + amount);

        public bool CanDo(StatType type) => Get(type) > 0;

        /// <summary>Number of special abilities unlocked for the stat (0-4).</summary>
        public int MilestoneTier(StatType type) => Get(type) / MilestoneInterval;

        /// <summary>Rolls every stat between the given bounds (used for new villagers).</summary>
        public void Randomize(int min, int max)
        {
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
                Set(type, UnityEngine.Random.Range(min, max + 1));
        }

        public IReadOnlyList<StatValue> All => values;
    }
}
