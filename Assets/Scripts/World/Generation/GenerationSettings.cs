using System;
using KingOfNothing.Utilities;
using UnityEngine;

namespace KingOfNothing.World.Generation
{
    /// <summary>Climate / character tags of a biome; used by rules such as "no snow around the desert".</summary>
    [Flags]
    public enum BiomeTags
    {
        None = 0,
        Arid = 1 << 0,
        Snowy = 1 << 1,
        Wet = 1 << 2,
        Rocky = 1 << 3,
        Grassland = 1 << 4,
    }

    public enum BiomePlacementMode
    {
        /// <summary>Fills whatever land the regional biomes left over, as many medium sized regions.</summary>
        Filler,
        /// <summary>A fixed share of the map split into a fixed number of large regions (desert, rock, swamp).</summary>
        Regional,
    }

    /// <summary>Where the land, coasts, big rivers and biome areas come from.</summary>
    public enum MapSource
    {
        /// <summary>The hand-made reference map (Azgaar .map file), adjusted to the rules.</summary>
        ReferenceMap,
        /// <summary>Fully procedural from the seed.</summary>
        Procedural,
    }

    /// <summary>How a biome behaves when the map comes from the reference map.</summary>
    public enum ReferencePlacement
    {
        /// <summary>Keep the areas the reference map gives this biome.</summary>
        UseReferenceAreas,
        /// <summary>Grow the biome to its Map Coverage / Region Count, starting from the reference's own areas.</summary>
        RegionalQuota,
    }

    // ==================================================================== Reference map

    [Serializable]
    public class ReferenceMapSettings
    {
        [Tooltip("Only this many rivers of the reference (largest discharge first) are used.")]
        [Min(0)] public int maxRivers = 20;
        [Tooltip("The reference has less land than Land Ratio; the sea level is lowered (shallow sea becomes land) " +
                 "following the reference heights plus this noise, so the new coasts are irregular.")]
        public NoiseSettings coastNoise = new NoiseSettings(90f, 3);
        [Min(0f)] public float coastNoiseStrength = 10f;
        public NoiseSettings coastDetailNoise = new NoiseSettings(9f, 2);
        [Min(0f)] public float coastDetailStrength = 4f;
        [Range(0, 6)] public int coastSmoothingPasses = 2;
        [Tooltip("Sideways bend (cells) applied to the reference river outlines when the map is imported, so no " +
                 "stretch is straight. 0 = as drawn. Changing it re-imports the reference map.")]
        [Min(0f)] public float riverWarp = 3f;
        [Tooltip("Length (cells) of one bend of the reference rivers.")]
        [Min(2f)] public float riverWarpScale = 22f;
        [Tooltip("Width (cells) of the part added to a river when its mouth is extended to the new coast.")]
        [Min(1)] public int riverExtensionWidth = 3;
        [Tooltip("Cells along the map border that are always sea.")]
        [Min(1)] public int borderMargin = 2;
        [Tooltip("Land that reaches the map border is cut back by a noisy sea band of this width (cells), " +
                 "so no coast follows the straight map edge.")]
        [Min(0)] public int edgeSeaBand = 8;
        [Tooltip("Extra, noisy width of the edge sea band (cells): bays and capes along the map edge.")]
        [Min(0f)] public float edgeSeaBandNoise = 45f;
        public NoiseSettings edgeSeaBandNoiseSettings = new NoiseSettings(45f, 4);
        [Tooltip("Noise that bends the rivers extended to the new coast (higher contrast = more bends).")]
        public NoiseSettings riverExtensionNoise = new NoiseSettings(25f, 3);
        [Min(0f)] public float riverExtensionContrast = 7f;
        [Tooltip("Islands smaller than this (cells) are removed.")]
        [Min(0)] public int minIslandSize = 225;
        [Tooltip("Sea holes smaller than this (cells) inside the land are filled.")]
        [Min(0)] public int minSeaHoleSize = 9;

        [Header("Biomes from the reference")]
        [Tooltip("The reference biomes are straight-edged polygons; they are read through this noise warp (cells) " +
                 "so the borders become organic while the areas stay in place.")]
        [Min(0f)] public float biomeBorderWarp = 14f;
        public NoiseSettings biomeBorderWarpNoise = new NoiseSettings(35f, 3);
        [Tooltip("Smoothing of the reference biome borders (they are blocky after rasterisation).")]
        [Range(0, 10)] public int biomeSmoothingPasses = 4;
        [Tooltip("Reference biome patches smaller than this (cells) join their neighbour.")]
        [Min(0)] public int minBiomePatch = 180;
        [Tooltip("Regional quota biomes grow this much cheaper over cells where the reference already has them " +
                 "(lower = follow the reference shapes more closely).")]
        [Range(0.05f, 1f)] public float referenceAffinity = 0.35f;
    }

