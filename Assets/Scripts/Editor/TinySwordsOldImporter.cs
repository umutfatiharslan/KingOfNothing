using UnityEditor;

/// <summary>
/// "Assets/Tiny Swords Old" klasörüne giren görsellere yeni Tiny Swords paketiyle aynı içe aktarma
/// ayarlarını verir: 64 piksel = 1 birim, keskin piksel (Point), sıkıştırma yok.
/// Sprite sheet'ler Single modda gelir; kullanılacakları zaman Sprite Editor'de dilimlenmeleri gerekir.
/// </summary>
public class TinySwordsOldImporter : AssetPostprocessor
{
    const string Folder = "Assets/Tiny Swords Old/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;

        var importer = (TextureImporter)assetImporter;
        // Sadece ilk içe aktarmada ayarla, sonradan elle yapılan dilimleme/ayar değişikliklerini ezme
        if (!importer.importSettingsMissing) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 64;
        importer.filterMode = UnityEngine.FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
    }
}
