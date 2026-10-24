using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.World.Generation
{
    /// <summary>Where the exiled party starts.</summary>
    [Serializable]
    public class StartAreaSettings
    {
        [Tooltip("Radius (cells) kept free of trees and other objects around the start.")]
        [Min(0)] public int clearRadius = 5;
        [Tooltip("Minimum distance (cells) from the start to any water.")]
        [Min(0)] public int minDistanceToWater = 8;
        [Tooltip("The start is never placed in biomes with these tags.")]
        public BiomeTags avoidedTags = BiomeTags.Wet | BiomeTags.Arid | BiomeTags.Rocky;
        [Tooltip("0 = as close to the map centre as possible, 1 = anywhere on the map (random).")]
        [Range(0f, 1f)] public float randomness = 0.35f;
    }

    /// <summary>Finds a good start cell: dry, open land in a friendly biome, near the centre.</summary>
    public static class StartLocationFinder
    {
        public static int Find(MapLayout map, MapGenerationConfig config, StartAreaSettings settings, int seed)
        {
            var random = new Utilities.DeterministicRandom(seed);
            var waterDistance = map.ComputeDistanceToWater();
            int w = map.Width, h = map.Height;

            // Search origin: the centre shifted randomly
            int ox = (int)(w * 0.5f + random.Range(-0.5f, 0.5f) * w * settings.randomness);
            int oy = (int)(h * 0.5f + random.Range(-0.5f, 0.5f) * h * settings.randomness);

            int fallback = -1;
            int maxRadius = Math.Max(w, h);
            for (int r = 0; r < maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // ring only
                    int x = ox + dx, y = oy + dy;
                    if (!map.InBounds(x, y)) continue;
                    int i = x + y * w;
                    if (!map.IsLand(i)) continue;
                    if (fallback < 0) fallback = i;
                    if (waterDistance[i] < settings.minDistanceToWater) continue;
                    if (!BiomeAllowed(map, config, settings, i)) continue;
                    if (!AreaIsOpenLand(map, config, settings, x, y)) continue;
                    return i;
                }
            }
            return fallback;
        }

        static bool BiomeAllowed(MapLayout map, MapGenerationConfig config, StartAreaSettings settings, int i)
        {
            int b = map.Biome[i];
            if (b < 0 || b >= config.Biomes.Length || config.Biomes[b] == null) return false;
            return (config.Biomes[b].tags & settings.avoidedTags) == 0;
        }

        static bool AreaIsOpenLand(MapLayout map, MapGenerationConfig config, StartAreaSettings settings, int cx, int cy)
        {
            int r = settings.clearRadius;
            for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!map.InBounds(x, y)) return false;
                int i = x + y * map.Width;
                if (!map.IsLand(i) || !BiomeAllowed(map, config, settings, i)) return false;
            }
            return true;
        }

        /// <summary>Cells inside the clear radius (reserved so no object spawns there).</summary>
        public static List<int> ClearArea(MapLayout map, int startCell, int radius)
        {
            var cells = new List<int>();
            if (startCell < 0) return cells;
            int cx = map.X(startCell), cy = map.Y(startCell);
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
                if (map.InBounds(x, y)) cells.Add(x + y * map.Width);
            return cells;
        }
    }
}
