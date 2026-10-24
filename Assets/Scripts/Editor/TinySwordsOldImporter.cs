using UnityEditor;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Gives textures imported into "Assets/Tiny Swords Old" the same import settings as the new Tiny Swords
    /// pack: 64 pixels = 1 unit, crisp pixels (Point), no compression. Sprite sheets come in as Single and
    /// have to be sliced in the Sprite Editor when they are used.
    /// </summary>
    public class TinySwordsOldImporter : AssetPostprocessor
    {
        const string Folder = "Assets/Tiny Swords Old/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;

            var importer = (TextureImporter)assetImporter;
            // Only on the first import, so later manual slicing / settings are not overwritten
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
}
