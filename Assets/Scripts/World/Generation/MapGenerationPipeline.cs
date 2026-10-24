using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>Shared state passed through every generation pass.</summary>
    public sealed class MapGenerationContext
    {
        public readonly MapGenerationConfig Config;
        public readonly MapLayout Layout;
        public readonly DeterministicRandom Random;
        public readonly int Seed;
        public readonly MapGenerationReport Report = new MapGenerationReport();

        /// <summary>Noisy distance of every land cell to the sea along the drainage network (DrainagePass).</summary>
        public float[] DrainageDistance;
        /// <summary>Next cell downstream (towards the sea) for every land cell; -1 at the sea (DrainagePass).</summary>
        public int[] DrainageNext;
        /// <summary>Cells that must stay sea (e.g. the noisy band along the map edge); null = none.</summary>
        public bool[] ForcedSea;
        /// <summary>Reference biome of every land cell (reference maps only; filled by ReferenceTerrainPass).</summary>
        public ReferenceBiome[] ReferenceBiomes;

        public MapGenerationContext(MapGenerationConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Seed = config.Seed;
            Layout = new MapLayout(config.Width, config.Height);
            Random = new DeterministicRandom(config.Seed);
        }

        /// <summary>
        /// Seed for a noise layer that only depends on the map seed and a fixed salt, so changing one pass
        /// does not reshuffle the noise of the others.
        /// </summary>
        public int NoiseSeed(int salt) => unchecked(Seed * 73856093 ^ salt * 19349663 + 0x5bd1e995);
    }

    /// <summary>One step of the map generation. Passes run in order and each refines the layout.</summary>
    public abstract class MapGenerationPass
    {
        public abstract string Name { get; }

        public abstract void Execute(MapGenerationContext context);
    }

    /// <summary>Runs the generation passes and measures them.</summary>
    public sealed class MapGenerationPipeline
    {
        readonly List<MapGenerationPass> passes = new List<MapGenerationPass>();

        public IReadOnlyList<MapGenerationPass> Passes => passes;

        /// <summary>The standard pipeline for the given map source.</summary>
        public static MapGenerationPipeline CreateFor(MapGenerationConfig config) =>
            config != null && config.Source == MapSource.ReferenceMap ? CreateReference() : CreateDefault();

        /// <summary>Reference map: land, coasts, big rivers and biome areas come from the hand-made map.</summary>
        public static MapGenerationPipeline CreateReference()
        {
            var pipeline = new MapGenerationPipeline();
            pipeline.Add(new ReferenceTerrainPass());
            pipeline.Add(new LakePass());
            pipeline.Add(new DrainagePass());
            pipeline.Add(new RiverPass());
            pipeline.Add(new BiomeLayoutPass());
            pipeline.Add(new WetlandPass());
            pipeline.Add(new LandBalancePass());
            return pipeline;
        }

        /// <summary>Fully procedural pipeline.</summary>
        public static MapGenerationPipeline CreateDefault()
        {
            var pipeline = new MapGenerationPipeline();
            pipeline.Add(new LandmassPass());
            pipeline.Add(new LakePass());
            pipeline.Add(new DrainagePass());
            pipeline.Add(new RiverPass());
            pipeline.Add(new BiomeLayoutPass());
            pipeline.Add(new WetlandPass());
            pipeline.Add(new LandBalancePass());
            return pipeline;
        }

        public MapGenerationPipeline Add(MapGenerationPass pass)
        {
            passes.Add(pass);
            return this;
        }

        public MapGenerationPipeline Insert(int index, MapGenerationPass pass)
        {
            passes.Insert(index, pass);
            return this;
        }

        /// <param name="progress">Optional callback (pass name, 0-1) for progress bars.</param>
        public MapGenerationContext Run(MapGenerationConfig config, Action<string, float> progress = null)
        {
            var context = new MapGenerationContext(config);
            var watch = new Stopwatch();
            for (int i = 0; i < passes.Count; i++)
            {
                progress?.Invoke(passes[i].Name, i / (float)passes.Count);
                watch.Restart();
                passes[i].Execute(context);
                context.Report.PassTimes.Add(new KeyValuePair<string, double>(passes[i].Name, watch.Elapsed.TotalMilliseconds));
            }
            progress?.Invoke("Done", 1f);
            context.Report.Measure(context);
            return context;
        }
    }

    /// <summary>Statistics of a generated map, printed to the console after generation.</summary>
    public sealed class MapGenerationReport
    {
        public readonly List<KeyValuePair<string, double>> PassTimes = new List<KeyValuePair<string, double>>();
        public float LandRatio;
        public int LakeCount, RiverCount;
        public int SeaCells, LakeCells, RiverCells, PondCells, MarshCells;
        public float[] BiomeMapCoverage = Array.Empty<float>();
        public int[] BiomeRegionCount = Array.Empty<int>();
        public int[] BiomeLargestRegion = Array.Empty<int>();
        public string[] BiomeNames = Array.Empty<string>();

        /// <summary>Regions smaller than this are not counted in BiomeRegionCount.</summary>
        public const int MinCountedRegion = 500;

        public void Measure(MapGenerationContext context)
        {
            var map = context.Layout;
            int n = map.CellCount;
            LandRatio = map.LandRatio;
            LakeCount = map.Lakes.Count;
            RiverCount = map.Rivers.Count;
            SeaCells = map.Count(CellTerrain.Sea);
            LakeCells = map.Count(CellTerrain.Lake);
            RiverCells = map.Count(CellTerrain.River);
            PondCells = map.Count(CellTerrain.Pond);
            MarshCells = map.Count(CellTerrain.Marsh);

            int biomeCount = context.Config.Biomes.Length;
            BiomeNames = context.Config.BiomeNames;
            var counts = map.CountBiomes(biomeCount);
            BiomeMapCoverage = new float[biomeCount];
            BiomeRegionCount = new int[biomeCount];
            BiomeLargestRegion = new int[biomeCount];
            for (int b = 0; b < biomeCount; b++) BiomeMapCoverage[b] = counts[b] / (float)n;

            var sizes = new List<int>();
            var labels = GridAlgorithms.LabelRegions(map, i => map.IsLand(i) && map.Biome[i] >= 0, sizes,
                (p, q) => map.Biome[p] == map.Biome[q]);
            var seen = new bool[sizes.Count];
            for (int i = 0; i < n; i++)
            {
                int l = labels[i];
                if (l < 0 || seen[l]) continue;
                seen[l] = true;
                int b = map.Biome[i];
                if (b < 0 || b >= biomeCount) continue;
                if (sizes[l] >= MinCountedRegion) BiomeRegionCount[b]++;
                if (sizes[l] > BiomeLargestRegion[b]) BiomeLargestRegion[b] = sizes[l];
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Land: {LandRatio * 100f:F2}% of the map | lakes: {LakeCount} | rivers: {RiverCount}");
            sb.AppendLine($"Water cells - sea: {SeaCells}, lake: {LakeCells}, river: {RiverCells}, pond: {PondCells}, marsh: {MarshCells}");
            for (int b = 0; b < BiomeMapCoverage.Length; b++)
            {
                string name = b < BiomeNames.Length ? BiomeNames[b] : $"Biome {b}";
                sb.AppendLine($"  {name}: {BiomeMapCoverage[b] * 100f:F2}% of the map, {BiomeRegionCount[b]} regions (>= {MinCountedRegion} cells), largest {BiomeLargestRegion[b]} cells");
            }
            sb.Append("Pass times (ms): ");
            foreach (var t in PassTimes) sb.Append($"{t.Key} {t.Value:F0}  ");
            return sb.ToString();
        }
    }
}
