using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Hazır dilimlenmemiş bir tileset görselini 64x64 karelere böler ve her kare için bir Tile asset'i üretir.
/// Eski Tiny Swords sürümündeki Tilemap_Flat / Tilemap_Elevation gibi dosyalar için kullanılır.
/// </summary>
public static class TilesetSlicer
{
    /// <returns>Anahtar: (sütun, üstten satır).</returns>
    public static Dictionary<Vector2Int, TileBase> EnsureTiles(string texturePath, string tileFolder, int cell = 64)
    {
        var result = new Dictionary<Vector2Int, TileBase>();
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (importer == null || tex == null)
        {
            Debug.LogError("Tileset bulunamadı: " + texturePath);
            return result;
        }

        int cols = tex.width / cell, rows = tex.height / cell;
        string baseName = Path.GetFileNameWithoutExtension(texturePath);

        int spriteCount = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().Count();
        if (importer.spriteImportMode != SpriteImportMode.Multiple || spriteCount != cols * rows)
            Slice(importer, texturePath, baseName, tex.height, cols, rows, cell);

        EnsureFolder(tileFolder);
        foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>())
        {
            int c = Mathf.RoundToInt(sprite.rect.x) / cell;
            int r = (tex.height - Mathf.RoundToInt(sprite.rect.y)) / cell - 1;

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

    static void Slice(TextureImporter importer, string path, string baseName, int texHeight, int cols, int rows, int cell)
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
                rect = new Rect(c * cell, texHeight - (r + 1) * cell, cell, cell),
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

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
