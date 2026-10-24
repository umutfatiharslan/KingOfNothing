using System.Collections.Generic;
using KingOfNothing.World.Generation;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>Renders a MapLayout into a small texture (Inspector preview, later the minimap).</summary>
    public static class MapPreviewTexture
    {
        public static readonly Color SeaColor = new Color32(60, 120, 170, 255);
        public static readonly Color LakeColor = new Color32(40, 100, 200, 255);
        public static readonly Color RiverColor = new Color32(30, 80, 210, 255);
        public static readonly Color PondColor = new Color32(50, 140, 220, 255);
        public static readonly Color MarshColor = new Color32(60, 110, 140, 255);

        /// <param name="downscale">1 = one pixel per cell; 2 = half size...</param>
        public static Texture2D Render(MapLayout layout, IReadOnlyList<BiomeDefinition> biomes, int downscale = 1)
        {
            downscale = Mathf.Max(1, downscale);
            int tw = Mathf.Max(1, layout.Width / downscale), th = Mathf.Max(1, layout.Height / downscale);
            var texture = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Map Preview",
            };

            var biomeColors = new Color32[biomes.Count];
            for (int b = 0; b < biomes.Count; b++)
                biomeColors[b] = biomes[b] != null ? (Color32)biomes[b].PreviewColor : new Color32(255, 0, 255, 255);

            var pixels = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                int i = layout.Index(x * downscale, y * downscale);
                pixels[x + y * tw] = ColorOf(layout, i, biomeColors);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static Color32 ColorOf(MapLayout layout, int i, Color32[] biomeColors)
        {
            switch (layout.Terrain[i])
            {
                case CellTerrain.Sea: return SeaColor;
                case CellTerrain.Lake: return LakeColor;
                case CellTerrain.River: return RiverColor;
                case CellTerrain.Pond: return PondColor;
                case CellTerrain.Marsh: return MarshColor;
            }
            int b = layout.Biome[i];
            return b >= 0 && b < biomeColors.Length ? biomeColors[b] : new Color32(255, 0, 255, 255);
        }
    }
}
