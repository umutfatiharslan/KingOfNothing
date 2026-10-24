using System;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Builds the continents: domain-warped fractal noise minus a noisy falloff towards the map edge.
    /// The land threshold is chosen from the height distribution so the land share is exact, and the
    /// falloff guarantees that land never touches (and is never cut straight by) the map border.
    /// </summary>
    public sealed class LandmassPass : MapGenerationPass
    {
        public override string Name => "Landmass";

        public override void Execute(MapGenerationContext context)
        {
            var s = context.Config.Landmass;
            var map = context.Layout;
            int w = map.Width, h = map.Height;

            var elevationNoise = new GradientNoise(context.NoiseSeed(101));
            var warpNoiseX = new GradientNoise(context.NoiseSeed(102));
            var warpNoiseY = new GradientNoise(context.NoiseSeed(103));
            var edgeNoise = new GradientNoise(context.NoiseSeed(104));

            float exponent = Math.Max(1f, s.edgeShapeExponent);
            float invExponent = 1f / exponent;
            var elevation = map.Elevation;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (x < s.borderMargin || y < s.borderMargin || x >= w - s.borderMargin || y >= h - s.borderMargin)
                {
                    elevation[i] = -1f;
                    continue;
                }

                // Domain warp: bend the sample position so coasts and continents get organic shapes
                float wx = x + (warpNoiseX.Fractal(x, y, s.warpNoise) - 0.5f) * 2f * s.warpStrength;
                float wy = y + (warpNoiseY.Fractal(x, y, s.warpNoise) - 0.5f) * 2f * s.warpStrength;
                float e = elevationNoise.Fractal(wx, wy, s.elevationNoise);

                // Rounded-rectangle distance to the map centre (0 centre, 1 edge) with noise on top
                float nx = Math.Abs((x - w * 0.5f) / (w * 0.5f));
                float ny = Math.Abs((y - h * 0.5f) / (h * 0.5f));
                float d = (float)Math.Pow(Math.Pow(nx, exponent) + Math.Pow(ny, exponent), invExponent)
                          + (edgeNoise.Fractal(x, y, s.edgeNoise) - 0.5f) * 2f * s.edgeNoiseStrength;
                float edge = GridAlgorithms.SmoothStep(s.edgeFalloffStart, s.edgeFalloffEnd, d);

                elevation[i] = e - edge * s.edgeFalloffStrength;
            }

            float target = Math.Min(0.98f, s.landRatio + s.initialOvershoot);
            float threshold = GridAlgorithms.ThresholdForTopFraction(elevation, target);

            var land = new bool[map.CellCount];
            for (int i = 0; i < land.Length; i++) land[i] = elevation[i] > threshold;

            GridAlgorithms.MajoritySmooth(land, w, h, s.coastSmoothingPasses);
            GridAlgorithms.RemoveSmallRegions(map, land, true, s.minIslandSize);
            GridAlgorithms.RemoveSmallRegions(map, land, false, s.minInlandWaterSize);

            for (int i = 0; i < land.Length; i++)
                map.Terrain[i] = land[i] ? CellTerrain.Land : CellTerrain.Sea;
        }
    }
}