    // ==================================================================== Landmass

    [Serializable]
    public class LandmassSettings
    {
        [Tooltip("Share of the whole map that ends up as land (water inside the land does not count).")]
        [Range(0.05f, 0.95f)] public float landRatio = 0.40f;

        [Tooltip("Extra land generated first; lakes and rivers eat into it and the coast is balanced back to Land Ratio at the end.")]
        [Range(0f, 0.2f)] public float initialOvershoot = 0.03f;

        [Tooltip("Continent height noise. Scale = typical continent size in cells.")]
        public NoiseSettings elevationNoise = new NoiseSettings(360f, 6);

        [Tooltip("Domain warp noise: bends the continents so coasts are never straight.")]
        public NoiseSettings warpNoise = new NoiseSettings(300f, 3);
        [Min(0f)] public float warpStrength = 140f;

        [Header("Map edge falloff (keeps land away from the map border)")]
        [Tooltip("Normalised distance from the centre (0 = centre, 1 = edge) where the land starts to sink.")]
        [Range(0f, 1f)] public float edgeFalloffStart = 0.66f;
        [Tooltip("Normalised distance where the land is fully pushed under the sea.")]
        [Range(0f, 1.2f)] public float edgeFalloffEnd = 0.97f;
        [Tooltip("How strongly the edge pushes land down.")]
        [Min(0f)] public float edgeFalloffStrength = 0.7f;
        [Tooltip("Shape of the falloff: 2 = ellipse, higher = closer to a rounded rectangle.")]
        [Range(1f, 10f)] public float edgeShapeExponent = 3f;
        [Tooltip("Noise added to the falloff border so the outer coast is irregular.")]
        public NoiseSettings edgeNoise = new NoiseSettings(160f, 4);
        [Range(0f, 1f)] public float edgeNoiseStrength = 0.22f;
        [Tooltip("Cells along the map border that are always sea.")]
        [Min(1)] public int borderMargin = 8;

        [Header("Cleanup")]
        [Range(0, 8)] public int coastSmoothingPasses = 3;
        [Tooltip("Islands smaller than this (cells) are removed.")]
        [Min(0)] public int minIslandSize = 600;
        [Tooltip("Enclosed water smaller than this (cells) is filled. Real lakes are added later by the lake settings.")]
        [Min(0)] public int minInlandWaterSize = 300;
    }

    // ==================================================================== Lakes

    [Serializable]
    public class LakeSettings
    {
        public bool enabled = true;
        [Tooltip("Number of lakes on the whole map.")]
        public IntRange count = new IntRange(10, 16);
        [Tooltip("Main radius of a lake in cells.")]
        public FloatRange radius = new FloatRange(6f, 26f);
        [Tooltip("Above 1 = most lakes are small and a few are big.")]
        [Range(0.2f, 5f)] public float smallLakeBias = 2.2f;
        [Tooltip("Number of overlapping blobs a lake is made of (more = more irregular).")]
        public IntRange lobes = new IntRange(1, 3);
        [Tooltip("Shore noise amount.")]
        [Range(0f, 1f)] public float shoreWobble = 0.35f;
        [Min(0)] public int minDistanceToCoast = 14;
        [Min(0)] public int minDistanceBetweenLakes = 70;

        [Header("Rivers flowing out of lakes")]
        [Tooltip("Chance that a lake has outlet rivers at all.")]
        [Range(0f, 1f)] public float outletChance = 0.7f;
        [Tooltip("Number of rivers leaving a lake that has outlets.")]
        public IntRange outletsPerLake = new IntRange(1, 2);
    }

    // ==================================================================== Rivers

    [Serializable]
    public class RiverSettings
    {
        public bool enabled = true;

        [Header("Drainage (decides the course of every river)")]
        [Tooltip("Terrain noise rivers flow around. Smaller scale = more bends.")]
        public NoiseSettings courseNoise = new NoiseSettings(45f, 4);
        [Tooltip("How strongly rivers avoid the high noise areas. Higher = curvier rivers.")]
        [Min(0f)] public float courseContrast = 9f;
        public NoiseSettings courseWarpNoise = new NoiseSettings(120f, 2);
        [Min(0f)] public float courseWarpStrength = 80f;

