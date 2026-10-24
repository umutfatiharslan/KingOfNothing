using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Adds the small water bodies that belong to a biome:
    ///  - marsh pools: biomes with a Marsh Water Ratio (the swamp) get many small noise-shaped pools,
    ///  - ponds: small puddles scattered according to each biome's "Ponds Per 10000 Cells".
    /// The cells keep their biome so gameplay can still tell that a pool belongs to the swamp.
    /// </summary>
    public sealed class WetlandPass : MapGenerationPass
    {
        public override string Name => "Ponds & Marsh";

        public override void Execute(MapGenerationContext context)
        {
            AddMarshPools(context);
            AddPonds(context);
        }

        static void AddMarshPools(MapGenerationContext context)
        {
            var map = context.Layout;
            var s = context.Config.Wetlands;
            var biomes = context.Config.Biomes;
            var noise = new GradientNoise(context.NoiseSeed(601));
            int w = map.Width, h = map.Height, n = map.CellCount;

            for (int b = 0; b < biomes.Length; b++)
            {
                var profile = biomes[b];
                if (profile == null || profile.marshWaterRatio <= 0f) continue;
                short id = (short)b;

                int biomeCells = 0;
                for (int i = 0; i < n; i++)
                    if (map.Terrain[i] == CellTerrain.Land && map.Biome[i] == id) biomeCells++;
                if (biomeCells == 0) continue;

                // Distance to the nearest cell that is NOT this biome's land: keeps pools off the biome edge
                var edgeDistance = GridAlgorithms.DistanceField(map, i => !(map.Terrain[i] == CellTerrain.Land && map.Biome[i] == id),
                    s.marshEdgeMargin + 1);

                var inner = new List<int>();
                var values = new List<float>();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    if (edgeDistance[i] < s.marshEdgeMargin || edgeDistance[i] == 0) continue;
                    inner.Add(i);
                    values.Add(noise.Fractal(x, y, s.marshNoise));
                }
                if (inner.Count == 0) continue;

                float fraction = Math.Min(0.95f, profile.marshWaterRatio * biomeCells / inner.Count);
                float threshold = GridAlgorithms.ThresholdForTopFraction(values.ToArray(), fraction);
                for (int k = 0; k < inner.Count; k++)
                    if (values[k] > threshold) map.Terrain[inner[k]] = CellTerrain.Marsh;
            }
        }

        static void AddPonds(MapGenerationContext context)
        {
            var map = context.Layout;
            var s = context.Config.Wetlands;
            var biomes = context.Config.Biomes;
            var random = context.Random;
            int w = map.Width, n = map.CellCount;
            var waterDistance = map.ComputeDistanceToWater();

            var cells = new List<int>();
            for (int b = 0; b < biomes.Length; b++)
            {
                var profile = biomes[b];
                if (profile == null || profile.pondsPer10000Cells <= 0f) continue;
                short id = (short)b;

                cells.Clear();
                for (int i = 0; i < n; i++)
                    if (map.Terrain[i] == CellTerrain.Land && map.Biome[i] == id && waterDistance[i] >= s.pondMinDistanceToWater)
                        cells.Add(i);
                if (cells.Count == 0) continue;

                int count = (int)(cells.Count / 10000f * profile.pondsPer10000Cells);
                for (int p = 0; p < count; p++)
                {
                    int c = cells[random.Range(0, cells.Count)];
                    int cx = c % w, cy = c / w;
                    float radius = s.pondRadius.Roll(random);
                    float aspect = random.Range(0.75f, 1.25f);
                    int extent = (int)Math.Ceiling(radius * 1.3f) + 1;
                    for (int dy = -extent; dy <= extent; dy++)
                    for (int dx = -extent; dx <= extent; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (!map.InBounds(x, y)) continue;
                        int i = x + y * w;
                        if (map.Terrain[i] != CellTerrain.Land) continue;
                        float ex = dx * aspect, ey = dy / aspect;
                        if (ex * ex + ey * ey <= radius * radius) map.Terrain[i] = CellTerrain.Pond;
                    }
                }
            }
        }
    }
}
