using System;
using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>Inclusive integer range editable in the Inspector (e.g. "3 - 5 regions").</summary>
    [Serializable]
    public struct IntRange
    {
        public int min;
        public int max;

        public IntRange(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        /// <summary>Random value in [min, max].</summary>
        public int Roll(DeterministicRandom random) => random.RangeInclusive(Math.Min(min, max), Math.Max(min, max));

        public bool Contains(int value) => value >= Math.Min(min, max) && value <= Math.Max(min, max);

        public override string ToString() => $"{min}-{max}";
    }

    /// <summary>Float range editable in the Inspector.</summary>
    [Serializable]
    public struct FloatRange
    {
        public float min;
        public float max;

        public FloatRange(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        public float Roll(DeterministicRandom random) => random.Range(Math.Min(min, max), Math.Max(min, max));

        /// <summary>Value at t in [0, 1] between min and max.</summary>
        public float Lerp(float t) => min + (max - min) * t;

        public override string ToString() => $"{min}-{max}";
    }
}
