using System.Collections.Generic;
using KingOfNothing.World.Generation;

namespace KingOfNothing.World
{
    /// <summary>
    /// Converts the spawn rules of the settings and biomes into plain planner rules and keeps the lookup
    /// tables needed to turn the planner output back into SpawnableDefinitions.
    /// </summary>
    public sealed class PlacementRuleSet
    {
        public readonly List<PlacementRule> Rules = new List<PlacementRule>();
        /// <summary>Variant id -> spawnable.</summary>
        public readonly List<SpawnableDefinition> Variants = new List<SpawnableDefinition>();
        /// <summary>Rule id -> label for hierarchy groups.</summary>
        public readonly List<string> RuleLabels = new List<string>();

        readonly Dictionary<SpawnableDefinition, int> variantIds = new Dictionary<SpawnableDefinition, int>();

        public static PlacementRuleSet Build(MapGenerationSettings settings)
        {
            var set = new PlacementRuleSet();
            var biomes = settings.biomes;

            for (int b = 0; b < biomes.Count; b++)
            {
                var biome = biomes[b];
                if (biome == null) continue;
                var allowed = new bool[biomes.Count];
                allowed[b] = true;
                foreach (var rule in biome.SpawnRules)
                    set.AddRule(rule, allowed, $"{biome.DisplayName} - {rule.label}");
            }

            foreach (var rule in settings.globalSpawnRules)
            {
                if (rule == null) continue;
                bool[] allowed = null;
                if (rule.allowedBiomes != null && rule.allowedBiomes.Count > 0)
                {
                    allowed = new bool[biomes.Count];
                    foreach (var biome in rule.allowedBiomes)
                    {
                        int index = biomes.IndexOf(biome);
                        if (index >= 0) allowed[index] = true;
                    }
                }
                set.AddRule(rule, allowed, rule.label);
            }
            return set;
        }

        int VariantId(SpawnableDefinition spawnable)
        {
            if (spawnable == null) return -1;
            if (variantIds.TryGetValue(spawnable, out int id)) return id;
            id = Variants.Count;
            Variants.Add(spawnable);
            variantIds[spawnable] = id;
            return id;
        }

        PlacementVariant[] ConvertVariants(List<WeightedSpawnable> source)
        {
            var result = new List<PlacementVariant>();
            if (source == null) return result.ToArray();
            foreach (var v in source)
            {
                if (v == null || v.spawnable == null || v.weight <= 0f) continue;
                var footprint = v.spawnable.Footprint;
                result.Add(new PlacementVariant
                {
                    VariantId = VariantId(v.spawnable),
                    Weight = v.weight,
                    Width = footprint.x,
                    Height = footprint.y,
                    StumpVariantId = VariantId(v.spawnable.Stump),
                });
            }
            return result.ToArray();
        }

        void AddRule(SpawnRule rule, bool[] allowedBiomes, string label)
        {
            if (rule == null || !rule.enabled) return;
            var variants = ConvertVariants(rule.variants);
            if (variants.Length == 0) return;

            var placementRule = new PlacementRule
            {
                RuleId = Rules.Count,
                Label = label,
                Settings = rule.placement ?? new PlacementSettings(),
                Terrain = rule.allowedTerrain,
                AllowedBiomes = allowedBiomes,
                Variants = variants,
                StumpChance = rule.stumpChance,
            };
            if (rule.companions != null && rule.companions.enabled)
            {
                placementRule.Companions = new CompanionRule
                {
                    Count = rule.companions.count,
                    RingRadius = rule.companions.ringRadius,
                    Variants = ConvertVariants(rule.companions.variants),
                };
            }
            Rules.Add(placementRule);
            RuleLabels.Add(label);
        }
    }
}