        [Header("Sources")]
        [Tooltip("Rivers that start in the highlands (in addition to lake outlets).")]
        public IntRange sourceCount = new IntRange(12, 18);
        [Tooltip("Minimum drainage distance to the sea of a source; higher = longer rivers.")]
        [Min(0f)] public float minSourceDistanceToSea = 90f;
        [Min(0f)] public float minDistanceBetweenSources = 80f;
        [Tooltip("Rivers shorter than this (cells) are discarded.")]
        [Min(2)] public int minRiverLength = 70;

        [Header("Branches (tributaries)")]
        [Tooltip("Tributaries per highland river. They join the main river (branching / veins).")]
        public IntRange tributariesPerRiver = new IntRange(1, 3);
        [Tooltip("Distance of a tributary source from its main river.")]
        public FloatRange tributaryDistance = new FloatRange(25f, 70f);
        [Min(2)] public int minTributaryLength = 20;
        [Tooltip("Branches added to rivers that already exist before the river pass (the reference map's rivers).")]
        public IntRange existingRiverBranches = new IntRange(10, 16);

        [Header("Width")]
        [Tooltip("Width (cells) at the source.")]
        [Min(1)] public int sourceWidth = 1;
        [Tooltip("Maximum width (cells), reached near the mouth of long rivers.")]
        [Min(1)] public int maxWidth = 6;
        [Tooltip("Every [min, max] cells of river length the width changes by Width Step " +
                 "(widens going downstream = narrows going upstream).")]
        public IntRange widthStepLength = new IntRange(30, 55);
        [Min(1)] public int widthStep = 1;
        [Tooltip("Extra width below the point where two rivers meet.")]
        [Min(0)] public int confluenceBonus = 1;

        [Header("Meanders")]
        [Tooltip("Sideways swing of the river in cells.")]
        [Min(0f)] public float meanderAmplitude = 6f;
        [Tooltip("Length of one swing in cells.")]
        [Min(1f)] public float meanderWavelength = 55f;
        [Tooltip("Smoothing of the raw course before meanders are applied.")]
        [Range(0, 20)] public int courseSmoothing = 7;
    }

    // ==================================================================== Biome layout

    [Serializable]
    public class BiomeLayoutSettings
    {
        [Tooltip("Noise that makes region borders irregular instead of straight lines.")]
        public NoiseSettings borderNoise = new NoiseSettings(40f, 4);
        [Tooltip("Higher = more irregular, lobed regions.")]
        [Min(0f)] public float borderContrast = 9f;
        [Tooltip("Large scale noise that bends long borders between regions (prevents straight lines).")]
        public NoiseSettings broadBorderNoise = new NoiseSettings(150f, 3);
        [Min(0f)] public float broadBorderContrast = 3f;
        [Tooltip("Filler regions grow at slightly different speeds, which curves the borders between them.")]
        [Range(0f, 0.8f)] public float fillerGrowthVariation = 0.35f;
        [Tooltip("Average size (cells) of one filler (grass) region.")]
        [Min(100)] public int fillerRegionSize = 9000;
        [Tooltip("Cost multiplier for a region to grow across a river; high = rivers often separate biomes.")]
        [Min(1f)] public float riverCrossingCost = 4f;
        [Tooltip("Random candidates tried when choosing where a regional biome starts.")]
        [Min(1)] public int seedCandidates = 80;
        [Tooltip("Regions of the same biome try to start at least this far (cells) from each other.")]
        [Min(1f)] public float regionSpreadDistance = 400f;
        [Tooltip("Two regions of the same biome never start closer than this (cells), so they do not merge.")]
        [Min(0f)] public float minRegionSeedSpacing = 70f;

        [Header("Smoothing")]
        [Range(0, 6)] public int smoothingPasses = 2;
        [Range(1, 4)] public int smoothingRadius = 2;
        [Tooltip("A cell changes biome only if the winning biome has this many more neighbours than its own.")]
        [Min(0)] public int smoothingMargin = 3;
        [Tooltip("Biome patches smaller than this (cells) are merged into their neighbour.")]
        [Min(0)] public int minPatchSize = 40;
    }

    // ==================================================================== Ponds & marsh

    [Serializable]
    public class WetlandSettings
    {
        [Tooltip("Pond radius in cells.")]
        public FloatRange pondRadius = new FloatRange(1f, 3.2f);
        [Tooltip("Ponds keep at least this distance (cells) from other water.")]
        [Min(1)] public int pondMinDistanceToWater = 5;
        [Tooltip("Noise of the pools inside swamps. Smaller scale = more, smaller pools.")]
        public NoiseSettings marshNoise = new NoiseSettings(7f, 2);
        [Tooltip("Swamp pools stay this far (cells) from the swamp border so the biome edge stays clean.")]
        [Min(0)] public int marshEdgeMargin = 3;
    }

