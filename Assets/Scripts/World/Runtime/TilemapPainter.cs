using System.Collections.Generic;
using KingOfNothing.World.Generation;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingOfNothing.World
{
    /// <summary>
    /// Paints a MapLayout onto tilemaps: coast foam, the base ground under all land, then one layer per biome.
    /// Every layer only uses flat ground tiles (no cliffs or stairs). Each biome layer has rounded borders,
    /// so where it sits on the base layer the biome change looks like a natural transition.
    /// Water is not tiled: the camera background colour shows through (millions of water tiles were too heavy).
    /// </summary>
    public sealed class TilemapPainter
    {
        public Grid Paint(MapLayout layout, MapGenerationSettings settings, Transform parent)
        {
            var gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(parent, false);
            var grid = gridObject.AddComponent<Grid>();

            int order = settings.groundSortingOrder;
            if (settings.waterFoamTile != null)
            {
                var foam = CreateTilemap(grid, "Water Foam", order - 1);
                PaintFoam(layout, foam, settings.waterFoamTile);
            }

            var biomes = settings.biomes;
            if (biomes.Count == 0 || biomes[0] == null) return grid;

            // Base layer: every land cell with the first biome's tiles
            var baseLayer = CreateTilemap(grid, "Ground - " + biomes[0].DisplayName, order);
            PaintLayer(layout, baseLayer, biomes[0].GroundTiles, i => layout.Terrain[i] == CellTerrain.Land);

            for (int b = 1; b < biomes.Count; b++)
            {
                var biome = biomes[b];
                if (biome == null || biome.GroundTiles == null) continue;
                short id = (short)b;
                var layer = CreateTilemap(grid, "Ground - " + biome.DisplayName, order + b);
                PaintLayer(layout, layer, biome.GroundTiles, i => layout.Terrain[i] == CellTerrain.Land && layout.Biome[i] == id);
            }
            return grid;
        }

        static Tilemap CreateTilemap(Grid grid, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(grid.transform, false);
            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        static void PaintFoam(MapLayout layout, Tilemap tilemap, TileBase tile)
        {
            var distance = layout.ComputeDistanceToWater();
            var cells = new List<Vector3Int>();
            for (int i = 0; i < layout.CellCount; i++)
                if (layout.Terrain[i] == CellTerrain.Land && distance[i] == 1)
                    cells.Add(new Vector3Int(layout.X(i), layout.Y(i), 0));

            var tiles = new TileBase[cells.Count];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = tile;
            tilemap.SetTiles(cells.ToArray(), tiles);
        }

        static void PaintLayer(MapLayout layout, Tilemap tilemap, GroundTileSet tileSet, System.Func<int, bool> has)
        {
            if (tileSet == null) return;
            int w = layout.Width, h = layout.Height;
            var cells = new List<Vector3Int>();
            var tiles = new List<TileBase>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (!has(i)) continue;
                bool north = y + 1 < h && has(i + w);
                bool east = x + 1 < w && has(i + 1);
                bool south = y > 0 && has(i - w);
                bool west = x > 0 && has(i - 1);
                cells.Add(new Vector3Int(x, y, 0));
                tiles.Add(tileSet.GetTile(north, east, south, west));
            }
            tilemap.SetTiles(cells.ToArray(), tiles.ToArray());
        }
    }
}
