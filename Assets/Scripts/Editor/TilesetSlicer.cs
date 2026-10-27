using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Slices a tileset texture into 64x64 sprites (if not sliced yet) and creates one Tile asset per sprite.
    /// Used for the old Tiny Swords version files such as Tilemap_Flat (desert sand) and Tilemap_Elevation (rock).
    /// </summary>
    public static class TilesetSlicer
    {
        /// <returns>Tiles keyed by (column, row counted from the top).</returns>
        public static Dictionary<Vector2Int, TileBase> EnsureTiles(string texturePath, string tileFolder, int cell = 64)
        {
            var result = new Dictionary<Vector2Int, TileBase>();
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (importer == null || texture == null)
            {
                Debug.LogError("Tileset not found: " + texturePath);
                return result;
            }

            int cols = texture.width / cell, rows = texture.height / cell;
            string baseName = Path.GetFileNameWithoutExtension(texturePath);

            int spriteCount = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().Count();
            if (importer.spriteImportMode != SpriteImportMode.Multiple || spriteCount != cols * rows)
                Slice(importer, baseName, texture.height, cols, rows, cell);

            EnsureFolder(tileFolder);
            foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>())
            {
                int c = Mathf.RoundToInt(sprite.rect.x) / cell;
                int r = (texture.height - Mathf.RoundToInt(sprite.rect.y)) / cell - 1;

                string tilePath = $"{tileFolder}/{sprite.name}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = sprite;
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                else if (tile.sprite != sprite)
                {
                    tile.sprite = sprite;
                    EditorUtility.SetDirty(tile);
                }
                result[new Vector2Int(c, r)] = tile;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static void Slice(TextureImporter importer, string baseName, int textureHeight, int cols, int rows, int cell)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = cell;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            var rects = new List<SpriteRect>();
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                rects.Add(new SpriteRect
                {
                    name = $"{baseName}_{c}_{r}",
                    rect = new Rect(c * cell, textureHeight - (r + 1) * cell, cell, cell),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = GUID.Generate()
                });
            }
            provider.SetSpriteRects(rects.ToArray());

            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));

            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Slices a texture into named sprites with the given pixel rects (Unity coordinates: y from the bottom),
        /// unless it already has exactly these sprites. Returns the sprites by name.
        /// </summary>
        public static Dictionary<string, Sprite> EnsureNamedSlices(string texturePath, IList<(string name, RectInt rect)> slices)
        {
            var result = new Dictionary<string, Sprite>();
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("Texture not found: " + texturePath);
                return result;
            }
            var existing = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().ToList();
            bool sliced = importer.spriteImportMode == SpriteImportMode.Multiple &&
                          slices.All(sl => existing.Any(sp => sp.name == sl.name && Mathf.RoundToInt(sp.rect.x) == sl.rect.x &&
                                                                 Mathf.RoundToInt(sp.rect.y) == sl.rect.y &&
                                                                 Mathf.RoundToInt(sp.rect.width) == sl.rect.width &&
                                                                 Mathf.RoundToInt(sp.rect.height) == sl.rect.height));
            if (!sliced)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 64;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;

                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                provider.InitSpriteEditorDataProvider();
                // Sprites that already exist keep their id, so references to them (building assets) stay valid
                var old = provider.GetSpriteRects() ?? new SpriteRect[0];
                var rects = slices.Select(sl =>
                {
                    var previous = old.FirstOrDefault(o => o.name == sl.name);
                    return new SpriteRect
                    {
                        name = sl.name,
                        rect = new Rect(sl.rect.x, sl.rect.y, sl.rect.width, sl.rect.height),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        spriteID = previous != null ? previous.spriteID : GUID.Generate(),
                    };
                }).ToArray();
                provider.SetSpriteRects(rects);
                var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
            }
            foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>())
                result[sprite.name] = sprite;
            return result;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