    // ==================================================================== Final land balance

    [Serializable]
    public class LandBalanceSettings
    {
        public bool enabled = true;
        [Tooltip("Allowed difference from the target land ratio (fraction of the map).")]
        [Range(0.0001f, 0.02f)] public float tolerance = 0.0005f;
        [Range(1, 1000)] public int maxIterations = 400;
        [Tooltip("Share of the coastal cells changed per iteration. Lower = the coast moves more evenly.")]
        [Range(0.05f, 1f)] public float coastLayerFraction = 0.5f;
        public NoiseSettings coastNoise = new NoiseSettings(25f, 2);
        [Range(0f, 0.5f)] public float coastNoiseStrength = 0.04f;
        [Tooltip("Tiny islands left after balancing (cells) are removed.")]
        [Min(0)] public int minIslandSize = 30;
    }

    // ==================================================================== Per-biome generation profile

    /// <summary>How and where a biome is laid out. Lives inside BiomeDefinition.</summary>
    [Serializable]
    public class BiomeGenerationProfile
    {
        [Tooltip("Procedural maps: filler (shares the leftover land) or regional (fixed share of the map).")]
        public BiomePlacementMode placementMode = BiomePlacementMode.Filler;
        [Tooltip("Reference maps: keep the reference areas, or grow the biome to Map Coverage / Region Count.")]
        public ReferencePlacement referencePlacement = ReferencePlacement.UseReferenceAreas;
        public BiomeTags tags = BiomeTags.Grassland;

        [Header("Filler")]
        [Tooltip("Relative share of the leftover land (filler biomes only).")]
        [Min(0f)] public float fillerWeight = 1f;

        [Header("Regional")]
        [Tooltip("Share of the WHOLE map covered by this biome (e.g. 0.05 = 5% of the map).")]
        [Range(0f, 0.5f)] public float mapCoverage = 0.05f;
        [Tooltip("Number of separate regions the coverage is split into.")]
        public IntRange regionCount = new IntRange(3, 5);
        [Tooltip("How much region sizes differ from each other.")]
        [Range(0f, 0.9f)] public float regionSizeVariation = 0.3f;
        [Tooltip("Regional biomes are placed in ascending priority order.")]
        public int placementPriority;
        [Tooltip("Preferred distance (cells) to water. Growth outside this range is more expensive.")]
        public IntRange preferredWaterDistance = new IntRange(0, 9999);
        [Min(0f)] public float waterPreferenceStrength = 1f;

        [Header("Neighbour rules")]
        [Tooltip("Biomes carrying any of these tags are never placed within Repel Distance of this biome.")]
        public BiomeTags repelledTags = BiomeTags.None;
        [Min(0)] public int repelDistance = 12;

        [Header("Water inside the biome")]
        [Tooltip("Small ponds per 10,000 cells of this biome.")]
        [Min(0f)] public float pondsPer10000Cells = 1f;
        [Tooltip("Share of the biome turned into small marsh pools (swamps).")]
        [Range(0f, 0.8f)] public float marshWaterRatio;
    }

    /// <summary>Everything the core generator needs, independent of Unity assets.</summary>
    public sealed class MapGenerationConfig
    {
        public int Width = 1800;
        public int Height = 800;
        public int Seed;
        public MapSource Source = MapSource.Procedural;
        /// <summary>Required when Source is ReferenceMap; must have the same size as the map.</summary>
        public ReferenceMapData Reference;
        public ReferenceMapSettings ReferenceSettings = new ReferenceMapSettings();
        /// <summary>Reference biome (index = ReferenceBiome value) to biome id; -1 = use biome 0.</summary>
        public int[] ReferenceBiomeMap = new int[ReferenceMapData.BiomeCount];
        public LandmassSettings Landmass = new LandmassSettings();
        public LakeSettings Lakes = new LakeSettings();
        public RiverSettings Rivers = new RiverSettings();
        public BiomeLayoutSettings BiomeLayout = new BiomeLayoutSettings();
        public WetlandSettings Wetlands = new WetlandSettings();
        public LandBalanceSettings LandBalance = new LandBalanceSettings();
        /// <summary>Index = biome id. Index 0 is the base ground layer.</summary>
        public BiomeGenerationProfile[] Biomes = Array.Empty<BiomeGenerationProfile>();
        public string[] BiomeNames = Array.Empty<string>();
    }
}
