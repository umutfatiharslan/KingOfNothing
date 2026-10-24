using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Lays out the biomes.
    ///
    /// Regional biomes (desert, rock, swamp...) get an exact share of the map ("Map Coverage"), split into a
    /// configurable number of large regions. Each region grows from a seed cell like a flood fill whose step
    /// cost comes from noise, so regions get irregular lobed outlines, and it stops exactly at its quota.
    /// This is what guarantees e.g. "desert = 5% of the map in 3-5 big areas" instead of a few tiny specks.
    ///
    /// Filler biomes (the grass types) then share the remaining land: many seeds grow at the same time and
    /// meet at noisy borders (an organic Voronoi diagram).
    ///
    /// Reference maps: every land cell first gets the biome of the reference map (smoothed). Biomes set to
    /// "Regional Quota" (desert, rock) are then regrown to their exact share and region count, starting from
    /// the reference's own areas and preferring them, and only over grassland (swamps etc. stay as they are).
    /// </summary>
    public sealed class BiomeLayoutPass : MapGenerationPass
    {
        public override string Name => "Biomes";

        MapLayout map;
        MapGenerationConfig config;
        BiomeLayoutSettings settings;
        float[] borderCost;
        int[] waterDistance;
        float[] dist;
        readonly List<int> touched = new List<int>();
        MinHeap heap;
        GradientNoise bufferNoise;
        /// <summary>Cells already taken by a regional biome.</summary>
        bool[] locked;
        /// <summary>Reference maps: cells where the reference had the biome that is currently growing.</summary>
        bool[] affinity;
        float affinityCost = 1f;

        public override void Execute(MapGenerationContext context)
        {
            map = context.Layout;
            config = context.Config;
            settings = config.BiomeLayout;
            var biomes = config.Biomes;
            if (biomes == null || biomes.Length == 0)
                throw new InvalidOperationException("Map generation needs at least one biome.");

            int w = map.Width, h = map.Height, n = map.CellCount;
            for (int i = 0; i < n; i++) map.Biome[i] = MapLayout.NoBiome;

            waterDistance = map.ComputeDistanceToWater();
            var noise = new GradientNoise(context.NoiseSeed(501));
            var broadNoise = new GradientNoise(context.NoiseSeed(502));
            bufferNoise = new GradientNoise(context.NoiseSeed(503));
            borderCost = new float[n];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                float v = noise.Fractal(x, y, settings.borderNoise);
                float broad = broadNoise.Fractal(x, y, settings.broadBorderNoise);
                borderCost[i] = (float)Math.Exp((v - 0.5f) * settings.borderContrast + (broad - 0.5f) * settings.broadBorderContrast);
            }

            dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            heap = new MinHeap(1 << 16);
            locked = new bool[n];

            if (config.Source == MapSource.ReferenceMap && context.ReferenceBiomes != null)
            {
                var reference = config.ReferenceSettings;
                AssignReferenceBiomes(context);
                for (int pass = 0; pass < reference.biomeSmoothingPasses; pass++) SmoothBorders(0);
                MergeSmallPatches(reference.minBiomePatch);
                affinityCost = reference.referenceAffinity;
                foreach (int b in RegionalOrder(true)) GrowReferenceRegionalBiome(context, b);
            }
            else
            {
                foreach (int b in RegionalOrder(false)) GrowRegionalBiome(context, b, null);
                PlaceFillerBiomes(context);
            }

            for (int pass = 0; pass < settings.smoothingPasses; pass++) SmoothBorders(settings.smoothingMargin);
            MergeSmallPatches(settings.minPatchSize);
        }

        /// <summary>A regional biome may only take cells that are free or grassland, never another regional biome or a swamp.</summary>
        bool CanTake(int i)
        {
            if (locked[i]) return false;
            int b = map.Biome[i];
            if (b < 0) return true;
            var profile = config.Biomes[b];
            return profile == null || (profile.tags & BiomeTags.Grassland) != 0;
        }

        bool IsGrowable(int i) => map.Terrain[i] == CellTerrain.Land || map.Terrain[i] == CellTerrain.River;

        float PreferenceCost(BiomeGenerationProfile profile, int i)
        {
            int d = waterDistance[i];
            int min = Math.Min(profile.preferredWaterDistance.min, profile.preferredWaterDistance.max);
            int max = Math.Max(profile.preferredWaterDistance.min, profile.preferredWaterDistance.max);
            if (d < min) return 1f + profile.waterPreferenceStrength * (min - d) / Math.Max(1f, min);
            if (d > max) return 1f + profile.waterPreferenceStrength * Math.Min(3f, (d - max) / 10f);
            return 1f;
        }

        void ResetDistances()
        {
            foreach (int i in touched) dist[i] = float.PositiveInfinity;
            touched.Clear();
            heap.Clear();
        }

        // ------------------------------------------------------------------ regional biomes

        List<int> RegionalOrder(bool reference)
        {
            var biomes = config.Biomes;
            var order = new List<int>();
            for (int b = 0; b < biomes.Length; b++)
            {
                var p = biomes[b];
                if (p == null || p.mapCoverage <= 0f) continue;
                bool regional = reference ? p.referencePlacement == ReferencePlacement.RegionalQuota : p.placementMode == BiomePlacementMode.Regional;
                if (regional) order.Add(b);
            }
            order.Sort((first, second) =>
            {
                int c = biomes[first].placementPriority.CompareTo(biomes[second].placementPriority);
                return c != 0 ? c : first.CompareTo(second);
            });
            return order;
        }

        /// <summary>Cells closer than the repel distance to an already placed biome with conflicting tags.</summary>
        bool[] BuildBlockedMask(int biome)
        {
            var biomes = config.Biomes;
            var profile = biomes[biome];
            var blocked = new bool[map.CellCount];
            for (int other = 0; other < biomes.Length; other++)
            {
                if (other == biome || biomes[other] == null) continue;
                var o = biomes[other];
                int buffer = 0;
                if ((o.repelledTags & profile.tags) != 0) buffer = Math.Max(buffer, o.repelDistance);
                if ((profile.repelledTags & o.tags) != 0) buffer = Math.Max(buffer, profile.repelDistance);
                if (buffer <= 0) continue;

                bool any = false;
                for (int i = 0; i < map.CellCount && !any; i++) any = map.Biome[i] == other;
                if (!any) continue;

                short id = (short)other;
                // The buffer width wobbles (50%-150%) so the gap between the biomes has no straight edges
                int maxBuffer = (int)Math.Ceiling(buffer * 1.5f);
                var d = GridAlgorithms.DistanceField(map, i => map.Biome[i] == id, maxBuffer);
                for (int i = 0; i < blocked.Length; i++)
                {
                    if (d[i] > maxBuffer) continue;
                    float wobble = 1f + (bufferNoise.Fractal((i % map.Width) / 25f, (i / map.Width) / 25f, 3) - 0.5f) * 1.6f;
                    if (d[i] <= buffer * wobble) blocked[i] = true;
                }
            }
            return blocked;
        }

        /// <param name="preferredSeeds">Cells to start regions from first (reference areas); null = choose freely.</param>
        void GrowRegionalBiome(MapGenerationContext context, int biome, List<int> preferredSeeds)
        {
            var random = context.Random;
            var profile = config.Biomes[biome];
            var blocked = BuildBlockedMask(biome);
            int n = map.CellCount;

            int totalQuota = (int)Math.Round(profile.mapCoverage * n);
            int regionCount = Math.Max(1, profile.regionCount.Roll(random));
            var weights = new float[regionCount];
            float weightSum = 0f;
            for (int r = 0; r < regionCount; r++)
            {
                weights[r] = 1f + random.Range(-profile.regionSizeVariation, profile.regionSizeVariation);
                weightSum += weights[r];
            }

            var seeds = new List<int>();
            int carry = 0;
            // One extra round lets leftover quota (regions that got stuck on small islands) start a new region
            for (int r = 0; r < regionCount + 1; r++)
            {
                int quota = r < regionCount ? (int)(totalQuota * weights[r] / weightSum) + carry : carry;
                carry = 0;
                if (quota <= 0) continue;

                int seed = -1;
                while (preferredSeeds != null && preferredSeeds.Count > 0 && seed < 0)
                {
                    int candidate = preferredSeeds[0];
                    preferredSeeds.RemoveAt(0);
                    if (blocked[candidate] || !CanTake(candidate) || map.Terrain[candidate] != CellTerrain.Land) continue;
                    // Regions starting too close together would merge into one big region
                    if (DistanceToNearest(candidate, seeds) < settings.minRegionSeedSpacing) continue;
                    seed = candidate;
                }
                if (seed < 0) seed = ChooseSeed(random, profile, blocked, seeds);
                if (seed < 0)
                {
                    carry = quota;
                    continue;
                }
                seeds.Add(seed);
                int grown = GrowRegion(seed, (short)biome, quota, profile, blocked);
                carry = quota - grown;
            }
        }

        float DistanceToNearest(int cell, List<int> seeds)
        {
            int w = map.Width, x = cell % w, y = cell / w;
            float nearest = float.MaxValue;
            foreach (int s in seeds)
            {
                float dx = x - s % w, dy = y - s / w;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < nearest) nearest = d;
            }
            return nearest;
        }

        int ChooseSeed(DeterministicRandom random, BiomeGenerationProfile profile, bool[] blocked, List<int> seeds)
        {
            int w = map.Width, n = map.CellCount;
            int best = -1;
            float bestScore = float.MinValue;
            float spread = Math.Max(1f, settings.regionSpreadDistance);
            // Try more candidates when the map is crowded so a seed is still found
            for (int c = 0; c < settings.seedCandidates * 4 && (best < 0 || c < settings.seedCandidates); c++)
            {
                int i = random.Range(0, n);
                if (map.Terrain[i] != CellTerrain.Land || !CanTake(i) || blocked[i]) continue;

                int x = i % w, y = i / w;
                float nearest = spread;
                foreach (int s in seeds)
                {
                    float dx = x - s % w, dy = y - s / w;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < nearest) nearest = d;
                }
                float score = nearest / spread - (PreferenceCost(profile, i) - 1f) * 0.5f + random.NextFloat() * 0.2f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        int GrowRegion(int seed, short biome, int quota, BiomeGenerationProfile profile, bool[] blocked)
        {
            ResetDistances();
            int w = map.Width, h = map.Height;
            dist[seed] = 0f;
            touched.Add(seed);
            heap.Push(0f, seed);
            int grown = 0;

            while (grown < quota && heap.TryPop(out float d, out int i, out _))
            {
                if (d > dist[i] || !CanTake(i)) continue;
                if (map.Terrain[i] == CellTerrain.Land)
                {
                    map.Biome[i] = biome;
                    locked[i] = true;
                    grown++;
                }

                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (blocked[j] || !CanTake(j) || !IsGrowable(j)) continue;
                    float step = borderCost[j] * PreferenceCost(profile, j);
                    if (map.Terrain[j] == CellTerrain.River) step *= settings.riverCrossingCost;
                    if (affinity != null && affinity[j]) step *= affinityCost;
                    float nd = d + step;
                    if (nd >= dist[j]) continue;
                    if (float.IsPositiveInfinity(dist[j])) touched.Add(j);
                    dist[j] = nd;
                    heap.Push(nd, j);
                }
            }
            return grown;
        }

        // ------------------------------------------------------------------ reference maps

        void AssignReferenceBiomes(MapGenerationContext context)
        {
            var referenceBiomes = context.ReferenceBiomes;
            var mapping = config.ReferenceBiomeMap;
            var reference = config.ReferenceSettings;
            int biomeCount = config.Biomes.Length;
            int w = map.Width, h = map.Height;
            var warpX = new GradientNoise(context.NoiseSeed(511));
            var warpY = new GradientNoise(context.NoiseSeed(512));
            for (int i = 0; i < map.CellCount; i++)
            {
                if (map.Terrain[i] != CellTerrain.Land) continue;
                // Read the reference biome a little off-grid so polygon edges become organic borders
                int x = i % w, y = i / w, source = i;
                if (reference.biomeBorderWarp > 0f)
                {
                    int sx = x + (int)Math.Round((warpX.Fractal(x, y, reference.biomeBorderWarpNoise) - 0.5f) * 2f * reference.biomeBorderWarp);
                    int sy = y + (int)Math.Round((warpY.Fractal(x, y, reference.biomeBorderWarpNoise) - 0.5f) * 2f * reference.biomeBorderWarp);
                    if (sx >= 0 && sy >= 0 && sx < w && sy < h && referenceBiomes[sx + sy * w] != ReferenceBiome.Marine) source = sx + sy * w;
                }
                int r = (int)referenceBiomes[source];
                int b = mapping != null && r < mapping.Length ? mapping[r] : -1;
                map.Biome[i] = (short)(b >= 0 && b < biomeCount && config.Biomes[b] != null ? b : 0);
            }
        }

        /// <summary>
        /// Regrows a regional biome on a reference map: its reference areas become seeds (largest first), the old
        /// cells turn into the nearest grassland, then the biome grows back to its quota, preferring the cells the
        /// reference gave it. Missing regions start at good free spots.
        /// </summary>
        void GrowReferenceRegionalBiome(MapGenerationContext context, int biome)
        {
            int w = map.Width, n = map.CellCount;
            short id = (short)biome;

            // Reference areas of the biome, largest first; their most central cell is the seed
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Land && map.Biome[i] == id, sizes);
            affinity = new bool[n];
            var sumX = new double[sizes.Count];
            var sumY = new double[sizes.Count];
            for (int i = 0; i < n; i++)
            {
                int l = labels[i];
                if (l < 0) continue;
                affinity[i] = true;
                sumX[l] += i % w;
                sumY[l] += i / w;
            }
            var bestCell = new int[sizes.Count];
            var bestDistance = new double[sizes.Count];
            for (int l = 0; l < sizes.Count; l++) { bestCell[l] = -1; bestDistance[l] = double.MaxValue; }
            for (int i = 0; i < n; i++)
            {
                int l = labels[i];
                if (l < 0) continue;
                double dx = i % w - sumX[l] / sizes[l], dy = i / w - sumY[l] / sizes[l];
                double d = dx * dx + dy * dy;
                if (d < bestDistance[l]) { bestDistance[l] = d; bestCell[l] = i; }
            }
            var order = new List<int>();
            for (int l = 0; l < sizes.Count; l++) order.Add(l);
            order.Sort((p, q) => sizes[q].CompareTo(sizes[p]));
            var seeds = new List<int>();
            foreach (int l in order) seeds.Add(bestCell[l]);

            // The biome's current cells become the nearest grassland until it grows back
            RefillWithNearestGrassland(id);
            GrowRegionalBiome(context, biome, seeds);
            affinity = null;
        }

        void RefillWithNearestGrassland(short removed)
        {
            int w = map.Width, h = map.Height, n = map.CellCount;
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (map.Terrain[i] != CellTerrain.Land) continue;
                if (map.Biome[i] == removed) map.Biome[i] = MapLayout.NoBiome;
                else if (map.Biome[i] >= 0 && IsGrassland(map.Biome[i])) queue.Enqueue(i);
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
                    if (map.Terrain[j] != CellTerrain.Land || map.Biome[j] >= 0) continue;
                    map.Biome[j] = map.Biome[i];
                    queue.Enqueue(j);
                }
            }
            // Land with no grassland reachable (isolated islands) falls back to the base biome
            for (int i = 0; i < n; i++)
                if (map.Terrain[i] == CellTerrain.Land && map.Biome[i] < 0) map.Biome[i] = 0;
        }

        bool IsGrassland(int biome)
        {
            var profile = biome >= 0 && biome < config.Biomes.Length ? config.Biomes[biome] : null;
            return profile != null && (profile.tags & BiomeTags.Grassland) != 0;
        }

        // ------------------------------------------------------------------ filler biomes

        void PlaceFillerBiomes(MapGenerationContext context)
        {
            var random = context.Random;
            var biomes = config.Biomes;
            int w = map.Width, h = map.Height, n = map.CellCount;

            var fillers = new List<int>();
            var fillerWeights = new List<float>();
            for (int b = 0; b < biomes.Length; b++)
            {
                if (biomes[b] == null || biomes[b].placementMode != BiomePlacementMode.Filler) continue;
                fillers.Add(b);
                fillerWeights.Add(Math.Max(0f, biomes[b].fillerWeight));
            }
            if (fillers.Count == 0) fillers.Add(0); // fallback: base biome fills everything
            if (fillerWeights.Count != fillers.Count) fillerWeights.Add(1f);

            var remaining = new List<int>();
            for (int i = 0; i < n; i++)
                if (map.Terrain[i] == CellTerrain.Land && map.Biome[i] < 0) remaining.Add(i);
            if (remaining.Count == 0) return;

            ResetDistances();
            int seedCount = Math.Max(fillers.Count, remaining.Count / Math.Max(1, settings.fillerRegionSize));
            // Per seed: its biome and growth speed (tag of the heap entries = seed index)
            var seedBiome = new short[seedCount];
            var seedSpeed = new float[seedCount];
            for (int s = 0; s < seedCount; s++)
            {
                int i = remaining[random.Range(0, remaining.Count)];
                int pick = random.WeightedIndex(fillerWeights);
                seedBiome[s] = (short)fillers[pick < 0 ? 0 : pick];
                seedSpeed[s] = 1f + random.Range(-settings.fillerGrowthVariation, settings.fillerGrowthVariation);
                if (dist[i] == 0f) continue;
                dist[i] = 0f;
                touched.Add(i);
                heap.Push(0f, i, s);
            }

            while (heap.TryPop(out float d, out int i, out int seedIndex))
            {
                if (map.Biome[i] >= 0 || d > dist[i]) continue;
                if (map.Terrain[i] == CellTerrain.Land) map.Biome[i] = seedBiome[seedIndex];

                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (map.Biome[j] >= 0 || !IsGrowable(j)) continue;
                    float step = borderCost[j] / seedSpeed[seedIndex];
                    if (map.Terrain[j] == CellTerrain.River) step *= settings.riverCrossingCost;
                    float nd = d + step;
                    if (nd >= dist[j]) continue;
                    if (float.IsPositiveInfinity(dist[j])) touched.Add(j);
                    dist[j] = nd;
                    heap.Push(nd, j, seedIndex);
                }
            }
            ResetDistances();

            // Islands no seed reached get a random filler biome each
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Land && map.Biome[i] < 0, sizes);
            var islandBiome = new short[sizes.Count];
            for (int l = 0; l < sizes.Count; l++)
            {
                int pick = random.WeightedIndex(fillerWeights);
                islandBiome[l] = (short)fillers[pick < 0 ? 0 : pick];
            }
            for (int i = 0; i < n; i++)
                if (labels[i] >= 0) map.Biome[i] = islandBiome[labels[i]];
        }

        // ------------------------------------------------------------------ cleanup

        /// <summary>Mode filter: a land cell takes the most common biome around it if it clearly dominates.</summary>
        void SmoothBorders(int margin)
        {
            int w = map.Width, h = map.Height, n = map.CellCount;
            int r = settings.smoothingRadius;
            int biomeCount = config.Biomes.Length;
            var bestCount = new int[n];
            var bestBiome = new short[n];
            var ownCount = new int[n];
            var integral = new int[(w + 1) * (h + 1)];
            int iw = w + 1;

            for (int b = 0; b < biomeCount; b++)
            {
                // Summed area table of "land cell of biome b"
                for (int y = 0; y < h; y++)
                {
                    int rowSum = 0;
                    for (int x = 0; x < w; x++)
                    {
                        int i = x + y * w;
                        if (map.Terrain[i] == CellTerrain.Land && map.Biome[i] == b) rowSum++;
                        integral[(x + 1) + (y + 1) * iw] = integral[(x + 1) + y * iw] + rowSum;
                    }
                }

                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    if (map.Terrain[i] != CellTerrain.Land) continue;
                    int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r);
                    int x1 = Math.Min(w, x + r + 1), y1 = Math.Min(h, y + r + 1);
                    int c = integral[x1 + y1 * iw] - integral[x0 + y1 * iw] - integral[x1 + y0 * iw] + integral[x0 + y0 * iw];
                    if (map.Biome[i] == b) ownCount[i] = c;
                    if (c > bestCount[i])
                    {
                        bestCount[i] = c;
                        bestBiome[i] = (short)b;
                    }
                }
            }

            for (int i = 0; i < n; i++)
                if (map.Terrain[i] == CellTerrain.Land && bestCount[i] > ownCount[i] + margin && !locked[i])
                    map.Biome[i] = bestBiome[i];
        }

        /// <summary>Tiny biome patches look broken with the tile set; they join their most common neighbour.</summary>
        void MergeSmallPatches(int minPatchSize)
        {
            if (minPatchSize <= 1) return;
            int w = map.Width, h = map.Height, n = map.CellCount;
            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.Terrain[i] == CellTerrain.Land && map.Biome[i] >= 0, sizes,
                (p, q) => map.Biome[p] == map.Biome[q]);

            var patches = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int l = labels[i];
                if (l < 0 || sizes[l] >= minPatchSize) continue;
                if (!patches.TryGetValue(l, out var cells)) patches[l] = cells = new List<int>();
                cells.Add(i);
            }

            var counts = new int[config.Biomes.Length];
            foreach (var cells in patches.Values)
            {
                Array.Clear(counts, 0, counts.Length);
                short own = map.Biome[cells[0]];
                foreach (int i in cells)
                {
                    int x = i % w, y = i / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        short b = map.Biome[j];
                        if (map.Terrain[j] == CellTerrain.Land && b >= 0 && b != own) counts[b]++;
                    }
                }
                int best = -1, bestCount = 0;
                for (int b = 0; b < counts.Length; b++)
                    if (counts[b] > bestCount) { best = b; bestCount = counts[b]; }
                if (best < 0) continue;
                foreach (int i in cells) map.Biome[i] = (short)best;
            }
        }
    }
}
