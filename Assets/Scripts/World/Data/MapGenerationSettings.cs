using System;
using System.Collections.Generic;
using KingOfNothing.World.Generation;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingOfNothing.World
{
    /// <summary>Which game biome a biome of the reference (Azgaar) map becomes.</summary>
    [Serializable]
    public class ReferenceBiomeMapping
    {
        public ReferenceBiome source;
        public BiomeDefinition biome;

        public ReferenceBiomeMapping() { }

        public ReferenceBiomeMapping(ReferenceBiome source, BiomeDefinition biome)
        {
            this.source = source;
            this.biome = biome;
        }
    }

    /// <summary>
    /// The single place where (almost) every detail of the map is tuned: size, seed, map source, land share,
    /// lakes, rivers, biomes and the objects that are not tied to one biome. Assign it to a MapGenerator.
    /// </summary>
    [CreateAssetMenu(fileName = "MapGenerationSettings", menuName = "King of Nothing/World/Map Generation Settings")]
    public class MapGenerationSettings : ScriptableObject
    {
        [Header("Map")]
        [Tooltip("Map width in cells (1 cell = 64 px = 1 unit).")]
        [Min(64)] public int width = 1800;
        [Tooltip("Map height in cells.")]
        [Min(64)] public int height = 800;
        [Tooltip("Same seed = same map for every team member (decides objects, extra rivers, regional biome growth...).")]
        public int seed = 20261024;

        [Header("Source")]
        [Tooltip("Reference Map = the hand-made Azgaar map adjusted to the rules. Procedural = fully random from the seed.")]
        public MapSource mapSource = MapSource.ReferenceMap;
        public ReferenceMapAsset referenceMap;
        public ReferenceMapSettings referenceSettings = new ReferenceMapSettings();
        [Tooltip("Which biome every reference (Azgaar) biome becomes. Unlisted reference biomes use the first biome.")]
        public List<ReferenceBiomeMapping> referenceBiomeMapping = new List<ReferenceBiomeMapping>();

        [Header("Terrain")]
        public LandmassSettings landmass = new LandmassSettings();
        public LakeSettings lakes = new LakeSettings();
        public RiverSettings rivers = new RiverSettings();
        public WetlandSettings wetlands = new WetlandSettings();
        public LandBalanceSettings landBalance = new LandBalanceSettings();

        [Header("Biomes")]
        public BiomeLayoutSettings biomeLayout = new BiomeLayoutSettings();
        [Tooltip("All biomes. The FIRST one is painted under every land cell (base layer); the others are layered on top in list order.")]
        public List<BiomeDefinition> biomes = new List<BiomeDefinition>();

        [Header("Objects")]
        [Tooltip("Rules that span several biomes (gold mines, pumpkins, bones, water rocks...). Biome specific rules live in each biome.")]
        public List<GlobalSpawnRule> globalSpawnRules = new List<GlobalSpawnRule>();
        [Tooltip("Objects that are not spawned by any rule but can be placed with the developer tools.")]
        public List<SpawnableDefinition> extraDeveloperObjects = new List<SpawnableDefinition>();
        public StartAreaSettings startArea = new StartAreaSettings();

        [Header("Rendering")]
        [Tooltip("Animated foam tile drawn under the coast line.")]
        public TileBase waterFoamTile;
        [Tooltip("Water is not tiled; the camera background shows through. Should match the Tiny Swords water colour.")]
        public Color waterColor = new Color32(71, 171, 169, 255);
        [Tooltip("Sorting order of the base ground layer. Foam is one below, biome layers are stacked above.")]
        public int groundSortingOrder = -40;

        /// <summary>Plain data copy of these settings for the generation core (loads the reference map if needed).</summary>
        public MapGenerationConfig ToConfig(int seedOverride)
        {
            var config = new MapGenerationConfig
            {
                Width = width,
                Height = height,
                Seed = seedOverride,
                Source = mapSource,
                ReferenceSettings = referenceSettings,
                Landmass = landmass,
                Lakes = lakes,
                Rivers = rivers,
                BiomeLayout = biomeLayout,
                Wetlands = wetlands,
                LandBalance = landBalance,
                Biomes = new BiomeGenerationProfile[biomes.Count],
                BiomeNames = new string[biomes.Count],
            };
            for (int i = 0; i < biomes.Count; i++)
            {
                config.Biomes[i] = biomes[i] != null ? biomes[i].Generation : null;
                config.BiomeNames[i] = biomes[i] != null ? biomes[i].DisplayName : "(missing)";
            }

            config.ReferenceBiomeMap = new int[ReferenceMapData.BiomeCount];
            for (int r = 0; r < config.ReferenceBiomeMap.Length; r++) config.ReferenceBiomeMap[r] = -1;
            foreach (var m in referenceBiomeMapping)
            {
                if (m == null) continue;
                int r = (int)m.source;
                if (r >= 0 && r < config.ReferenceBiomeMap.Length) config.ReferenceBiomeMap[r] = biomes.IndexOf(m.biome);
            }

            if (mapSource == MapSource.ReferenceMap && referenceMap != null)
                config.Reference = referenceMap.GetData(width, height, referenceSettings);
            return config;
        }

        /// <summary>Every object the map can contain (rules, companions, stumps, drops, extra developer objects).</summary>
        public List<SpawnableDefinition> CollectAllSpawnables()
        {
            var result = new List<SpawnableDefinition>();
            void Add(SpawnableDefinition s)
            {
                if (s == null || result.Contains(s)) return;
                result.Add(s);
                Add(s.Stump);
                // Items it drops (wood, gold, stones...) must be known to rebuild the edited map
                if (s.Drops != null) foreach (var d in s.Drops) if (d != null) Add(d.item);
            }
            void AddRule(SpawnRule rule)
            {
                if (rule == null) return;
                if (rule.variants != null) foreach (var v in rule.variants) if (v != null) Add(v.spawnable);
                if (rule.companions != null && rule.companions.variants != null)
                    foreach (var v in rule.companions.variants) if (v != null) Add(v.spawnable);
            }
            foreach (var biome in biomes)
                if (biome != null) foreach (var rule in biome.SpawnRules) AddRule(rule);
            foreach (var rule in globalSpawnRules) AddRule(rule);
            foreach (var s in extraDeveloperObjects) Add(s);
            return result;
        }

        /// <summary>Returns a list of problems that would break generation (empty = OK).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (biomes == null || biomes.Count == 0) problems.Add("No biomes assigned.");
            else
            {
                for (int i = 0; i < biomes.Count; i++)
                {
                    if (biomes[i] == null) problems.Add($"Biome slot {i} is empty.");
                    else if (biomes[i].GroundTiles == null) problems.Add($"Biome '{biomes[i].DisplayName}' has no Ground Tile Set.");
                }
                float regional = 0f;
                foreach (var b in biomes)
                {
                    if (b == null) continue;
                    bool isRegional = mapSource == MapSource.ReferenceMap
                        ? b.Generation.referencePlacement == ReferencePlacement.RegionalQuota
                        : b.Generation.placementMode == BiomePlacementMode.Regional;
                    if (isRegional) regional += b.Generation.mapCoverage;
                }
                if (regional >= landmass.landRatio)
                    problems.Add($"Regional biomes cover {regional:P0} of the map but land is only {landmass.landRatio:P0}.");
            }
            if (biomes != null && biomes.Count > short.MaxValue) problems.Add("Too many biomes.");
            if (mapSource == MapSource.ReferenceMap && referenceMap == null)
                problems.Add("Map Source is Reference Map but no Reference Map asset is assigned.");
            return problems;
        }
    }
}
