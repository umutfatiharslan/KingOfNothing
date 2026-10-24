using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;
using UnityEngine;

namespace KingOfNothing.World.Generation
{
    public enum PlacementDistribution
    {
        /// <summary>Objects per 1000 eligible cells (trees, bushes, stones...).</summary>
        Density,
        /// <summary>A total number of objects between min and max (pumpkins, gold mines, skulls...).</summary>
        CountRange,
    }

    /// <summary>Inspector settings that decide where, how many and in which groups a spawn rule places objects.</summary>
    [Serializable]
    public class PlacementSettings
    {
        [Header("Amount")]
        [Tooltip("Density: objects per 1000 eligible cells. Count Range: a total number of objects.")]
        public PlacementDistribution distribution = PlacementDistribution.Density;
        [Tooltip("Density mode: expected objects per 1000 eligible cells (before spacing rejections).")]
        [Min(0f)] public float densityPer1000Cells = 5f;
        [Tooltip("Count Range mode: total number of objects of this rule (biome rule: in that biome on the whole map). Min may be 0.")]
        public IntRange count = new IntRange(5, 10);
        [Tooltip("Density mode: also keep the total between Count Limit min and max.")]
        public bool limitCount;
        public IntRange countLimit = new IntRange(0, 100000);
        [Tooltip("Chance that this rule places nothing at all on a map.")]
        [Range(0f, 1f)] public float absentChance;
        [Tooltip("Random swing of the amount per map: 0.5 = between 50% and 150% of the density / count.")]
        [Range(0f, 1f)] public float amountVariation = 0.25f;

        [Header("Clusters (objects placed together in a small area)")]
        [Tooltip("How many objects one cluster has (1-1 = single objects).")]
        public IntRange clusterSize = new IntRange(1, 1);
        [Tooltip("Radius (cells) of the area a cluster spreads over.")]
        [Min(0)] public int clusterRadius = 2;

        [Header("Natural distribution (forests, rock fields...)")]
        [Tooltip("Objects gather where this noise is high, leaving clearings elsewhere.")]
        public bool useClustering;
        public NoiseSettings clusterNoise = new NoiseSettings(26f, 3);
        [Range(0f, 1f)] public float clusterThreshold = 0.55f;
        [Tooltip("Width of the soft edge of a cluster (noise units).")]
        [Range(0.01f, 0.5f)] public float clusterFalloff = 0.1f;
        [Tooltip("Chance multiplier outside clusters (lone objects).")]
        [Range(0f, 1f)] public float outsideClusterFactor = 0.05f;

        [Header("Spacing")]
        [Tooltip("Free cells kept horizontally between two objects of THIS rule.")]
        [Min(0)] public int minSpacingX = 0;
        [Tooltip("Free cells kept vertically between two objects of THIS rule.")]
        [Min(0)] public int minSpacingY = 0;
        [Tooltip("Free cells kept to ANY other object.")]
        [Min(0)] public int minDistanceToOtherObjects = 0;
        [Tooltip("Land objects: allowed distance to water. Water objects: allowed distance to land.")]
        public IntRange shoreDistance = new IntRange(2, 9999);

        [Header("Order & look")]
        [Tooltip("Rules with a lower priority are placed first and get the best spots.")]
        public int priority = 10;
        [Tooltip("Random offset inside the cell so small objects do not line up on the grid.")]
        [Range(0f, 0.5f)] public float positionJitter = 0.3f;
    }

    /// <summary>One possible object of a rule (core side: just an id, a weight and a footprint).</summary>
    public sealed class PlacementVariant
    {
        public int VariantId;
        public float Weight = 1f;
        public int Width = 1;
        public int Height = 1;
        /// <summary>Variant placed instead of this one when the rule rolls a stump (-1 = none).</summary>
        public int StumpVariantId = -1;
    }

    /// <summary>Objects placed around another object (e.g. bones around a skull).</summary>
    public sealed class CompanionRule
    {
        public IntRange Count = new IntRange(0, 3);
        public int RingRadius = 1;
        public PlacementVariant[] Variants = Array.Empty<PlacementVariant>();
    }

