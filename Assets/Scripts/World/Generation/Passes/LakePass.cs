using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Carves lakes with natural, varied shapes: every lake is the union of 1-N rotated ellipses ("lobes")
    /// with a noisy shore. Sizes are skewed so most lakes are small and a few are large.
    /// </summary>
    public sealed class LakePass : MapGenerationPass
    {
        public override string Name => "Lakes";

        public override void Execute(MapGenerationContext context)
        {
            var s = context.Config.Lakes;
            if (!s.enabled) return;

            var map = context.Layout;
            var random = context.Random;
            int w = map.Width, h = map.Height;
            var coastDistance = map.ComputeDistanceToWater();
            var shoreNoise = new GradientNoise(context.NoiseSeed(201));

            int wanted = s.count.Roll(random);
            var placed = new List<LakeInfo>();
            var lobeX = new List<float>();
            var lobeY = new List<float>();
            var lobeR = new List<float>();
            var lobeCos = new List<float>();
            var lobeSin = new List<float>();
            var lobeAspect = new List<float>();

            for (int attempt = 0; attempt < wanted * 60 && placed.Count < wanted; attempt++)
            {
                float t = (float)Math.Pow(random.NextFloat(), s.smallLakeBias);
                float radius = s.radius.Lerp(t);
                int cx = random.Range(0, w), cy = random.Range(0, h);
                int ci = cx + cy * w;
                if (!map.IsLand(ci) || coastDistance[ci] < radius + s.minDistanceToCoast) continue;

                bool tooClose = false;
                foreach (var other in placed)
                {
                    float dx = cx - other.CenterX, dy = cy - other.CenterY;
                    if (Math.Sqrt(dx * dx + dy * dy) < s.minDistanceBetweenLakes + radius + other.Radius)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                // Lobes: the first one is the main body, the others bulge out of it
                lobeX.Clear(); lobeY.Clear(); lobeR.Clear(); lobeCos.Clear(); lobeSin.Clear(); lobeAspect.Clear();
                int lobes = Math.Max(1, s.lobes.Roll(random));
                for (int k = 0; k < lobes; k++)
                {
                    float lx = cx, ly = cy, lr = radius;
                    if (k > 0)
                    {
                        float a = random.NextFloat() * (float)Math.PI * 2f;
                        float dist = radius * random.Range(0.5f, 0.9f);
                        lx = cx + (float)Math.Cos(a) * dist;
                        ly = cy + (float)Math.Sin(a) * dist;
                        lr = radius * random.Range(0.45f, 0.75f);
                    }
                    float rotation = random.NextFloat() * (float)Math.PI;
                    lobeX.Add(lx);
                    lobeY.Add(ly);
                    lobeR.Add(lr);
                    lobeCos.Add((float)Math.Cos(rotation));
                    lobeSin.Add((float)Math.Sin(rotation));
                    lobeAspect.Add(random.Range(0.55f, 1f));
                }

                var lake = new LakeInfo { Id = map.Lakes.Count + 1, CenterX = cx, CenterY = cy, Radius = radius };
                float noiseScale = Math.Max(4f, radius * 0.6f);
                int extent = (int)Math.Ceiling(radius * 2.2f);
                for (int y = Math.Max(0, cy - extent); y <= Math.Min(h - 1, cy + extent); y++)
                for (int x = Math.Max(0, cx - extent); x <= Math.Min(w - 1, cx + extent); x++)
                {
                    int i = x + y * w;
                    if (!map.IsLand(i)) continue;

                    float field = float.MaxValue;
                    for (int k = 0; k < lobeX.Count; k++)
                    {
                        float dx = x - lobeX[k], dy = y - lobeY[k];
                        float rx = dx * lobeCos[k] + dy * lobeSin[k];
                        float ry = (-dx * lobeSin[k] + dy * lobeCos[k]) / lobeAspect[k];
                        float d = (float)Math.Sqrt(rx * rx + ry * ry) / lobeR[k];
                        if (d < field) field = d;
                    }
                    float wobble = (shoreNoise.Fractal(x / noiseScale, y / noiseScale, 3) - 0.5f) * 2f * s.shoreWobble;
                    if (field + wobble >= 1f) continue;

                    map.Terrain[i] = CellTerrain.Lake;
                    map.LakeId[i] = lake.Id;
                    lake.CellCount++;
                }

                if (lake.CellCount > 0)
                {
                    placed.Add(lake);
                    map.Lakes.Add(lake);
                }
            }
        }
    }
}
