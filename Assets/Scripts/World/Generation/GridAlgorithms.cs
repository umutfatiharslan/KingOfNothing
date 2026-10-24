using System;
using System.Collections.Generic;

namespace KingOfNothing.World.Generation
{
    /// <summary>Grid helpers shared by the generation passes (distance fields, region labelling, smoothing).</summary>
    public static class GridAlgorithms
    {
        public static readonly int[] Dx4 = { 1, -1, 0, 0 };
        public static readonly int[] Dy4 = { 0, 0, 1, -1 };

        public static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        public static readonly int[] Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        public static readonly float[] Length8 = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        /// <summary>
        /// Breadth-first distance (in 8-neighbour steps, i.e. Chebyshev distance) from every cell to the
        /// nearest source cell. Cells farther than maxDistance keep int.MaxValue.
        /// </summary>
        public static int[] DistanceField(MapLayout map, Func<int, bool> isSource, int maxDistance = int.MaxValue)
        {
            int w = map.Width, h = map.Height, n = map.CellCount;
            var dist = new int[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                if (isSource(i))
                {
                    dist[i] = 0;
                    queue[tail++] = i;
                }
                else dist[i] = int.MaxValue;
            }

            while (head < tail)
            {
                int i = queue[head++];
                int d = dist[i] + 1;
                if (d > maxDistance) continue;
                int x = i % w, y = i / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx8[k], ny = y + Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (dist[j] <= d) continue;
                    dist[j] = d;
                    queue[tail++] = j;
                }
            }
            return dist;
        }

        /// <summary>Same as <see cref="DistanceField(MapLayout, Func{int, bool}, int)"/> with a bool mask as sources.</summary>
        public static int[] DistanceField(MapLayout map, bool[] sources, int maxDistance = int.MaxValue) =>
            DistanceField(map, i => sources[i], maxDistance);

        /// <summary>
        /// Labels 4-connected regions of cells where include(i) is true. Returns the label per cell (-1 = not included)
        /// and fills sizes with the cell count of every label.
        /// </summary>
        public static int[] LabelRegions(MapLayout map, Func<int, bool> include, List<int> sizes, Func<int, int, bool> sameRegion = null)
        {
            int w = map.Width, h = map.Height, n = map.CellCount;
            var label = new int[n];
            for (int i = 0; i < n; i++) label[i] = -1;
            sizes.Clear();
            var stack = new Stack<int>();

            for (int start = 0; start < n; start++)
            {
                if (label[start] >= 0 || !include(start)) continue;
                int id = sizes.Count;
                int size = 0;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    size++;
                    int x = i % w, y = i / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx4[k], ny = y + Dy4[k];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        if (label[j] >= 0 || !include(j)) continue;
                        if (sameRegion != null && !sameRegion(i, j)) continue;
                        label[j] = id;
                        stack.Push(j);
                    }
                }
                sizes.Add(size);
            }
            return label;
        }

        /// <summary>Cellular-automaton smoothing of a boolean mask (majority of the 8 neighbours wins).</summary>
        public static void MajoritySmooth(bool[] mask, int width, int height, int passes)
        {
            var next = new bool[mask.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Dx8[k], ny = y + Dy8[k];
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        if (mask[nx + ny * width]) count++;
                    }
                    int i = x + y * width;
                    next[i] = count > 4 || (count == 4 && mask[i]);
                }
                Array.Copy(next, mask, mask.Length);
            }
        }

        /// <summary>
        /// Removes 4-connected regions of value <paramref name="value"/> smaller than minSize by flipping them.
        /// </summary>
        public static void RemoveSmallRegions(MapLayout map, bool[] mask, bool value, int minSize)
        {
            if (minSize <= 1) return;
            var sizes = new List<int>();
            var labels = LabelRegions(map, i => mask[i] == value, sizes);
            for (int i = 0; i < mask.Length; i++)
            {
                int l = labels[i];
                if (l >= 0 && sizes[l] < minSize) mask[i] = !value;
            }
        }

        /// <summary>Value such that the given fraction of the values is greater than it.</summary>
        public static float ThresholdForTopFraction(float[] values, float fraction)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            int index = (int)Math.Round((1.0 - fraction) * (sorted.Length - 1));
            index = Math.Max(0, Math.Min(sorted.Length - 1, index));
            return sorted[index];
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = edge1 > edge0 ? Clamp01((x - edge0) / (edge1 - edge0)) : (x >= edge1 ? 1f : 0f);
            return t * t * (3f - 2f * t);
        }

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
