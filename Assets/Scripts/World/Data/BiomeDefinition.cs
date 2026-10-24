using System.Collections.Generic;
using KingOfNothing.World.Generation;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// A biome: its ground tiles, how it is laid out on the map and what grows on it.
    /// Add, remove or reorder biomes in MapGenerationSettings; the first one is the base ground layer.
    /// </summary>
    [CreateAssetMenu(fileName = "Biome", menuName = "King of Nothing/World/Biome")]
    public class BiomeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] string displayName = "Biome";
        [Tooltip("Colour on the map preview / minimap.")]
        [SerializeField] Color previewColor = new Color(0.45f, 0.7f, 0.3f);

        [Header("Ground")]
        [SerializeField] GroundTileSet groundTiles;

        [Header("Gameplay")]
        [Tooltip("Movement speed multiplier for units walking on this biome.")]
        [Range(0.1f, 2f)] [SerializeField] float movementSpeedMultiplier = 1f;

        [Header("Layout")]
        [SerializeField] BiomeGenerationProfile generation = new BiomeGenerationProfile();

        [Header("Objects")]
        [SerializeField] List<SpawnRule> spawnRules = new List<SpawnRule>();

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Color PreviewColor => previewColor;
        public GroundTileSet GroundTiles => groundTiles;
        public float MovementSpeedMultiplier => movementSpeedMultiplier;
        public BiomeGenerationProfile Generation => generation;
        public BiomeTags Tags => generation != null ? generation.tags : BiomeTags.None;
        public IReadOnlyList<SpawnRule> SpawnRules => spawnRules;

        /// <summary>True if snow and snowy assets are never allowed on this biome.</summary>
        public bool ForbidsSnow => (Tags & BiomeTags.Arid) != 0 || (generation != null && (generation.repelledTags & BiomeTags.Snowy) != 0);

#if UNITY_EDITOR
        public void EditorSetup(string newName, Color color, GroundTileSet tiles, float speed, BiomeGenerationProfile profile, List<SpawnRule> rules)
        {
            displayName = newName;
            previewColor = color;
            groundTiles = tiles;
            movementSpeedMultiplier = speed;
            generation = profile ?? new BiomeGenerationProfile();
            spawnRules = rules ?? new List<SpawnRule>();
        }
#endif
    }
}