    /// <summary>A spawn rule converted to plain data for the planner.</summary>
    public sealed class PlacementRule
    {
        public int RuleId;
        public string Label = "";
        public PlacementSettings Settings = new PlacementSettings();
        public TerrainMask Terrain = TerrainMask.Land;
        /// <summary>Allowed biomes indexed by biome id; null = any biome (and water cells without biome).</summary>
        public bool[] AllowedBiomes;
        public PlacementVariant[] Variants = Array.Empty<PlacementVariant>();
        /// <summary>Chance that a placed variant is replaced by its stump variant.</summary>
        public float StumpChance;
        public CompanionRule Companions;
    }

    /// <summary>Output of the planner: "put variant V of rule R at cell (X, Y)".</summary>
    public struct PlacementRequest
    {
        public int RuleId;
        public int VariantId;
        public int X, Y;
        public int Width, Height;
        public float OffsetX, OffsetY;
        public bool IsCompanion;
        public bool IsStump;
    }

    /// <summary>
    /// Decides where every map object goes, without creating anything. Deterministic for a given seed.
    /// Candidate cells are visited in random order and objects keep their spacing, so the result is
    /// natural and never grid-like; clustering noise creates forests and clearings.
    /// </summary>
    public sealed class ObjectPlacementPlanner
    {
        MapLayout map;
        DeterministicRandom random;
        int[] distanceToWater, distanceToLand;
        bool[] occupied;
        int[] spacingOwner;
        readonly List<PlacementRequest> requests = new List<PlacementRequest>();
        readonly List<float> weights = new List<float>();

        public IReadOnlyList<PlacementRequest> Requests => requests;

        public List<PlacementRequest> Plan(MapLayout layout, IList<PlacementRule> rules, int seed, ICollection<int> reservedCells = null)
        {
            map = layout;
            random = new DeterministicRandom(seed);
            int n = map.CellCount;
            distanceToWater = map.ComputeDistanceToWater();
            distanceToLand = map.ComputeDistanceToLand();
            occupied = new bool[n];
            spacingOwner = new int[n];
            for (int i = 0; i < n; i++) spacingOwner[i] = -1;
            requests.Clear();

            if (reservedCells != null)
                foreach (int c in reservedCells)
                    if (c >= 0 && c < n) occupied[c] = true;

            var ordered = new List<PlacementRule>(rules);
            ordered.Sort((p, q) =>
            {
                int c = p.Settings.priority.CompareTo(q.Settings.priority);
                return c != 0 ? c : p.RuleId.CompareTo(q.RuleId);
            });

            var candidates = new List<int>();
            foreach (var rule in ordered)
            {
                if (rule.Variants == null || rule.Variants.Length == 0) continue;
                CollectCandidates(rule, candidates);
                if (candidates.Count == 0) continue;
                random.Shuffle(candidates);
                PlaceRule(rule, candidates);
            }
            return new List<PlacementRequest>(requests);
        }

        // ------------------------------------------------------------------ eligibility

        bool CellEligible(PlacementRule rule, int i)
        {
            if (occupied[i]) return false;
            var terrain = map.Terrain[i];
            if (!rule.Terrain.Matches(terrain)) return false;
            if (rule.AllowedBiomes != null)
            {
                int b = map.Biome[i];
                if (b < 0 || b >= rule.AllowedBiomes.Length || !rule.AllowedBiomes[b]) return false;
            }
            int shore = terrain == CellTerrain.Land ? distanceToWater[i] : distanceToLand[i];
            return rule.Settings.shoreDistance.Contains(shore);
        }

        void CollectCandidates(PlacementRule rule, List<int> candidates)
        {
            candidates.Clear();
            for (int i = 0; i < map.CellCount; i++)
                if (CellEligible(rule, i)) candidates.Add(i);
        }

        float ClusterFactor(PlacementSettings s, GradientNoise noise, int x, int y)
        {
            if (!s.useClustering || noise == null) return 1f;
            float v = noise.Fractal(x, y, s.clusterNoise);
            float t = GridAlgorithms.SmoothStep(s.clusterThreshold, s.clusterThreshold + s.clusterFalloff, v);
            return s.outsideClusterFactor + (1f - s.outsideClusterFactor) * t;
        }

