using System.Collections.Generic;
using System.Linq;
using KingOfNothing.Characters;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Generation;
using KingOfNothing.World.Objects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingOfNothing.EditorTools
{
    /// <summary>
    /// Creates the default data assets of the map from the Tiny Swords packs, following the map generation
    /// rules document: ground tile sets, spawnable objects, the 7 biomes with their spawn rules, the global
    /// rules (gold mines, pumpkins, skulls and bones, water rocks), the reference map, the map settings, the map
    /// edit log, a name pool, trait templates and the villager prefab.
    /// Existing assets are kept unless "overwrite" is true, so values tuned in the Inspector are not lost.
    /// </summary>
    public static class DefaultMapAssetsBuilder
    {
        const string TS = "Assets/Tiny Swords/";
        const string TSOld = "Assets/Tiny Swords Old/";
        const string SlicedTilesFolder = TS + "Terrain/Tileset/Tilemap Settings/Sliced Tiles";
        const string OldTilesFolder = TSOld + "Generated Tiles";

        public const string DataRoot = "Assets/Data/World";
        const string TileSetFolder = DataRoot + "/Tile Sets";
        const string SpawnableFolder = DataRoot + "/Spawnables";
        const string BiomeFolder = DataRoot + "/Biomes";
        public const string SettingsPath = DataRoot + "/MapGenerationSettings.asset";
        public const string ReferenceMapPath = DataRoot + "/ReferenceMap.asset";
        public const string ReferenceSourcePath = "Assets/Map Source/World Map.map";
        /// <summary>Developer edit log of an older version (replaced by OriginalMap + the edited map file).</summary>
        const string ObsoleteEditLogPath = DataRoot + "/MapEdits.asset";
        public const string NamePoolPath = "Assets/Data/Characters/NamePool.asset";
        public const string TraitFolder = "Assets/Data/Characters/Traits";
        public const string VillagerPrefabPath = "Assets/Prefabs/Characters/Villager.prefab";
        public const string PawnAnimationsPath = "Assets/Data/Characters/PawnAnimations_Blue.asset";
        const string PawnFolder = TS + "Pawn and Resources/Pawn/Blue Pawn/";

        static bool overwrite;
        static readonly Dictionary<string, Sprite[]> frameCache = new Dictionary<string, Sprite[]>();

        public static MapGenerationSettings Build(bool overwriteExisting)
        {
            overwrite = overwriteExisting;
            frameCache.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("King of Nothing", "Ground tile sets...", 0.1f);
                var tiles = BuildTileSets();
                if (tiles == null) return null;

                EditorUtility.DisplayProgressBar("King of Nothing", "Spawnable objects...", 0.4f);
                var s = BuildSpawnables();

                EditorUtility.DisplayProgressBar("King of Nothing", "Biomes...", 0.6f);
                var biomes = BuildBiomes(tiles, s);

                EditorUtility.DisplayProgressBar("King of Nothing", "Map settings and reference map...", 0.8f);
                var settings = BuildSettings(biomes, s);
                BuildMapFiles();

                EditorUtility.DisplayProgressBar("King of Nothing", "Characters...", 0.9f);
                BuildNamePool();
                BuildTraits();
                BuildVillagerPrefab();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[King of Nothing] Default map assets ready in {DataRoot} (overwrite: {overwriteExisting}).", settings);
                return settings;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ================================================================== helpers

        static T LoadOrCreate<T>(string path, out bool fresh) where T : ScriptableObject
        {
            TilesetSlicer.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                fresh = overwrite;
                return asset;
            }
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            fresh = true;
            return asset;
        }

        /// <summary>All sprites of a texture, left to right and top to bottom.</summary>
        static Sprite[] Frames(string path)
        {
            if (frameCache.TryGetValue(path, out var cached) && cached.All(f => f != null)) return cached;
            if (path.StartsWith(TSOld)) EnsureOldSpriteImport(path);
            var frames = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderByDescending(sp => sp.rect.y).ThenBy(sp => sp.rect.x).ToArray();
            if (frames.Length == 0) Debug.LogWarning("[King of Nothing] No sprite found at " + path);
            frameCache[path] = frames;
            return frames;
        }

        /// <summary>Fixes old pack images imported with Unity's default settings (64 PPU, crisp pixels).</summary>
        static void EnsureOldSpriteImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == TextureImporterType.Sprite && Mathf.Approximately(importer.spritePixelsPerUnit, 64f)
                && importer.filterMode == FilterMode.Point)
                return;

            importer.textureType = TextureImporterType.Sprite;
            if (importer.spriteImportMode == SpriteImportMode.None) importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        static List<WeightedSpawnable> Variants(params SpawnableDefinition[] spawnables) =>
            spawnables.Where(x => x != null).Select(x => new WeightedSpawnable(x)).ToList();

        // ================================================================== tile sets

        sealed class TileSets
        {
            public readonly GroundTileSet[] Color = new GroundTileSet[6]; // 1..5
            public GroundTileSet Desert, Rock;
        }

        // Tiny Swords flat ground 4x4 block: 3x3 corners / edges + vertical strip + horizontal strip + single tile
        static int ColumnFor(bool west, bool east) => west && east ? 1 : !west && east ? 0 : west ? 2 : 3;
        static int RowFor(bool north, bool south) => north && south ? 1 : !north && south ? 0 : north ? 2 : 3;

        delegate Vector2Int TileKey(int column, int row);

        static GroundTileSet BuildTileSet(string name, Dictionary<Vector2Int, TileBase> source, TileKey key)
        {
            var set = LoadOrCreate<GroundTileSet>($"{TileSetFolder}/{name}.asset", out bool fresh);
            if (!fresh && set.IsComplete) return set;

            for (int mask = 0; mask < GroundTileSet.TileCount; mask++)
            {
                bool north = (mask & GroundTileSet.North) != 0, east = (mask & GroundTileSet.East) != 0;
                bool south = (mask & GroundTileSet.South) != 0, west = (mask & GroundTileSet.West) != 0;
                var k = key(ColumnFor(west, east), RowFor(north, south));
                source.TryGetValue(k, out var tile);
                set.SetTile(mask, tile);
            }
            source.TryGetValue(key(1, 1), out var centre);
            set.SetFallback(centre);
            EditorUtility.SetDirty(set);
            return set;
        }

        static TileSets BuildTileSets()
        {
            var result = new TileSets();

            // New pack: Tilemap_color1..5 already sliced, Tile assets in "Sliced Tiles"
            var colorTiles = new Dictionary<Vector2Int, TileBase>[6];
            for (int i = 1; i <= 5; i++) colorTiles[i] = new Dictionary<Vector2Int, TileBase>();
            foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { SlicedTilesFolder }))
            {
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
                if (tile == null || tile.sprite == null) continue;
                string texture = tile.sprite.texture.name;
                if (!texture.StartsWith("Tilemap_color") || !int.TryParse(texture.Substring(13), out int set) || set < 1 || set > 5)
                    continue;
                var r = tile.sprite.rect;
                int rows = tile.sprite.texture.height / 64;
                colorTiles[set][new Vector2Int((int)r.x / 64, rows - 1 - (int)r.y / 64)] = tile;
            }
            for (int i = 1; i <= 5; i++)
            {
                if (colorTiles[i].Count < 16)
                {
                    Debug.LogError($"[King of Nothing] Tiles of Tilemap_color{i} are missing in {SlicedTilesFolder}");
                    return null;
                }
                result.Color[i] = BuildTileSet($"Ground Color{i}", colorTiles[i], (c, r) => new Vector2Int(c, r));
            }

            // Old pack: sand (right half of Tilemap_Flat) and rock (Tilemap_Elevation, only its flat top)
            var flat = TilesetSlicer.EnsureTiles(TSOld + "Terrain/Ground/Tilemap_Flat.png", OldTilesFolder + "/Tilemap_Flat");
            var elevation = TilesetSlicer.EnsureTiles(TSOld + "Terrain/Ground/Tilemap_Elevation.png", OldTilesFolder + "/Tilemap_Elevation");
            if (flat.Count < 40 || elevation.Count < 32)
            {
                Debug.LogError("[King of Nothing] Old Tiny Swords ground tiles could not be created: " + TSOld + "Terrain/Ground");
                return null;
            }
            // Sand block starts at column 5
            result.Desert = BuildTileSet("Ground Desert", flat, (c, r) => new Vector2Int(c + 5, r));
            // Rows 3 and 5 of the elevation sheet are cliff walls; its horizontal strip sits in row 4
            result.Rock = BuildTileSet("Ground Rock", elevation, (c, r) => new Vector2Int(c, r == 3 ? 4 : r));
            return result;
        }

        // ================================================================== spawnables

        sealed class Spawnables
        {
            public SpawnableDefinition[] Trees = new SpawnableDefinition[5];      // 1..4
            public SpawnableDefinition[] Stumps = new SpawnableDefinition[5];     // 1..4
            public SpawnableDefinition[] Bushes = new SpawnableDefinition[5];     // 1..4
            public SpawnableDefinition[] Stones = new SpawnableDefinition[5];     // 1..4 (Rock1..Rock4)
            public SpawnableDefinition[] GoldStones = new SpawnableDefinition[7]; // 1..6
            public SpawnableDefinition[] WaterRocks = new SpawnableDefinition[5]; // 1..4
            public SpawnableDefinition[] Mushrooms = new SpawnableDefinition[4];  // 1..3
            public SpawnableDefinition[] Pumpkins = new SpawnableDefinition[3];   // 1..2
            public SpawnableDefinition Bone1, Bone2, GoldMine;
            public SpawnableDefinition Signpost1, Signpost2, Scarecrow;           // Deco 16, 17, 18
            // Old-pack decorations (not spawned by the map yet; placeable with the developer tools)
            public SpawnableDefinition Rock, RockOld1, RockOld2;                  // Deco 04, 05, 06
            public SpawnableDefinition[] SmallBushes = new SpawnableDefinition[4]; // 1..3 = Deco 07..09
            public SpawnableDefinition Grass, GrassBig;                           // Deco 10, 11
            public SpawnableDefinition GoldMineActive, GoldMineInactive;
            // Items dropped by broken objects
            public SpawnableDefinition WoodResource, GoldResource, GoldResourceHighlight, PumpkinItem;

            public IEnumerable<SpawnableDefinition> DeveloperExtras => new[]
            {
                Signpost1, Signpost2, Scarecrow, Rock, RockOld1, RockOld2, SmallBushes[1], SmallBushes[2], SmallBushes[3],
                Grass, GrassBig, GoldMineActive, GoldMineInactive, WoodResource, GoldResource, GoldResourceHighlight, PumpkinItem,
            }.Where(x => x != null);
        }

        /// <summary>
        /// Version of the default gameplay values (health / drops / movement tables). Assets with an older version
        /// get the new values once; afterwards values tuned in the Inspector are kept. Raise it when the tables change.
        /// </summary>
        const int GameplayDataVersion = 2;

        static bool NeedsGameplay(SpawnableDefinition asset) =>
            asset != null && (overwrite || asset.EditorDataVersion < GameplayDataVersion);

        /// <summary>
        /// Movement rules of an object: solid (blocks characters, hitbox) or walk-through (with a speed multiplier).
        /// Filled once for existing assets (values tuned in the Inspector are kept), always on overwrite or when the
        /// gameplay tables are newer than the asset.
        /// </summary>
        static void Movement(SpawnableDefinition asset, bool walkThrough, float speed, Vector2 hitbox, float hitboxY = -0.1f, bool fade = true)
        {
            if (asset == null || (asset.EditorMovementConfigured && !NeedsGameplay(asset))) return;
            asset.EditorSetMovement(walkThrough, speed, hitbox, new Vector2(0f, hitboxY), fade);
            EditorUtility.SetDirty(asset);
        }

        static void Solid(SpawnableDefinition asset, float width, float height, bool fade = false) =>
            Movement(asset, false, 1f, new Vector2(width, height), -0.1f, fade);

        static void Passable(SpawnableDefinition asset, float speed = 1f, bool fade = false) =>
            Movement(asset, true, speed, new Vector2(0.6f, 0.4f), -0.1f, fade);

        /// <summary>Health (-1 = collected directly), drops, pile size, carrying and the look after the first hit.</summary>
        static void Gameplay(SpawnableDefinition asset, int health, params DropEntry[] drops) =>
            Gameplay(asset, health, false, 10, null, drops);

        static void Gameplay(SpawnableDefinition asset, int health, bool movable, int stack, Sprite[] damagedFrames, params DropEntry[] drops)
        {
            if (!NeedsGameplay(asset)) return;
            asset.EditorSetGameplay(GameplayDataVersion, health, drops.Where(d => d != null && d.item != null).ToList(), stack, movable, damagedFrames);
            EditorUtility.SetDirty(asset);
        }

        static DropEntry Drop(SpawnableDefinition item, int amount, float chance = 1f) => new DropEntry(item, amount, chance);

        static SpawnableDefinition Spawnable(string file, string displayName, WorldObjectKind kind, string spritePath,
            Vector2Int footprint, float inset, bool animate, bool blocksBuilding, ResourceType resource, int health, int yield)
        {
            var asset = LoadOrCreate<SpawnableDefinition>($"{SpawnableFolder}/{file}.asset", out bool fresh);
            if (!fresh) return asset;
            asset.EditorSetup(displayName, kind, Frames(spritePath), footprint, inset, animate, blocksBuilding, resource, health, yield);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Renames an object asset (keeps its GUID, so every reference stays valid).</summary>
        static void RenameAsset(string oldFile, string newFile)
        {
            string from = $"{SpawnableFolder}/{oldFile}.asset", to = $"{SpawnableFolder}/{newFile}.asset";
            if (AssetDatabase.LoadAssetAtPath<SpawnableDefinition>(from) == null ||
                AssetDatabase.LoadAssetAtPath<SpawnableDefinition>(to) != null) return;
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[King of Nothing] Could not rename {from}: {error}");
        }

        static void SetIdentity(SpawnableDefinition asset, string displayName, WorldObjectKind kind)
        {
            if (asset == null || (!NeedsGameplay(asset) && asset.DisplayName == displayName && asset.Kind == kind)) return;
            asset.EditorSetIdentity(displayName, kind);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>New names of the old-pack decoration images (agreed with the design; 01.png -> Mushroom_Red_Small.png ...).</summary>
        static readonly string[] DecoNames =
        {
            null, "Mushroom_Red_Small", "Mushroom_Red_Medium", "Mushroom_Red_Big", "Rock", "Rock_old1", "Rock_old2",
            "Bush_Green_Small", "Bush_Green_Medium", "Bush_Green_Big", "Grass_Green_Small", "Grass_Green_Big",
            "Pumpkin_Small", "Pumpkin_Big", "Bone1", "Bone2", "Signpost1", "Signpost2", "Scarecrow",
        };

        const string DecoFolder = TSOld + "Deco";

        /// <summary>Path of old-pack decoration n (01..18) under its new name.</summary>
        static string Deco(int n) => n > 0 && n < DecoNames.Length ? $"{DecoFolder}/{DecoNames[n]}.png" : $"{DecoFolder}/{n:D2}.png";

        /// <summary>Renames 01.png ... 18.png to their real names (keeps the GUIDs, so every reference stays valid).</summary>
        static void RenameDecoTextures()
        {
            for (int n = 1; n < DecoNames.Length; n++)
            {
                string oldPath = $"{DecoFolder}/{n:D2}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(oldPath) == null) continue;
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(Deco(n)) != null) continue;
                string error = AssetDatabase.RenameAsset(oldPath, DecoNames[n]);
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[King of Nothing] Could not rename {oldPath}: {error}");
            }
        }

        const string GoldStoneFolder = TS + "Pawn and Resources/Gold/Gold Stones/";

        static Spawnables BuildSpawnables()
        {
            var s = new Spawnables();
            var one = Vector2Int.one;

            RenameDecoTextures();

            // Asset names agreed with the design (keep the GUIDs): Deco 16 was "Skull" and then "Signpost",
            // Deco 04-06 were "SmallStone1-3", Deco 10-11 were "GrassTuft1-2".
            RenameAsset("Skull", "Signpost1");
            RenameAsset("Signpost", "Signpost1");
            RenameAsset("SmallStone1", "Rock");
            RenameAsset("SmallStone2", "Rock_old1");
            RenameAsset("SmallStone3", "Rock_old2");
            RenameAsset("GrassTuft1", "Grass");
            RenameAsset("GrassTuft2", "GrassBig");
            RenameAsset("LeafyBush", "GrassBig");

            for (int n = 1; n <= 4; n++)
            {
                s.Stumps[n] = Spawnable($"Stump{n}", $"Stump {n}", WorldObjectKind.Stump,
                    TS + $"Pawn and Resources/Wood/Trees/Stump {n}.png", one, 0.3f, false, true, ResourceType.Wood, 10, 1);
                s.Trees[n] = Spawnable($"Tree{n}", $"Tree {n}", WorldObjectKind.Tree,
                    TS + $"Pawn and Resources/Wood/Trees/Tree{n}.png", one, 0.55f, true, true, ResourceType.Wood, 20, 3);
                // Rules: tree1 -> stump1, tree2 -> stump2 ...
                if (s.Trees[n].Stump == null)
                {
                    s.Trees[n].EditorSetStump(s.Stumps[n]);
                    EditorUtility.SetDirty(s.Trees[n]);
                }

                s.Bushes[n] = Spawnable($"Bush{n}", $"Bush {n}", WorldObjectKind.Bush,
                    TS + $"Terrain/Decorations/Bushes/Bush {n}.png", one, 0.3f, true, false, ResourceType.None, 6, 0);
                s.Stones[n] = Spawnable($"Stone{n}", $"Stone {n}", WorldObjectKind.Stone,
                    TS + $"Terrain/Decorations/Rocks/Rock{n}.png", one, 0.25f, false, true, ResourceType.SmallStone, 3 + n, 1);
                s.WaterRocks[n] = Spawnable($"WaterRock{n}", $"Water Rock {n}", WorldObjectKind.WaterRock,
                    TS + $"Terrain/Decorations/Rocks in the Water/Water Rocks_0{n}.png", one, 0.3f, true, false, ResourceType.SmallStone, 12, 2);
            }

            // Gold stones spawn with their animated highlight; after the first hit they show the plain version
            for (int n = 1; n <= 6; n++)
                s.GoldStones[n] = Spawnable($"GoldStone{n}", $"Gold Stone {n}", WorldObjectKind.GoldStone,
                    GoldStoneFolder + $"Gold Stone {n}_Highlight.png", one, 0.3f, true, true, ResourceType.Gold, 30 + n * 10, n);

            s.GoldMine = Spawnable("GoldMine", "Gold Mine", WorldObjectKind.GoldMine,
                TSOld + "Resources/Gold Mine/GoldMine_Destroyed.png", new Vector2Int(3, 2), 0.25f, false, true, ResourceType.Gold, 500, 50);

            for (int n = 1; n <= 3; n++)
                s.Mushrooms[n] = Spawnable($"Mushroom{n}", $"Mushroom {n}", WorldObjectKind.Mushroom, Deco(n), one, 0.3f, false, false,
                    ResourceType.Mushroom, 2, 1);
            s.Pumpkins[1] = Spawnable("Pumpkin1", "Pumpkin 1", WorldObjectKind.Pumpkin, Deco(12), one, 0.3f, false, false, ResourceType.Pumpkin, 3, 1);
            s.Pumpkins[2] = Spawnable("Pumpkin2", "Pumpkin 2", WorldObjectKind.Pumpkin, Deco(13), one, 0.3f, false, false, ResourceType.Pumpkin, 5, 2);
            s.Bone1 = Spawnable("Bone1", "Bone 1", WorldObjectKind.Bone, Deco(14), one, 0.3f, false, false, ResourceType.Bone, 2, 0);
            s.Bone2 = Spawnable("Bone2", "Bone 2", WorldObjectKind.Bone, Deco(15), one, 0.3f, false, false, ResourceType.Bone, 2, 0);
            s.Signpost1 = Spawnable("Signpost1", "Skull Signpost", WorldObjectKind.Signpost, Deco(16), one, 0.3f, false, true, ResourceType.Wood, 8, 1);
            s.Signpost2 = Spawnable("Signpost2", "Arrow Signpost", WorldObjectKind.Signpost, Deco(17), one, 0.3f, false, true, ResourceType.Wood, 8, 1);
            s.Scarecrow = Spawnable("Scarecrow", "Scarecrow", WorldObjectKind.Scarecrow, Deco(18), one, 0.3f, false, true, ResourceType.Wood, 8, 1);
            SetIdentity(s.Signpost1, "Skull Signpost", WorldObjectKind.Signpost);

            s.Rock = Spawnable("Rock", "Rock", WorldObjectKind.Stone, Deco(4), one, 0.3f, false, false, ResourceType.SmallStone, 2, 1);
            s.RockOld1 = Spawnable("Rock_old1", "Rock (old) 1", WorldObjectKind.Stone, Deco(5), one, 0.3f, false, true, ResourceType.SmallStone, 3, 1);
            s.RockOld2 = Spawnable("Rock_old2", "Rock (old) 2", WorldObjectKind.Stone, Deco(6), one, 0.3f, false, true, ResourceType.SmallStone, 4, 1);
            SetIdentity(s.Rock, "Rock", WorldObjectKind.Stone);
            SetIdentity(s.RockOld1, "Rock (old) 1", WorldObjectKind.Stone);
            SetIdentity(s.RockOld2, "Rock (old) 2", WorldObjectKind.Stone);
            for (int n = 1; n <= 3; n++)
                s.SmallBushes[n] = Spawnable($"SmallBush{n}", $"Small Bush {n}", WorldObjectKind.Bush, Deco(6 + n), one, 0.3f, false, false,
                    ResourceType.None, 3 + n, 0);
            s.Grass = Spawnable("Grass", "Grass", WorldObjectKind.Grass, Deco(10), one, 0.3f, false, false, ResourceType.None, 1, 0);
            s.GrassBig = Spawnable("GrassBig", "Grass (big)", WorldObjectKind.Grass, Deco(11), one, 0.3f, false, false, ResourceType.None, 1, 0);
            SetIdentity(s.Grass, "Grass", WorldObjectKind.Grass);
            SetIdentity(s.GrassBig, "Grass (big)", WorldObjectKind.Grass);
            s.GoldMineActive = Spawnable("GoldMineActive", "Gold Mine (active)", WorldObjectKind.GoldMine,
                TSOld + "Resources/Gold Mine/GoldMine_Active.png", new Vector2Int(3, 2), 0.25f, false, true, ResourceType.Gold, 500, 50);
            s.GoldMineInactive = Spawnable("GoldMineInactive", "Gold Mine (inactive)", WorldObjectKind.GoldMine,
                TSOld + "Resources/Gold Mine/GoldMine_Inactive.png", new Vector2Int(3, 2), 0.25f, false, true, ResourceType.Gold, 500, 50);

            s.WoodResource = Spawnable("WoodResource", "Wood", WorldObjectKind.Item,
                TS + "Pawn and Resources/Wood/Wood Resource/Wood Resource.png", one, 0.3f, false, false, ResourceType.Wood, 1, 1);
            s.GoldResource = Spawnable("GoldResource", "Gold", WorldObjectKind.Item,
                TS + "Pawn and Resources/Gold/Gold Resource/Gold_Resource.png", one, 0.3f, false, false, ResourceType.Gold, 1, 1);
            s.GoldResourceHighlight = Spawnable("GoldResourceHighlight", "Gold (shining)", WorldObjectKind.Item,
                TS + "Pawn and Resources/Gold/Gold Resource/Gold_Resource_Highlight.png", one, 0.3f, true, false, ResourceType.Gold, 1, 1);
            s.PumpkinItem = Spawnable("PumpkinItem", "Small Pumpkin", WorldObjectKind.Item, Deco(12), one, 0.3f, false, false, ResourceType.Pumpkin, 1, 1);

            ApplyMovementRules(s);
            ApplyGameplayTables(s);
            return s;
        }

        /// <summary>
        /// Who can walk where: bushes (x0.7), grass and bones can be walked through, as well as the smallest
        /// mushroom (Deco 01), the smallest stone (Rock = Deco 04) and dropped items. Every other object is solid.
        /// Hitbox = the solid part characters collide with (cells), e.g. only a tree's trunk.
        /// </summary>
        static void ApplyMovementRules(Spawnables s)
        {
            // fade = 50% transparent while a character is inside / right behind it. Only: trees, bushes, cacti,
            // gold mines, Gold Stone 5 and 6, signposts.
            const bool fade = true;
            for (int n = 1; n <= 4; n++)
            {
                Solid(s.Trees[n], 0.45f, 0.4f, fade);
                Solid(s.Stumps[n], 0.55f, 0.35f);
                Passable(s.Bushes[n], 0.7f, fade);
                Solid(s.Stones[n], n == 4 ? 0.85f : n == 2 ? 0.75f : 0.6f, n == 4 ? 0.55f : 0.45f);
                Solid(s.WaterRocks[n], 0.8f, 0.6f);
            }
            for (int n = 1; n <= 6; n++) Solid(s.GoldStones[n], 0.7f, 0.5f, n >= 5);
            Solid(s.GoldMine, 2.8f, 1.7f, fade);
            Solid(s.GoldMineActive, 2.8f, 1.7f, fade);
            Solid(s.GoldMineInactive, 2.8f, 1.7f, fade);

            Passable(s.Mushrooms[1]);
            Solid(s.Mushrooms[2], 0.4f, 0.3f);
            Solid(s.Mushrooms[3], 0.55f, 0.4f);
            Solid(s.Pumpkins[1], 0.5f, 0.4f);
            Solid(s.Pumpkins[2], 0.6f, 0.45f);
            Passable(s.Bone1);
            Passable(s.Bone2);
            Solid(s.Signpost1, 0.3f, 0.3f, fade);
            Solid(s.Signpost2, 0.35f, 0.3f, fade);
            Solid(s.Scarecrow, 0.4f, 0.3f);

            Passable(s.Rock);
            Solid(s.RockOld1, 0.45f, 0.35f);
            Solid(s.RockOld2, 0.65f, 0.45f);
            for (int n = 1; n <= 3; n++) Passable(s.SmallBushes[n], 0.7f, fade);
            Passable(s.Grass);
            Passable(s.GrassBig);

            Passable(s.WoodResource);
            Passable(s.GoldResource);
            Passable(s.GoldResourceHighlight);
            Passable(s.PumpkinItem);
        }

        /// <summary>
        /// Health and drops from the design table (hp_damages_pieces, second version). Villagers hit for 10 until
        /// stats exist. Health -1 = collected directly into the inventory. Items lie on the ground in piles of 10.
        /// </summary>
        static void ApplyGameplayTables(Spawnables s)
        {
            var wood = s.WoodResource;
            var gold = s.GoldResource;
            var stone = s.Rock;
            var mushroom = s.Mushrooms[1];
            var pumpkin = s.PumpkinItem;
            const bool movable = true;

            // Items (collected directly, carried in Default mode)
            Gameplay(wood, -1, movable, 10, null, Drop(wood, 1));
            Gameplay(gold, -1, movable, 10, null, Drop(gold, 1));
            Gameplay(s.GoldResourceHighlight, -1, movable, 10, null, Drop(gold, 1));
            Gameplay(pumpkin, -1, movable, 10, null, Drop(pumpkin, 1));

            // Mushrooms, pumpkins, grass
            Gameplay(s.Mushrooms[1], -1, false, 10, null, Drop(mushroom, 1));
            Gameplay(s.Mushrooms[2], 15, Drop(mushroom, 2));
            Gameplay(s.Mushrooms[3], 40, Drop(mushroom, 4));
            Gameplay(s.Pumpkins[1], 40, Drop(pumpkin, 1));
            Gameplay(s.Pumpkins[2], 60, Drop(pumpkin, 3));
            Gameplay(s.Grass, -1);
            Gameplay(s.GrassBig, -1);

            // Stones, smallest to biggest (Rock = the stone item itself)
            Gameplay(s.Rock, -1, movable, 10, null, Drop(stone, 1));
            Gameplay(s.Stones[1], 60, Drop(stone, 3));
            Gameplay(s.Stones[3], 80, Drop(stone, 6));
            Gameplay(s.Stones[2], 110, Drop(stone, 10));
            Gameplay(s.Stones[4], 140, Drop(stone, 15));
            Gameplay(s.RockOld1, 60, Drop(stone, 3));
            Gameplay(s.RockOld2, 80, Drop(stone, 6));

            // Bushes (round: Bush 1 big, Bush 2 small, Bush_Green small / medium / big; leafy: Bush 3 big, Bush 4 small)
            Gameplay(s.Bushes[1], 25);
            Gameplay(s.Bushes[2], 10);
            Gameplay(s.Bushes[3], 20);
            Gameplay(s.Bushes[4], 8);
            Gameplay(s.SmallBushes[1], 10);
            Gameplay(s.SmallBushes[2], 15);
            Gameplay(s.SmallBushes[3], 25);

            // Others
            Gameplay(s.Bone1, -1, movable, 10, null, Drop(s.Bone1, 1, 0.5f));
            Gameplay(s.Bone2, -1, movable, 10, null, Drop(s.Bone2, 1, 0.5f));
            Gameplay(s.Signpost1, 30, movable, 10, null, Drop(wood, 3), Drop(s.Bone1, 3));
            Gameplay(s.Signpost2, 30, movable, 10, null, Drop(wood, 3));
            Gameplay(s.Scarecrow, 40, movable, 10, null, Drop(wood, 3), Drop(pumpkin, 1));

            // Gold stones (plain look after the first hit)
            int[] goldHealth = { 0, 50, 80, 150, 250, 400, 600 };
            int[] goldYield = { 0, 2, 4, 7, 11, 15, 20 };
            for (int n = 1; n <= 6; n++)
                Gameplay(s.GoldStones[n], goldHealth[n], false, 10, Frames(GoldStoneFolder + $"Gold Stone {n}.png"), Drop(gold, goldYield[n]));

            // Trees (Tree1 pine, Tree2 big pine, Tree3 leafy, Tree4 small leafy) and stumps
            int[] treeHealth = { 0, 90, 120, 60, 40 };
            int[] treeWood = { 0, 10, 14, 7, 4 };
            int[] stumpHealth = { 0, 45, 60, 30, 20 };
            int[] stumpWood = { 0, 2, 2, 1, 1 };
            for (int n = 1; n <= 4; n++)
            {
                Gameplay(s.Trees[n], treeHealth[n], Drop(wood, treeWood[n]));
                Gameplay(s.Stumps[n], stumpHealth[n], Drop(wood, stumpWood[n]));
            }

            // Not in the table yet: keep their health, only mark them as up to date
            for (int n = 1; n <= 4; n++) Gameplay(s.WaterRocks[n], Mathf.Max(1, s.WaterRocks[n].MaxHealth));
            foreach (var mine in new[] { s.GoldMine, s.GoldMineActive, s.GoldMineInactive })
                if (mine != null) Gameplay(mine, Mathf.Max(1, mine.MaxHealth));
        }

        // ================================================================== rule presets

        /// <summary>Forests: natural distribution noise (forests and clearings) + small clumps of trees.</summary>
        static PlacementSettings Forest(float density, float threshold, float outside, int minShore = 2) => new PlacementSettings
        {
            distribution = PlacementDistribution.Density,
            densityPer1000Cells = density,
            limitCount = true,
            countLimit = new IntRange(0, 60000),
            amountVariation = 0.2f,
            clusterSize = new IntRange(2, 6),
            clusterRadius = 3,
            useClustering = true,
            clusterNoise = new NoiseSettings(42f, 4),
            clusterThreshold = threshold,
            clusterFalloff = 0.1f,
            outsideClusterFactor = outside,
            // Trees are 3 cells wide: never two side by side
            minSpacingX = 1,
            minSpacingY = 0,
            shoreDistance = new IntRange(minShore, 9999),
            priority = 20,
            positionJitter = 0.3f,
        };

        static PlacementSettings Scatter(float density, float noiseScale, int priority, IntRange cluster, int minShore = 1,
            float variation = 0.35f, float absent = 0f) => new PlacementSettings
        {
            distribution = PlacementDistribution.Density,
            densityPer1000Cells = density,
            amountVariation = variation,
            absentChance = absent,
            clusterSize = cluster,
            clusterRadius = 2,
            useClustering = noiseScale > 0f,
            clusterNoise = new NoiseSettings(Mathf.Max(1f, noiseScale), 2),
            clusterThreshold = 0.45f,
            clusterFalloff = 0.2f,
            outsideClusterFactor = 0.3f,
            shoreDistance = new IntRange(minShore, 9999),
            priority = priority,
            positionJitter = 0.3f,
        };

        static PlacementSettings Counted(int min, int max, int priority, IntRange cluster, int spacing = 0, int minShore = 2,
            int distanceToOthers = 0, float variation = 0.25f) => new PlacementSettings
        {
            distribution = PlacementDistribution.CountRange,
            count = new IntRange(min, max),
            amountVariation = variation,
            clusterSize = cluster,
            clusterRadius = 2,
            minSpacingX = spacing,
            minSpacingY = spacing,
            minDistanceToOtherObjects = distanceToOthers,
            shoreDistance = new IntRange(minShore, 9999),
            priority = priority,
            positionJitter = 0.3f,
        };

        static readonly IntRange Single = new IntRange(1, 1);

        static SpawnRule Rule(string label, List<WeightedSpawnable> variants, PlacementSettings placement, float stumpChance = 0f) =>
            new SpawnRule(label) { variants = variants, placement = placement, stumpChance = stumpChance };

        // ================================================================== biomes

        static BiomeDefinition Biome(string file, string displayName, Color color, GroundTileSet tiles, float speed,
            BiomeGenerationProfile profile, List<SpawnRule> rules)
        {
            var asset = LoadOrCreate<BiomeDefinition>($"{BiomeFolder}/{file}.asset", out bool fresh);
            if (!fresh) return asset;
            asset.EditorSetup(displayName, color, tiles, speed, profile, rules);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static BiomeGenerationProfile Filler(float weight, float ponds) => new BiomeGenerationProfile
        {
            placementMode = BiomePlacementMode.Filler,
            referencePlacement = ReferencePlacement.UseReferenceAreas,
            tags = BiomeTags.Grassland,
            fillerWeight = weight,
            pondsPer10000Cells = ponds,
        };

        const float TreeStumpChance = 0.03f;

        static List<WeightedSpawnable> Stones(Spawnables s) => Variants(s.Stones[1], s.Stones[2], s.Stones[3], s.Stones[4]);
        static List<WeightedSpawnable> SmallGold(Spawnables s) => Variants(s.GoldStones[1], s.GoldStones[2], s.GoldStones[3], s.GoldStones[4]);
        static List<WeightedSpawnable> Mushrooms(Spawnables s) => Variants(s.Mushrooms[1], s.Mushrooms[2], s.Mushrooms[3]);

        static List<BiomeDefinition> BuildBiomes(TileSets tiles, Spawnables s)
        {
            var biomes = new List<BiomeDefinition>();
            var t = s.Trees;
            var b = s.Bushes;
            var stoneCluster = new IntRange(1, 3);
            var bushCluster = new IntRange(1, 4);

            // 0 - base layer. color1: tree1 + tree3, bush2 + bush3
            biomes.Add(Biome("Grass1", "Grass 1 (color1)", new Color32(150, 190, 80, 255), tiles.Color[1], 1f, Filler(0.25f, 0.8f),
                new List<SpawnRule>
                {
                    Rule("Trees", Variants(t[1], t[3]), Forest(350f, 0.60f, 0.03f), TreeStumpChance),
                    Rule("Bushes", Variants(b[2], b[3]), Scatter(50f, 14f, 30, bushCluster)),
                    Rule("Stones", Stones(s), Scatter(8f, 18f, 40, stoneCluster)),
                    Rule("Gold Stones", SmallGold(s), Scatter(1.2f, 0f, 15, new IntRange(1, 2), 2, 0.5f)),
                }));

            // color2: tree2 + tree3, bush2 + bush4, mushrooms, gold, water, stones (pumpkins: map-wide rule)
            biomes.Add(Biome("Grass2", "Grass 2 (color2)", new Color32(120, 175, 70, 255), tiles.Color[2], 1f, Filler(0.30f, 1.6f),
                new List<SpawnRule>
                {
                    Rule("Trees", Variants(t[2], t[3]), Forest(380f, 0.57f, 0.04f), TreeStumpChance),
                    Rule("Bushes", Variants(b[2], b[4]), Scatter(60f, 14f, 30, bushCluster)),
                    Rule("Mushrooms", Mushrooms(s), Scatter(6f, 12f, 35, new IntRange(2, 5), 1, 0.5f, 0.1f)),
                    Rule("Stones", Stones(s), Scatter(8f, 18f, 40, stoneCluster)),
                    Rule("Gold Stones", SmallGold(s), Scatter(1.5f, 0f, 15, new IntRange(1, 2), 2, 0.5f)),
                }));

            // color3: like color2 but denser; tree1 + tree2 + tree3, bush2 + bush3 + bush4
            biomes.Add(Biome("Grass3", "Grass 3 (color3)", new Color32(90, 150, 60, 255), tiles.Color[3], 1f, Filler(0.20f, 2.2f),
                new List<SpawnRule>
                {
                    Rule("Trees", Variants(t[1], t[2], t[3]), Forest(450f, 0.50f, 0.07f), TreeStumpChance),
                    Rule("Bushes", Variants(b[2], b[3], b[4]), Scatter(100f, 14f, 30, bushCluster)),
                    Rule("Mushrooms", Mushrooms(s), Scatter(12f, 12f, 35, new IntRange(2, 6), 1, 0.5f, 0.1f)),
                    Rule("Stones", Stones(s), Scatter(12f, 18f, 40, stoneCluster)),
                    Rule("Gold Stones", SmallGold(s), Scatter(2.5f, 0f, 15, new IntRange(1, 3), 2, 0.5f)),
                }));

            // color4: tree3 + tree4, bush1 + bush4
            biomes.Add(Biome("Grass4", "Grass 4 (color4)", new Color32(170, 170, 90, 255), tiles.Color[4], 1f, Filler(0.25f, 0.8f),
                new List<SpawnRule>
                {
                    Rule("Trees", Variants(t[3], t[4]), Forest(400f, 0.55f, 0.05f), TreeStumpChance),
                    Rule("Bushes", Variants(b[1], b[4]), Scatter(50f, 14f, 30, bushCluster)),
                    Rule("Stones", Stones(s), Scatter(10f, 18f, 40, stoneCluster)),
                    Rule("Gold Stones", SmallGold(s), Scatter(1.5f, 0f, 15, new IntRange(1, 2), 2, 0.5f)),
                }));

            // Swamp (color5): very dense trees and lots of water; characters walk at x0.25.
            // The trees are the existing ones until the swamp tree asset arrives (then only this rule changes).
            var swampForest = Forest(480f, 0.38f, 0.45f, 1);
            swampForest.clusterSize = new IntRange(3, 8);
            biomes.Add(Biome("Swamp", "Swamp (color5)", new Color32(80, 110, 70, 255), tiles.Color[5], 0.25f,
                new BiomeGenerationProfile
                {
                    placementMode = BiomePlacementMode.Regional,
                    referencePlacement = ReferencePlacement.UseReferenceAreas,
                    tags = BiomeTags.Wet,
                    mapCoverage = 0.03f,
                    regionCount = new IntRange(5, 8),
                    regionSizeVariation = 0.3f,
                    placementPriority = 2,
                    preferredWaterDistance = new IntRange(0, 20),
                    waterPreferenceStrength = 3f,
                    pondsPer10000Cells = 0f,
                    marshWaterRatio = 0.30f,
                },
                new List<SpawnRule>
                {
                    Rule("Swamp Trees", Variants(t[1], t[2], t[3], t[4]), swampForest, TreeStumpChance),
                    Rule("Stones", Stones(s), Scatter(6f, 18f, 40, stoneCluster)),
                }));

            // Desert (old Tilemap_Flat sand): 5% of the map in a few big areas, no snow in or around it, stones only
            biomes.Add(Biome("Desert", "Desert (Tilemap_Flat)", new Color32(235, 210, 120, 255), tiles.Desert, 1f,
                new BiomeGenerationProfile
                {
                    placementMode = BiomePlacementMode.Regional,
                    referencePlacement = ReferencePlacement.RegionalQuota,
                    tags = BiomeTags.Arid,
                    mapCoverage = 0.05f,
                    regionCount = new IntRange(2, 4),
                    regionSizeVariation = 0.3f,
                    placementPriority = 0,
                    preferredWaterDistance = new IntRange(12, 9999),
                    waterPreferenceStrength = 2.5f,
                    repelledTags = BiomeTags.Wet | BiomeTags.Snowy,
                    repelDistance = 14,
                    pondsPer10000Cells = 0.05f,
                },
                new List<SpawnRule>
                {
                    Rule("Stones", Stones(s), Scatter(25f, 20f, 40, stoneCluster)),
                }));

            // Rock (old Tilemap_Elevation, flat top only): more but smaller areas; no trees / bushes;
            // lots of gold, Gold Stone 5-6 only here
            biomes.Add(Biome("Rock", "Rock (Tilemap_Elevation)", new Color32(130, 150, 150, 255), tiles.Rock, 1f,
                new BiomeGenerationProfile
                {
                    placementMode = BiomePlacementMode.Regional,
                    referencePlacement = ReferencePlacement.RegionalQuota,
                    tags = BiomeTags.Rocky,
                    mapCoverage = 0.03f,
                    regionCount = new IntRange(10, 14),
                    regionSizeVariation = 0.35f,
                    placementPriority = 1,
                    preferredWaterDistance = new IntRange(4, 9999),
                    waterPreferenceStrength = 1f,
                    pondsPer10000Cells = 0.2f,
                },
                new List<SpawnRule>
                {
                    Rule("Stones", Stones(s), Scatter(50f, 16f, 40, new IntRange(1, 4))),
                    Rule("Gold Stones", Variants(s.GoldStones[1], s.GoldStones[2], s.GoldStones[3], s.GoldStones[4], s.GoldStones[5], s.GoldStones[6]),
                        Scatter(20f, 20f, 15, new IntRange(1, 3), 2)),
                }));

            return biomes;
        }

        // ================================================================== reference map

        static ReferenceMapAsset BuildReferenceMap(int width, int height, ReferenceMapSettings referenceSettings)
        {
            var asset = LoadOrCreate<ReferenceMapAsset>(ReferenceMapPath, out bool fresh);
            if (fresh) asset.EditorSetSource(ReferenceSourcePath);
            if (System.IO.File.Exists(asset.SourcePath)) asset.Import(width, height, referenceSettings);
            else Debug.LogWarning("[King of Nothing] Reference map source not found: " + asset.SourcePath);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static List<ReferenceBiomeMapping> DefaultReferenceMapping(List<BiomeDefinition> b)
        {
            // Same mapping as the original map generator
            return new List<ReferenceBiomeMapping>
            {
                new ReferenceBiomeMapping(ReferenceBiome.HotDesert, b[5]),
                new ReferenceBiomeMapping(ReferenceBiome.ColdDesert, b[5]),
                new ReferenceBiomeMapping(ReferenceBiome.Savanna, b[0]),
                new ReferenceBiomeMapping(ReferenceBiome.TropicalSeasonalForest, b[0]),
                new ReferenceBiomeMapping(ReferenceBiome.Grassland, b[1]),
                new ReferenceBiomeMapping(ReferenceBiome.TropicalRainforest, b[1]),
                new ReferenceBiomeMapping(ReferenceBiome.TemperateDeciduousForest, b[2]),
                new ReferenceBiomeMapping(ReferenceBiome.TemperateRainforest, b[2]),
                new ReferenceBiomeMapping(ReferenceBiome.Taiga, b[3]),
                new ReferenceBiomeMapping(ReferenceBiome.Tundra, b[6]),
                new ReferenceBiomeMapping(ReferenceBiome.Glacier, b[6]),
                new ReferenceBiomeMapping(ReferenceBiome.Wetland, b[4]),
                new ReferenceBiomeMapping(ReferenceBiome.Marine, b[1]),
            };
        }

        // ================================================================== settings

        static MapGenerationSettings BuildSettings(List<BiomeDefinition> biomes, Spawnables s)
        {
            var settings = LoadOrCreate<MapGenerationSettings>(SettingsPath, out bool fresh);
            if (!fresh)
            {
                // Keep tuned values; only make sure every biome is listed and the reference map exists
                foreach (var biome in biomes)
                    if (!settings.biomes.Contains(biome)) settings.biomes.Add(biome);
                if (settings.referenceMap == null)
                    settings.referenceMap = BuildReferenceMap(settings.width, settings.height, settings.referenceSettings);
                if (settings.referenceBiomeMapping.Count == 0) settings.referenceBiomeMapping = DefaultReferenceMapping(biomes);
                foreach (var extra in s.DeveloperExtras)
                    if (!settings.extraDeveloperObjects.Contains(extra)) settings.extraDeveloperObjects.Add(extra);
                EditorUtility.SetDirty(settings);
                return settings;
            }

            settings.width = 1800;
            settings.height = 800;
            settings.mapSource = MapSource.ReferenceMap;
            settings.referenceSettings = new ReferenceMapSettings();
            settings.referenceMap = BuildReferenceMap(settings.width, settings.height, settings.referenceSettings);
            settings.referenceBiomeMapping = DefaultReferenceMapping(biomes);
            settings.landmass = new LandmassSettings();
            // The reference map brings its own lakes and big rivers: add only a few lakes and highland rivers
            settings.lakes = new LakeSettings { count = new IntRange(4, 8) };
            settings.rivers = new RiverSettings { sourceCount = new IntRange(4, 8) };
            settings.wetlands = new WetlandSettings();
            settings.landBalance = new LandBalanceSettings();
            settings.biomeLayout = new BiomeLayoutSettings();
            settings.startArea = new StartAreaSettings();
            settings.biomes = new List<BiomeDefinition>(biomes);
            settings.waterFoamTile = AssetDatabase.LoadAssetAtPath<TileBase>(TS + "Terrain/Tileset/Tilemap Settings/Water Tile animated.asset");

            var grass = biomes.Take(4).ToList();
            var rock = biomes[6];
            var desert = biomes[5];

            // Gold mines: anywhere except swamp, desert and water; rare and far apart
            var mines = new GlobalSpawnRule("Gold Mines")
            {
                variants = Variants(s.GoldMine),
                placement = Counted(25, 40, 0, Single, spacing: 40, minShore: 3, distanceToOthers: 1, variation: 0.2f),
                allowedBiomes = new List<BiomeDefinition>(grass) { rock },
            };

            // Pumpkins in color2 and color3, total number on the whole map (min-max)
            var pumpkins = new GlobalSpawnRule("Pumpkins")
            {
                variants = Variants(s.Pumpkins[1], s.Pumpkins[2]),
                placement = Counted(150, 300, 12, new IntRange(1, 4), variation: 0.3f),
                allowedBiomes = new List<BiomeDefinition> { biomes[1], biomes[2] },
            };

            // Skulls with up to 3 bones one cell around them (any mix of Bone1 / Bone2). Waiting for the skull asset:
            // put it into Variants and enable the rule.
            var skulls = new GlobalSpawnRule("Skulls (waiting for the skull asset)")
            {
                enabled = false,
                variants = new List<WeightedSpawnable>(),
                placement = Counted(100, 180, 5, Single, spacing: 6, distanceToOthers: 1),
                companions = new CompanionSpawn
                {
                    enabled = true,
                    count = new IntRange(0, 3),
                    ringRadius = 1,
                    variants = Variants(s.Bone1, s.Bone2),
                },
                allowedBiomes = new List<BiomeDefinition>(grass) { desert },
            };

            var bones = new GlobalSpawnRule("Bones")
            {
                variants = Variants(s.Bone1, s.Bone2),
                placement = Counted(250, 300, 50, new IntRange(1, 2), variation: 0.3f),
                allowedBiomes = new List<BiomeDefinition>(grass) { desert },
            };

            // Rocks in the water near the shore (sea, lakes, rivers)
            var waterRocks = new GlobalSpawnRule("Water Rocks")
            {
                variants = Variants(s.WaterRocks[1], s.WaterRocks[2], s.WaterRocks[3], s.WaterRocks[4]),
                allowedTerrain = TerrainMask.Sea | TerrainMask.Lake | TerrainMask.River,
                placement = new PlacementSettings
                {
                    distribution = PlacementDistribution.Density,
                    densityPer1000Cells = 8f,
                    amountVariation = 0.3f,
                    clusterSize = new IntRange(1, 3),
                    clusterRadius = 2,
                    shoreDistance = new IntRange(2, 4),
                    minSpacingX = 1,
                    minSpacingY = 1,
                    priority = 60,
                    positionJitter = 0f,
                },
            };

            settings.globalSpawnRules = new List<GlobalSpawnRule> { mines, pumpkins, skulls, bones, waterRocks };
            settings.extraDeveloperObjects = new List<SpawnableDefinition>(s.DeveloperExtras);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>
        /// The original map asset (baked on the first Play, never overwritten here: it is the frozen original).
        /// Removes the edit log of older versions.
        /// </summary>
        static void BuildMapFiles()
        {
            OriginalMapAsset.LoadOrCreateDefault();
            MovementSettings.LoadOrCreateDefault();
            InteractionSettings.LoadOrCreateDefault().EditorEnsureDefaults();
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ObsoleteEditLogPath, AssetPathToGUIDOptions.OnlyExistingAssets)))
                AssetDatabase.DeleteAsset(ObsoleteEditLogPath);
        }

        // ================================================================== characters

        static void BuildNamePool()
        {
            var pool = LoadOrCreate<NamePool>(NamePoolPath, out bool fresh);
            if (fresh) EditorUtility.SetDirty(pool);
        }

        static TraitDefinition Trait(string file, string displayName, string description, string flavor, TraitTrigger trigger,
            float chance, bool hidden, float work, float move, float moraleDrain)
        {
            var asset = LoadOrCreate<TraitDefinition>($"{TraitFolder}/{file}.asset", out bool fresh);
            if (!fresh) return asset;
            asset.EditorSetup(displayName, description, flavor, trigger, chance, hidden, work, move, moraleDrain);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>A few GDD traits as templates; the ones that change speed are used by the starting party.</summary>
        public static List<TraitDefinition> BuildTraits()
        {
            return new List<TraitDefinition>
            {
                Trait("Hasty", "Hasty", "Always runs.", "Why walk when you can run?", TraitTrigger.GameStart, 1f, false, 1f, 1.4f, 0f),
                Trait("NightOwl", "Night Owl", "Yawns during the day and works slower; works faster at night.",
                    "Sleep is for the weak.", TraitTrigger.Random, 0.05f, true, 0.8f, 1f, 0f),
                Trait("StoneEater", "Stone Eater", "Eats stones when starving; walks slower afterwards.",
                    "Crunchy.", TraitTrigger.Starvation, 0.3f, true, 1f, 0.7f, 0f),
                Trait("Sluggish", "Sluggish", "Takes their time. Every time.", "No rush.", TraitTrigger.GameStart, 1f, false, 1f, 0.75f, 0f),
            };
        }

        /// <summary>Every "Pawn_*" sheet of one colour: idle / run, tools (idle, run, interact) and carrying.</summary>
        static PawnAnimationSet BuildPawnAnimations()
        {
            var set = LoadOrCreate<PawnAnimationSet>(PawnAnimationsPath, out bool fresh);
            if (!fresh && set.idle != null && set.idle.Length > 0 && set.tools.Count > 0) return set;
            set.idle = Frames(PawnFolder + "Pawn_Idle.png");
            set.run = Frames(PawnFolder + "Pawn_Run.png");
            foreach (var tool in new[] { InteractionTool.Axe, InteractionTool.Pickaxe, InteractionTool.Knife, InteractionTool.Hammer })
                set.EditorSetTool(tool, Frames(PawnFolder + $"Pawn_Idle {tool}.png"), Frames(PawnFolder + $"Pawn_Run {tool}.png"),
                    Frames(PawnFolder + $"Pawn_Interact {tool}.png"));
            set.idleWood = Frames(PawnFolder + "Pawn_Idle Wood.png");
            set.runWood = Frames(PawnFolder + "Pawn_Run Wood.png");
            set.idleGold = Frames(PawnFolder + "Pawn_Idle Gold.png");
            set.runGold = Frames(PawnFolder + "Pawn_Run Gold.png");
            set.idleMeat = Frames(PawnFolder + "Pawn_Idle Meat.png");
            set.runMeat = Frames(PawnFolder + "Pawn_Run Meat.png");
            EditorUtility.SetDirty(set);
            return set;
        }

        /// <summary>Children the scripts create themselves at runtime; saved copies in the prefab drew everything twice.</summary>
        static readonly string[] RuntimeChildren = { "Name Tag", "Selection Ring", "Path Line", "Destination Marker" };

        static void BuildVillagerPrefab()
        {
            var animations = BuildPawnAnimations();
            if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(VillagerPrefabPath) != null)
            {
                // Existing prefab: remove saved runtime children, connect the pawn animations
                var contents = PrefabUtility.LoadPrefabContents(VillagerPrefabPath);
                try
                {
                    foreach (var childName in RuntimeChildren)
                        for (int i = contents.transform.childCount - 1; i >= 0; i--)
                            if (contents.transform.GetChild(i).name == childName)
                                Object.DestroyImmediate(contents.transform.GetChild(i).gameObject);
                    var existingUnit = contents.GetComponent<Unit>();
                    if (existingUnit != null && existingUnit.Animations == null) existingUnit.Animations = animations;
                    PrefabUtility.SaveAsPrefabAsset(contents, VillagerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                return;
            }
            TilesetSlicer.EnsureFolder(System.IO.Path.GetDirectoryName(VillagerPrefabPath).Replace('\\', '/'));

            var idle = animations.idle;
            var run = animations.run;
            var root = new GameObject("Villager");
            try
            {
                // The Pawn frame is 192x192 (3x3 units); the feet are about 1 unit above its bottom
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                var body = visual.AddComponent<SpriteRenderer>();
                body.sprite = idle.Length > 0 ? idle[0] : null;

                var collider = root.AddComponent<BoxCollider2D>();
                collider.offset = new Vector2(0f, 0.5f);
                collider.size = new Vector2(0.7f, 1f);

                var unit = root.AddComponent<Unit>();
                unit.SetVisuals(body, idle, run);
                unit.Animations = animations;
                root.AddComponent<Villager>();

                PrefabUtility.SaveAsPrefabAsset(root, VillagerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
