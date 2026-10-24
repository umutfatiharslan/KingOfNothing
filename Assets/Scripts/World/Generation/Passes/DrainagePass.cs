using System;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Computes the drainage network: a shortest-path tree from the sea over the land, where each cell has a
    /// noisy "travel cost". Following DrainageNext from any land cell leads to the sea along a curved, natural
    /// valley; paths from nearby sources merge into each other, which is what gives rivers their branches.
    /// Lakes are cheap to cross, so rivers naturally flow into (and out of) lakes.
    /// </summary>
    public sealed class DrainagePass : MapGenerationPass
    {
        const float LakeCost = 0.05f;

        public override string Name => "Drainage";

        public override void Execute(MapGenerationContext context)
        {
            var s = context.Config.Rivers;
            var map = context.Layout;
            int w = map.Width, h = map.Height, n = map.CellCount;

            var courseNoise = new GradientNoise(context.NoiseSeed(301));
            var warpNoise = new GradientNoise(context.NoiseSeed(302));
            var cost = new float[n];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                var terrain = map.Terrain[i];
                if (terrain == CellTerrain.Sea) continue;
                if (terrain == CellTerrain.Lake)
                {
                    cost[i] = LakeCost;
                    continue;
                }
                float wx = x + (warpNoise.Fractal(x, y, s.courseWarpNoise) - 0.5f) * 2f * s.courseWarpStrength;
                float wy = y + (warpNoise.Fractal(y, x, s.courseWarpNoise) - 0.5f) * 2f * s.courseWarpStrength;
                float v = courseNoise.Fractal(wx, wy, s.courseNoise);
                cost[i] = (float)Math.Exp((v - 0.5f) * s.courseContrast);
            }

            var distance = new float[n];
            var next = new int[n];
            var heap = new MinHeap(n / 4);
            for (int i = 0; i < n; i++)
            {
                distance[i] = float.PositiveInfinity;
                next[i] = -1;
            }

            // Sources: sea cells touching land
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (map.Terrain[i] != CellTerrain.Sea) continue;
                bool coast = false;
                for (int k = 0; k < 8 && !coast; k++)
                {
                    int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && map.Terrain[nx + ny * w] != CellTerrain.Sea) coast = true;
                }
                if (!coast) continue;
                distance[i] = 0f;
                heap.Push(0f, i);
            }

            while (heap.TryPop(out float d, out int i, out _))
            {
                if (d > distance[i]) continue;
                int x = i % w, y = i / w;
                float ci = cost[i];
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (map.Terrain[j] == CellTerrain.Sea) continue;
                    float nd = d + GridAlgorithms.Length8[k] * 0.5f * (ci + cost[j]);
                    if (nd >= distance[j]) continue;
                    distance[j] = nd;
                    next[j] = i;
                    heap.Push(nd, j);
                }
            }

            context.DrainageDistance = distance;
            context.DrainageNext = next;
        }
    }
}
