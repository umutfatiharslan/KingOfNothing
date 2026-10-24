using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Brings the land share back to exactly LandmassSettings.landRatio after lakes, rivers and ponds took
    /// their part. The coast is moved one ring at a time following the landmass height (plus a little noise),
    /// so it stays natural. When the coast grows next to a river mouth, the river is extended instead of
    /// being cut off from the sea.
    /// </summary>
    public sealed class LandBalancePass : MapGenerationPass
    {
        public override string Name => "Land Balance";

        public override void Execute(MapGenerationContext context)
        {
            var s = context.Config.LandBalance;
            var map = context.Layout;
            if (s.enabled) Balance(context, s);
            RemoveTinyIslands(map, s.minIslandSize);
        }

        static void Balance(MapGenerationContext context, LandBalanceSettings s)
        {
            var map = context.Layout;
            int w = map.Width, h = map.Height, n = map.CellCount;
            int border = Math.Max(1, context.Config.Landmass.borderMargin);
            int target = (int)Math.Round(context.Config.Landmass.landRatio * n);
            int tolerance = Math.Max(1, (int)(s.tolerance * n));

            var noise = new GradientNoise(context.NoiseSeed(701));
            var score = new float[n];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                score[i] = map.Elevation[i] + (noise.Fractal(x, y, s.coastNoise) - 0.5f) * 2f * s.coastNoiseStrength;
            }

            var candidates = new List<int>();
            var biomeVotes = new int[Math.Max(1, context.Config.Biomes.Length)];
            for (int iteration = 0; iteration < s.maxIterations; iteration++)
            {
                int land = map.Count(CellTerrain.Land);
                int diff = land - target;
                if (Math.Abs(diff) <= tolerance) break;

                candidates.Clear();
                bool shrink = diff > 0;
                for (int y = border; y < h - border; y++)
                for (int x = border; x < w - border; x++)
                {
                    int i = x + y * w;
                    var t = map.Terrain[i];
                    if (shrink ? t != CellTerrain.Land : t != CellTerrain.Sea) continue;
                    if (!shrink && context.ForcedSea != null && context.ForcedSea[i]) continue;
                    if (HasNeighbour(map, x, y, shrink ? CellTerrain.Sea : CellTerrain.Land)) candidates.Add(i);
                }
                if (candidates.Count == 0) break;

                // Shrink: lowest coastal land first. Grow: highest shallow sea first.
                if (shrink) candidates.Sort((p, q) => score[p].CompareTo(score[q]));
                else candidates.Sort((p, q) => score[q].CompareTo(score[p]));

                int amount = Math.Min(Math.Abs(diff), Math.Max(1, (int)(candidates.Count * s.coastLayerFraction)));
                for (int k = 0; k < amount; k++)
                {
                    int i = candidates[k];
                    int x = i % w, y = i / w;
                    if (shrink)
                    {
                        map.Terrain[i] = CellTerrain.Sea;
                        map.Biome[i] = MapLayout.NoBiome;
                    }
                    else if (HasNeighbour(map, x, y, CellTerrain.River))
                    {
                        map.Terrain[i] = CellTerrain.River; // extend the river mouth to the new coast
                    }
                    else
                    {
                        map.Terrain[i] = CellTerrain.Land;
                        map.Biome[i] = MajorityNeighbourBiome(map, x, y, biomeVotes);
                    }
                }
            }
        }

        static bool HasNeighbour(MapLayout map, int x, int y, CellTerrain terrain)
        {
            for (int k = 0; k < 4; k++)
            {
                int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                if (map.InBounds(nx, ny) && map.Terrain[nx + ny * map.Width] == terrain) return true;
            }
            return false;
        }

        static short MajorityNeighbourBiome(MapLayout map, int x, int y, int[] votes)
        {
            Array.Clear(votes, 0, votes.Length);
            for (int k = 0; k < 8; k++)
            {
                int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                if (!map.InBounds(nx, ny)) continue;
                int j = nx + ny * map.Width;
                short b = map.Biome[j];
                if (map.Terrain[j] == CellTerrain.Land && b >= 0 && b < votes.Length) votes[b]++;
            }
            int best = 0;
            for (int b = 1; b < votes.Length; b++)
                if (votes[b] > votes[best]) best = b;
            return (short)best;
        }

        static void RemoveTinyIslands(MapLayout map, int minSize)
        {
            if (minSize <= 1) return;
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Land, sizes);

            // Only islands in the sea are removed; small land specks between swamp pools or rivers stay
            var touchesSea = new bool[sizes.Count];
            int w = map.Width;
            for (int i = 0; i < map.CellCount; i++)
            {
                int l = labels[i];
                if (l < 0 || sizes[l] >= minSize || touchesSea[l]) continue;
                if (HasNeighbour(map, i % w, i / w, CellTerrain.Sea)) touchesSea[l] = true;
            }

            for (int i = 0; i < map.CellCount; i++)
            {
                int l = labels[i];
                if (l < 0 || sizes[l] >= minSize || !touchesSea[l]) continue;
                map.Terrain[i] = CellTerrain.Sea;
                map.Biome[i] = MapLayout.NoBiome;
            }
        }
    }
}
