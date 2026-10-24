using System.Collections.Generic;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Seeded pseudo random number generator (xoshiro128**).
    /// Unlike UnityEngine.Random and System.Random it gives exactly the same sequence on every machine and
    /// every .NET runtime, so the same seed always produces the same map for the whole team.
    /// </summary>
    public sealed class DeterministicRandom
    {
        uint s0, s1, s2, s3;

        public DeterministicRandom(int seed)
        {
            ulong state = (ulong)(uint)seed;
            s0 = SplitMix(ref state);
            s1 = SplitMix(ref state);
            s2 = SplitMix(ref state);
            s3 = SplitMix(ref state);
            if ((s0 | s1 | s2 | s3) == 0) s0 = 1u;
        }

        static uint SplitMix(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (uint)((z ^ (z >> 31)) >> 32);
        }

        static uint RotateLeft(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            uint result = RotateLeft(s1 * 5u, 7) * 9u;
            uint t = s1 << 9;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = RotateLeft(s3, 11);
            return result;
        }

        /// <summary>Random int that can be used as the seed of another generator.</summary>
        public int NextSeed() => unchecked((int)NextUInt());

        /// <summary>[0, 1)</summary>
        public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

        /// <summary>[0, 1)</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>[minInclusive, maxExclusive)</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextDouble() * ((long)maxExclusive - minInclusive));
        }

        /// <summary>[min, max]</summary>
        public int RangeInclusive(int min, int max) => max < min ? min : Range(min, max + 1);

        /// <summary>[min, max)</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Fisher-Yates shuffle.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public void Shuffle(int[] array, int count)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                int tmp = array[i];
                array[i] = array[j];
                array[j] = tmp;
            }
        }

        /// <summary>Picks an index according to the given weights. Returns -1 if every weight is zero.</summary>
        public int WeightedIndex(IReadOnlyList<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0f) total += weights[i];
            if (total <= 0f) return -1;

            float roll = NextFloat() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                roll -= weights[i];
                if (roll < 0f) return i;
            }
            for (int i = weights.Count - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;
            return -1;
        }
    }
}