        // ------------------------------------------------------------------ placement

        void PlaceRule(PlacementRule rule, List<int> candidates)
        {
            var s = rule.Settings;
            int w = map.Width;
            var noise = s.useClustering ? new GradientNoise(unchecked(rule.RuleId * 7919 + 104729)) : null;

            // Randomness per map: sometimes none at all, otherwise more or less than usual
            if (s.absentChance > 0f && random.NextFloat() < s.absentChance) return;
            float abundance = Math.Max(0f, 1f + random.Range(-s.amountVariation, s.amountVariation));
            int minSize = Math.Max(1, Math.Min(s.clusterSize.min, s.clusterSize.max));
            int maxSize = Math.Max(minSize, Math.Max(s.clusterSize.min, s.clusterSize.max));
            float meanClusterSize = (minSize + maxSize) * 0.5f;

            int minCount = 0, maxCount = int.MaxValue;
            if (s.distribution == PlacementDistribution.CountRange)
            {
                minCount = maxCount = (int)Math.Round(s.count.Roll(random) * abundance);
            }
            else if (s.limitCount)
            {
                minCount = Math.Min(s.countLimit.min, s.countLimit.max);
                maxCount = Math.Max(s.countLimit.min, s.countLimit.max);
            }
            if (maxCount <= 0) return;

            int placed = 0;
            if (s.distribution == PlacementDistribution.Density)
            {
                // Chance per cell that a cluster starts here, so the object density stays as configured
                float clusterChance = s.densityPer1000Cells / 1000f * abundance / meanClusterSize;
                if (clusterChance > 0f)
                {
                    foreach (int i in candidates)
                    {
                        if (placed >= maxCount) break;
                        int x = i % w, y = i / w;
                        if (random.NextFloat() >= clusterChance * ClusterFactor(s, noise, x, y)) continue;
                        placed += PlaceCluster(rule, x, y, maxCount - placed, minSize, maxSize);
                    }
                }
            }

            // Count mode, or a density result below the minimum: sweep again, natural distribution first
            for (int sweep = 0; sweep < 4 && placed < minCount; sweep++)
            {
                foreach (int i in candidates)
                {
                    if (placed >= minCount) break;
                    int x = i % w, y = i / w;
                    if (sweep < 3 && random.NextFloat() > ClusterFactor(s, noise, x, y)) continue;
                    placed += PlaceCluster(rule, x, y, minCount - placed, minSize, maxSize);
                }
            }
        }

