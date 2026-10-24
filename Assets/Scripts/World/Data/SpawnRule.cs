using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;
using KingOfNothing.World.Generation;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>A spawnable with a relative chance among the other variants of a rule.</summary>
    [Serializable]
    public class WeightedSpawnable
    {
        public SpawnableDefinition spawnable;
        [Min(0f)] public float weight = 1f;

        public WeightedSpawnable() { }

        public WeightedSpawnable(SpawnableDefinition spawnable, float weight = 1f)
        {
            this.spawnable = spawnable;
            this.weight = weight;
        }
    }

    /// <summary>Objects spawned around the main object of a rule, e.g. up to 3 bones one cell around a skull.</summary>
    [Serializable]
    public class CompanionSpawn
    {
        public bool enabled;
        [Tooltip("How many companions per main object.")]
        public IntRange count = new IntRange(0, 3);
        [Tooltip("Companions are placed on the ring of cells this far from the main object (1 = touching it).")]
        [Min(1)] public int ringRadius = 1;
        public List<WeightedSpawnable> variants = new List<WeightedSpawnable>();
    }

    /// <summary>
    /// "Spawn these objects here, this often." A biome owns a list of these; the map settings own global ones.
    /// </summary>
    [Serializable]
    public class SpawnRule
    {
        [Tooltip("Name used for the hierarchy group and logs.")]
        public string label = "Objects";
        public bool enabled = true;
        [Tooltip("Objects picked at random (by weight) for every placement.")]
        public List<WeightedSpawnable> variants = new List<WeightedSpawnable>();
        [Tooltip("Ground the object may stand on.")]
        public TerrainMask allowedTerrain = TerrainMask.Land;
        public PlacementSettings placement = new PlacementSettings();
        [Tooltip("Trees: chance that a stump (the variant's Stump) spawns instead of the tree.")]
        [Range(0f, 1f)] public float stumpChance;
        public CompanionSpawn companions = new CompanionSpawn();

        public SpawnRule() { }

        public SpawnRule(string label) => this.label = label;
    }

    /// <summary>A rule that is not tied to one biome. Empty Allowed Biomes = every biome (and water).</summary>
    [Serializable]
    public class GlobalSpawnRule : SpawnRule
    {
        [Tooltip("Biomes the rule may use. Leave empty for all biomes.")]
        public List<BiomeDefinition> allowedBiomes = new List<BiomeDefinition>();

        public GlobalSpawnRule() { }

        public GlobalSpawnRule(string label) : base(label) { }
    }
}
