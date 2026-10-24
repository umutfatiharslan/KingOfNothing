using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Builds land, sea, lakes and the big rivers from the reference map (the hand-made Azgaar map):
    ///  1. copy land / lakes / rivers / heights of the reference,
    ///  2. lower the sea level until the land share is reached (the shallowest sea becomes land first, with noise
    ///     so the new coasts are irregular - never straight),
    ///  3. extend the river mouths that are now inland to the new coast,
    ///  4. remove tiny islands and sea holes,
    ///  5. give the new land the reference biome of its nearest neighbour.
    /// </summary>
    public sealed class ReferenceTerrainPass : MapGenerationPass
    {
        public override string Name => "Reference Terrain";

        public override void Execute(MapGenerationContext context)
        {
            var config = context.Config;
            var reference = config.Reference;
            var s = config.ReferenceSettings;
            var map = context.Layout;
            if (reference == null)
                throw new InvalidOperationException("The map source is the reference map but no reference data was given.");
            if (reference.Width != map.Width || reference.Height != map.Height)
                throw new InvalidOperationException(
                    $"The reference map was imported at {reference.Width}x{reference.Height} but the map is {map.Width}x{map.Height}. Re-import it.");

            int n = map.CellCount;
            var rivers = reference.River;
            for (int i = 0; i < n; i++)
            {
                map.Terrain[i] = reference.Lake[i] ? CellTerrain.Lake
                    : rivers[i] ? CellTerrain.River
                    : reference.Land[i] ? CellTerrain.Land
                    : CellTerrain.Sea;
                // 0-1 like the procedural height; used by the final land balance
                map.Elevation[i] = reference.Elevation[i] / 100f;
            }
            ClearBorder(map, s.borderMargin);
            ApplyEdgeSeaBand(context);

            float target = Math.Min(0.95f, config.Landmass.landRatio + config.Landmass.initialOvershoot);
            LowerSeaLevel(context, target, rivers);
            ExtendRiversToSea(context);
            ClearBorder(map, s.borderMargin);
            for (int i = 0; i < n; i++)
                if (context.ForcedSea[i]) map.Terrain[i] = CellTerrain.Sea;
            RemoveSmallSeaRegions(map, s.minIslandSize, s.minSeaHoleSize);

            LabelLakes(map);
            context.ReferenceBiomes = SpreadReferenceBiomes(map, reference);
        }

        /// <summary>A sea band of noisy width along the map edges; land there is removed and never comes back.</summary>
        static void ApplyEdgeSeaBand(MapGenerationContext context)
        {
            var map = context.Layout;
            var s = context.Config.ReferenceSettings;
            var noise = new GradientNoise(context.NoiseSeed(803));
            int w = map.Width, h = map.Height;
            var forced = new bool[map.CellCount];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int edge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                // Stretch the noise (fractal noise stays near 0.5) so the band really varies: deep bays and capes
                float v = GridAlgorithms.Clamp01((noise.Fractal(x, y, s.edgeSeaBandNoiseSettings) - 0.5f) * 2.5f + 0.5f);
                float band = s.edgeSeaBand + v * s.edgeSeaBandNoise;
                if (edge >= band) continue;
                int i = x + y * w;
                forced[i] = true;
                map.Terrain[i] = CellTerrain.Sea;
            }
            context.ForcedSea = forced;
        }

        static void ClearBorder(MapLayout map, int margin)
        {
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (x < margin || y < margin || x >= map.Width - margin || y >= map.Height - margin)
                    map.Terrain[x + y * map.Width] = CellTerrain.Sea;
        }

        /// <summary>Turns the highest (shallowest) sea cells into land until the target share is reached.</summary>
        static void LowerSeaLevel(MapGenerationContext context, float targetRatio, bool[] rivers)
        {
            var map = context.Layout;
            var reference = context.Config.Reference;
            var s = context.Config.ReferenceSettings;
            int w = map.Width, h = map.Height, n = map.CellCount;
            int need = (int)Math.Round(targetRatio * n) - map.Count(CellTerrain.Land);
            if (need <= 0) return;

            var broad = new GradientNoise(context.NoiseSeed(801));
            var fine = new GradientNoise(context.NoiseSeed(802));
            var candidates = new List<int>();
            var score = new float[n];
            int margin = s.borderMargin;
            for (int y = margin; y < h - margin; y++)
            for (int x = margin; x < w - margin; x++)
            {
                int i = x + y * w;
                if (map.Terrain[i] != CellTerrain.Sea || context.ForcedSea[i]) continue;
                score[i] = reference.Elevation[i]
                           + (broad.Fractal(x, y, s.coastNoise) - 0.5f) * 2f * s.coastNoiseStrength
                           + (fine.Fractal(x, y, s.coastDetailNoise) - 0.5f) * 2f * s.coastDetailStrength;
                candidates.Add(i);
            }
            if (candidates.Count == 0) return;

            candidates.Sort((p, q) => score[q].CompareTo(score[p]));
            int count = Math.Min(need, candidates.Count);
            for (int k = 0; k < count; k++) map.Terrain[candidates[k]] = CellTerrain.Land;

            // Smooth the new coasts; lakes and rivers stay water
            var land = new bool[n];
            for (int i = 0; i < n; i++) land[i] = map.Terrain[i] == CellTerrain.Land;
            GridAlgorithms.MajoritySmooth(land, w, h, s.coastSmoothingPasses);
            for (int i = 0; i < n; i++)
            {
                if (context.ForcedSea[i]) map.Terrain[i] = CellTerrain.Sea;
                else if (reference.Lake[i]) map.Terrain[i] = CellTerrain.Lake;
                else if (rivers[i]) map.Terrain[i] = CellTerrain.River;
                else map.Terrain[i] = land[i] ? CellTerrain.Land : CellTerrain.Sea;
            }
        }

        /// <summary>
        /// When the sea moves back, river mouths end inside the land. From each mouth the river is extended towards
        /// the open sea (water connected to the map border) along a slightly random path.
        /// </summary>
        static void ExtendRiversToSea(MapGenerationContext context)
        {
            var map = context.Layout;
            var reference = context.Config.Reference;
            int width = Math.Max(1, context.Config.ReferenceSettings.riverExtensionWidth);
            int w = map.Width, h = map.Height, n = map.CellCount;

            // Open sea = sea connected to the map border
            var openSea = new bool[n];
            var queue = new Queue<int>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if ((x == 0 || y == 0 || x == w - 1 || y == h - 1) && map.Terrain[i] == CellTerrain.Sea)
                {
                    openSea[i] = true;
                    queue.Enqueue(i);
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (openSea[j] || map.Terrain[j] != CellTerrain.Sea) continue;
                    openSea[j] = true;
                    queue.Enqueue(j);
                }
            }

            // Noisy shortest paths from every cell to the open sea: following them gives curved, natural extensions
            var s = context.Config.ReferenceSettings;
            var noise = new GradientNoise(context.NoiseSeed(804));
            var cost = new float[n];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cost[x + y * w] = (float)Math.Exp((noise.Fractal(x, y, s.riverExtensionNoise) - 0.5f) * s.riverExtensionContrast);

            var distance = new float[n];
            var next = new int[n];
            var heap = new MinHeap(1 << 16);
            for (int i = 0; i < n; i++)
            {
                next[i] = -1;
                distance[i] = float.PositiveInfinity;
                if (!openSea[i]) continue;
                distance[i] = 0f;
                heap.Push(0f, i);
            }
            while (heap.TryPop(out float d, out int i, out _))
            {
                if (d > distance[i]) continue;
                int x = i % w, y = i / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    float nd = d + GridAlgorithms.Length8[k] * 0.5f * (cost[i] + cost[j]);
                    if (nd >= distance[j]) continue;
                    distance[j] = nd;
                    next[j] = i;
                    heap.Push(nd, j);
                }
            }

            foreach (int mouth in reference.RiverMouths)
            {
                int cur = mouth;
                for (int step = 0; step < w + h && cur >= 0 && !openSea[cur]; step++)
                {
                    int nxt = next[cur];
                    if (nxt < 0) break;
                    int x = cur % w, y = cur / w, bx = nxt % w, by = nxt / w;
                    // A diagonal step also opens the cell between, so the river is never cut
                    if (bx != x && by != y) CarveSquare(map, bx, y, width);
                    CarveSquare(map, bx, by, width);
                    cur = nxt;
                }
            }
        }

        static void CarveSquare(MapLayout map, int cx, int cy, int width)
        {
            int lo = -(width - 1) / 2, hi = width / 2;
            for (int dy = lo; dy <= hi; dy++)
            for (int dx = lo; dx <= hi; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!map.InBounds(x, y)) continue;
                int i = x + y * map.Width;
                if (map.Terrain[i] == CellTerrain.Land) map.Terrain[i] = CellTerrain.River;
            }
        }

        static void RemoveSmallSeaRegions(MapLayout map, int minIsland, int minSeaHole)
        {
            int n = map.CellCount;
            var land = new bool[n];
            for (int i = 0; i < n; i++) land[i] = map.Terrain[i] == CellTerrain.Land;
            GridAlgorithms.RemoveSmallRegions(map, land, true, minIsland);
            for (int i = 0; i < n; i++)
                if (!land[i] && map.Terrain[i] == CellTerrain.Land) map.Terrain[i] = CellTerrain.Sea;

            // Tiny sea holes inside the land become land (lakes and rivers are kept)
            if (minSeaHole <= 1) return;
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Sea, sizes);
            for (int i = 0; i < n; i++)
            {
                int l = labels[i];
                if (l >= 0 && sizes[l] < minSeaHole) map.Terrain[i] = CellTerrain.Land;
            }
        }

        static void LabelLakes(MapLayout map)
        {
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Lake, sizes);
            var sumX = new double[sizes.Count];
            var sumY = new double[sizes.Count];
            for (int i = 0; i < map.CellCount; i++)
            {
                int l = labels[i];
                map.LakeId[i] = l + 1;
                if (l < 0) continue;
                sumX[l] += map.X(i);
                sumY[l] += map.Y(i);
            }
            for (int l = 0; l < sizes.Count; l++)
            {
                map.Lakes.Add(new LakeInfo
                {
                    Id = l + 1,
                    CenterX = (int)(sumX[l] / sizes[l]),
                    CenterY = (int)(sumY[l] / sizes[l]),
                    Radius = (float)Math.Sqrt(sizes[l] / Math.PI),
                    CellCount = sizes[l],
                });
            }
        }

        /// <summary>Reference biome per land cell; land that is new (former sea) takes its nearest neighbour's biome.</summary>
        static ReferenceBiome[] SpreadReferenceBiomes(MapLayout map, ReferenceMapData reference)
        {
            int w = map.Width, h = map.Height, n = map.CellCount;
            var biomes = new ReferenceBiome[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (map.Terrain[i] != CellTerrain.Land) continue;
                var b = reference.Land[i] ? reference.Biome[i] : ReferenceBiome.Marine;
                biomes[i] = b;
                if (b != ReferenceBiome.Marine) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (map.Terrain[j] != CellTerrain.Land || biomes[j] != ReferenceBiome.Marine) continue;
                    biomes[j] = biomes[i];
                    queue.Enqueue(j);
                }
            }
            for (int i = 0; i < n; i++)
                if (map.Terrain[i] == CellTerrain.Land && biomes[i] == ReferenceBiome.Marine) biomes[i] = ReferenceBiome.Grassland;
            return biomes;
        }
    }
}