        /// <summary>Places one object at (x, y) and the rest of its cluster around it. Returns how many were placed.</summary>
        int PlaceCluster(PlacementRule rule, int x, int y, int remaining, int minSize, int maxSize)
        {
            if (remaining <= 0 || !TryPlace(rule, x, y)) return 0;
            int placed = 1;
            int size = Math.Min(remaining, random.RangeInclusive(minSize, maxSize));
            int radius = Math.Max(1, rule.Settings.clusterRadius);
            for (int k = 1; k < size; k++)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    // Uniform point in a disc around the first object
                    double angle = random.NextDouble() * Math.PI * 2.0;
                    double r = Math.Sqrt(random.NextDouble()) * radius;
                    int cx = x + (int)Math.Round(Math.Cos(angle) * r), cy = y + (int)Math.Round(Math.Sin(angle) * r);
                    if (!map.InBounds(cx, cy) || !TryPlace(rule, cx, cy)) continue;
                    placed++;
                    break;
                }
            }
            return placed;
        }

        PlacementVariant PickVariant(PlacementVariant[] variants)
        {
            weights.Clear();
            foreach (var v in variants) weights.Add(v.Weight);
            int index = random.WeightedIndex(weights);
            return index < 0 ? null : variants[index];
        }

        bool AreaFree(PlacementRule rule, int x, int y, int width, int height)
        {
            for (int yy = y; yy < y + height; yy++)
            for (int xx = x; xx < x + width; xx++)
            {
                if (!map.InBounds(xx, yy) || !CellEligible(rule, xx + yy * map.Width)) return false;
            }
            return true;
        }

        bool WindowContains(int x0, int y0, int x1, int y1, Func<int, bool> test)
        {
            int w = map.Width, h = map.Height;
            for (int yy = Math.Max(0, y0); yy <= Math.Min(h - 1, y1); yy++)
            for (int xx = Math.Max(0, x0); xx <= Math.Min(w - 1, x1); xx++)
                if (test(xx + yy * w)) return true;
            return false;
        }

        bool TryPlace(PlacementRule rule, int x, int y)
        {
            var variant = PickVariant(rule.Variants);
            if (variant == null) return false;
            var s = rule.Settings;
            int width = Math.Max(1, variant.Width), height = Math.Max(1, variant.Height);

            if (!AreaFree(rule, x, y, width, height)) return false;
            int id = rule.RuleId;
            if ((s.minSpacingX > 0 || s.minSpacingY > 0) &&
                WindowContains(x - s.minSpacingX, y - s.minSpacingY, x + width - 1 + s.minSpacingX, y + height - 1 + s.minSpacingY,
                    i => spacingOwner[i] == id))
                return false;
            int d = s.minDistanceToOtherObjects;
            if (d > 0 && WindowContains(x - d, y - d, x + width - 1 + d, y + height - 1 + d, i => occupied[i]))
                return false;

            Occupy(x, y, width, height, id);

            bool stump = variant.StumpVariantId >= 0 && rule.StumpChance > 0f && random.NextFloat() < rule.StumpChance;
            requests.Add(CreateRequest(rule, stump ? variant.StumpVariantId : variant.VariantId, x, y, width, height, false, stump));

            if (rule.Companions != null) PlaceCompanions(rule, x, y, width, height);
            return true;
        }

        void Occupy(int x, int y, int width, int height, int ruleId)
        {
            for (int yy = y; yy < y + height; yy++)
            for (int xx = x; xx < x + width; xx++)
            {
                int i = xx + yy * map.Width;
                occupied[i] = true;
                spacingOwner[i] = ruleId;
            }
        }

        PlacementRequest CreateRequest(PlacementRule rule, int variantId, int x, int y, int width, int height, bool companion, bool stump)
        {
            float jitter = rule.Settings.positionJitter;
            bool small = width == 1 && height == 1;
            return new PlacementRequest
            {
                RuleId = rule.RuleId,
                VariantId = variantId,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                OffsetX = small ? random.Range(-jitter, jitter) : 0f,
                OffsetY = small ? random.Range(0f, jitter) : 0f,
                IsCompanion = companion,
                IsStump = stump,
            };
        }

        /// <summary>Places companions on the ring of cells exactly RingRadius away from the footprint.</summary>
        void PlaceCompanions(PlacementRule rule, int x, int y, int width, int height)
        {
            var companions = rule.Companions;
            if (companions.Variants == null || companions.Variants.Length == 0) return;
            int wanted = companions.Count.Roll(random);
            if (wanted <= 0) return;

            int r = Math.Max(1, companions.RingRadius);
            var ring = new List<int>();
            for (int yy = y - r; yy <= y + height - 1 + r; yy++)
            for (int xx = x - r; xx <= x + width - 1 + r; xx++)
            {
                bool onRing = xx == x - r || yy == y - r || xx == x + width - 1 + r || yy == y + height - 1 + r;
                if (onRing && map.InBounds(xx, yy)) ring.Add(xx + yy * map.Width);
            }
            random.Shuffle(ring);

            int placed = 0;
            foreach (int c in ring)
            {
                if (placed >= wanted) break;
                if (!CellEligible(rule, c)) continue;
                var variant = PickVariant(companions.Variants);
                if (variant == null) break;
                int cx = c % map.Width, cy = c / map.Width;
                Occupy(cx, cy, 1, 1, -1);
                requests.Add(CreateRequest(rule, variant.VariantId, cx, cy, 1, 1, true, false));
                placed++;
            }
        }
    }
}
