using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tiny Swords asset'leriyle rastgele, 5 biyomlu doğa haritası üretir.
/// Menü: RTS > Harita Oluştur  →  Assets/Scenes/RTSMap.unity
/// </summary>
public static class RTSMapGenerator
{
    const string TS = "Assets/Tiny Swords/";
    const string TilesFolder = TS + "Terrain/Tileset/Tilemap Settings";
    const string ScenePath = "Assets/Scenes/RTSMap.unity";
    const string TemplateScene = "Assets/Scenes/SampleScene.unity";

    const int W = 288;
    const int H = MapSorting.MapHeight; // çizim sırası formülü harita yüksekliğine bağlı
    const int WaterPadding = 40;
    const float S = W / 256f; // gürültü ölçeği: harita büyüdükçe kıta/biyom/orman da büyüsün

    // ---------------------------------------------------------------- Biyomlar

    class Biome
    {
        public string name;
        public int tileset;          // Tilemap_colorN
        public float forestThreshold; // orman gürültüsü bu değeri geçince orman başlar (düşük = daha çok orman)
        public float loneTree;       // ormanın dışında tek ağaç olasılığı (hücre başına)
        public float pineRatio;      // çam (Tree1/2) oranı, kalanı yapraklı ağaç (Tree3/4)
        public float bush;           // çalı olasılığı (hücre başına)
        public float rock;           // küçük taş olasılığı (hücre başına)
        public float goldPer10k;     // 10.000 hücre başına altın madeni
        public float sheepPer10k;    // 10.000 hücre başına koyun sürüsü
    }

    // Sıra önemli: 0 = taban katman, diğerleri üstüne biner
    static readonly Biome[] Biomes =
    {
        new Biome { name = "Savana",   tileset = 1, forestThreshold = 0.62f, loneTree = 0.015f, pineRatio = 0.25f, bush = 0.07f, rock = 0.015f, goldPer10k = 3f, sheepPer10k = 3f },
        new Biome { name = "Çayır",    tileset = 2, forestThreshold = 0.57f, loneTree = 0.025f, pineRatio = 0.40f, bush = 0.12f, rock = 0.006f, goldPer10k = 2.5f, sheepPer10k = 5f },
        new Biome { name = "Orman",    tileset = 3, forestThreshold = 0.47f, loneTree = 0.060f, pineRatio = 0.50f, bush = 0.09f, rock = 0.004f, goldPer10k = 2f, sheepPer10k = 1f },
        new Biome { name = "Kurak",    tileset = 4, forestThreshold = 0.75f, loneTree = 0.004f, pineRatio = 0.10f, bush = 0.03f, rock = 0.060f, goldPer10k = 8f, sheepPer10k = 0f },
        new Biome { name = "Tayga",    tileset = 5, forestThreshold = 0.50f, loneTree = 0.045f, pineRatio = 0.90f, bush = 0.06f, rock = 0.012f, goldPer10k = 3f, sheepPer10k = 1f },
    };

    static System.Random rng;
    static Tilemap groundBase;
    static bool[,] land, occupied, treeAt;
    static int[,] waterDist, biome;
    static Dictionary<Vector2Int, TileBase>[] tiles;

    // Takımdaki herkes aynı haritayı üretsin diye sabit. Haritayı değiştirmek için bu sayıyı değiştirip
    // commit'leyin; "Rastgele" menüsünün Console'a yazdığı seed'i buraya yazarak beğendiğiniz haritayı sabitleyebilirsiniz.
    const int MapSeed = 20261020;

    [MenuItem("RTS/Harita Oluştur")]
    public static void GenerateFixed() => Generate(MapSeed);

    [MenuItem("RTS/Rastgele Harita Dene")]
    public static void GenerateRandom() => Generate(Random.Range(0, int.MaxValue));

    static void Generate(int seed)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        rng = new System.Random(seed);
        if (!LoadTiles())
            return;

        try
        {
            EditorUtility.DisplayProgressBar("RTS Harita", "Arazi oluşturuluyor...", 0.1f);
            BuildLand();
            ComputeWaterDistance();
            BuildBiomes();

            EditorUtility.DisplayProgressBar("RTS Harita", "Tilemap dolduruluyor...", 0.3f);
            var scene = CreateScene();
            BuildTilemaps();

            EditorUtility.DisplayProgressBar("RTS Harita", "Doğa yerleştiriliyor...", 0.6f);
            occupied = new bool[W, H];
            treeAt = new bool[W, H];
            var pawn = PlacePawn();
            var objects = new GameObject("Nature").transform;
            PlaceForests(objects);
            PlaceResources(objects);
            PlaceScatter(objects);
            PlaceWaterRocks(objects);
            SetupCamera(pawn.transform.position);
            new GameObject("Selection Manager").AddComponent<SelectionManager>().walkableGround = groundBase;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild();
            Debug.Log($"RTS haritası oluşturuldu ({W}x{H}, seed {seed}): {ScenePath}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------------------------------------------------------------- Gürültü

    static float Fbm(float x, float y, Vector2 offset, int octaves)
    {
        float sum = 0, norm = 0, amp = 1, freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(offset.x + x * freq, offset.y + y * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    static Vector2 RandomOffset() => new Vector2(rng.Next(0, 100000) * 0.37f, rng.Next(0, 100000) * 0.37f);

    // ---------------------------------------------------------------- Arazi

    static void BuildLand()
    {
        land = new bool[W, H];
        var elevationOff = RandomOffset();
        var warpX = RandomOffset();
        var warpY = RandomOffset();

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            // Domain warp: kıyıları doğal ve düzensiz yapar
            float wx = x + (Fbm(x / (70f * S), y / (70f * S), warpX, 3) - 0.5f) * 60f * S;
            float wy = y + (Fbm(x / (70f * S), y / (70f * S), warpY, 3) - 0.5f) * 60f * S;

            float e = Fbm(wx / (90f * S), wy / (90f * S), elevationOff, 6);

            // Sadece harita sınırına yakın şeritte denize düşen maske (yuvarlatılmış dikdörtgen)
            // (warp'sız koordinat: aksi halde kara harita sınırına kadar taşıp düz kesiliyor)
            float nx = Mathf.Abs((x - W / 2f) / (W / 2f));
            float ny = Mathf.Abs((y - H / 2f) / (H / 2f));
            float d = Mathf.Pow(Mathf.Pow(nx, 6) + Mathf.Pow(ny, 6), 1f / 6f)
                    + (Fbm(x / (25f * S), y / (25f * S), warpX, 3) - 0.5f) * 0.10f;
            float mask = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.88f, 0.98f, d));

            land[x, y] = e - (1f - mask) * 0.6f > 0.36f;
        }

        // Harita kenarında her zaman deniz kalsın
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (x < 4 || y < 4 || x >= W - 4 || y >= H - 4)
                land[x, y] = false;

        for (int i = 0; i < 3; i++) Smooth(land);
        RemoveSmallRegions(true, 200);  // minik adacıklar
        RemoveSmallRegions(false, 120); // minik su birikintileri
    }

    static void Smooth(bool[,] grid)
    {
        var next = new bool[W, H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            int count = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if ((dx != 0 || dy != 0) && In(x + dx, y + dy) && grid[x + dx, y + dy]) count++;
            next[x, y] = count > 4 || (count == 4 && grid[x, y]);
        }
        System.Array.Copy(next, grid, next.Length);
    }

    static void RemoveSmallRegions(bool value, int minSize)
    {
        foreach (var comp in Components((x, y) => land[x, y] == value))
        {
            bool touchesBorder = comp.Any(p => p.x == 0 || p.y == 0 || p.x == W - 1 || p.y == H - 1);
            if (comp.Count < minSize && !(value == false && touchesBorder))
                foreach (var p in comp) land[p.x, p.y] = !value;
        }
    }

    /// <summary>4-komşulukla bağlı bölgeler. groupBy verilirse aynı değere sahip hücreler aynı bölgede toplanır.</summary>
    static List<List<Vector2Int>> Components(System.Func<int, int, bool> include, int[,] groupBy = null)
    {
        var seen = new bool[W, H];
        var result = new List<List<Vector2Int>>();
        var q = new Queue<Vector2Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (seen[x, y] || !include(x, y)) continue;
            int group = groupBy != null ? groupBy[x, y] : 0;
            var comp = new List<Vector2Int>();
            q.Enqueue(new Vector2Int(x, y));
            seen[x, y] = true;
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                comp.Add(p);
                foreach (var d in Dirs4)
                {
                    var n = p + d;
                    if (In(n.x, n.y) && !seen[n.x, n.y] && include(n.x, n.y)
                        && (groupBy == null || groupBy[n.x, n.y] == group))
                    {
                        seen[n.x, n.y] = true;
                        q.Enqueue(n);
                    }
                }
            }
            result.Add(comp);
        }
        return result;
    }

    static void ComputeWaterDistance()
    {
        waterDist = new int[W, H];
        var q = new Queue<Vector2Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            waterDist[x, y] = land[x, y] ? int.MaxValue : 0;
            if (!land[x, y]) q.Enqueue(new Vector2Int(x, y));
        }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Dirs8)
            {
                var n = p + d;
                if (In(n.x, n.y) && waterDist[n.x, n.y] > waterDist[p.x, p.y] + 1)
                {
                    waterDist[n.x, n.y] = waterDist[p.x, p.y] + 1;
                    q.Enqueue(n);
                }
            }
        }
    }

    // ---------------------------------------------------------------- Biyom haritası

    // Nem ve sıcaklık gürültüsünden biyom seçilir:
    //   kuru + sıcak → Kurak, kuru + serin → Savana, orta nem → Çayır,
    //   nemli + sıcak → Orman, nemli + serin → Tayga
    static void BuildBiomes()
    {
        biome = new int[W, H];
        var moistOff = RandomOffset();
        var tempOff = RandomOffset();
        var warpOff = RandomOffset();
        var jitterOff = RandomOffset();

        var moist = new float[W, H];
        var temp = new float[W, H];
        var landM = new List<float>();
        var landT = new List<float>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            float wx = x + (Fbm(x / (40f * S), y / (40f * S), warpOff, 2) - 0.5f) * 40f * S;
            float wy = y + (Fbm(y / (40f * S), x / (40f * S), warpOff, 2) - 0.5f) * 40f * S;
            // Hafif titreşim: biyom sınırı cetvelle çizilmiş gibi durmasın, ama sınır boyunca
            // birkaç adımda bir biyom değişecek kadar da karışmasın
            float jm = (Fbm(x / 12f, y / 12f, jitterOff, 2) - 0.5f) * 0.025f;
            float jt = (Fbm(y / 12f, x / 12f, jitterOff, 2) - 0.5f) * 0.025f;
            moist[x, y] = Fbm(wx / (75f * S), wy / (75f * S), moistOff, 3) + jm;
            temp[x, y] = Fbm(wx / (95f * S), wy / (95f * S), tempOff, 3) + jt;
            if (land[x, y]) { landM.Add(moist[x, y]); landT.Add(temp[x, y]); }
        }

        // Yüzdelik eşikler: her haritada 5 biyomun da makul payı olsun
        landM.Sort();
        landT.Sort();
        float m1 = Percentile(landM, 0.33f), m2 = Percentile(landM, 0.62f), tMid = Percentile(landT, 0.5f);

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            float m = moist[x, y], t = temp[x, y];
            int b;
            if (m < m1) b = t > tMid ? 3 : 0;       // Kurak / Savana
            else if (m < m2) b = 1;                 // Çayır
            else b = t > tMid ? 2 : 4;              // Orman / Tayga
            biome[x, y] = b;
        }

        // Sınırları yumuşat, küçük biyom parçalarını komşusuna kat: her biyom geniş bir bölge olsun
        for (int i = 0; i < 3; i++) SmoothBiomes();
        for (int pass = 0; pass < 2; pass++)
            foreach (var comp in Components((x, y) => land[x, y], biome))
            {
                if (comp.Count >= 400) continue;
                int replacement = MostCommonNeighbourBiome(comp);
                foreach (var p in comp) biome[p.x, p.y] = replacement;
            }

        BuildBiomeBlend(10);
    }

    // Her hücre için çevresindeki biyom oranları (kutu bulanıklığı). Ağaç/taş yoğunluğu bununla
    // karıştırılır, böylece orman biyom sınırında aniden kesilmez, yavaşça seyrekleşir.
    static float[,,] blend;

    static void BuildBiomeBlend(int radius)
    {
        int n = Biomes.Length;
        var tmp = new float[W, H, n];
        blend = new float[W, H, n];

        for (int b = 0; b < n; b++)
        {
            // Yatay geçiş
            for (int y = 0; y < H; y++)
            {
                float sum = 0;
                for (int x = -radius; x <= radius; x++) sum += biome[Mathf.Clamp(x, 0, W - 1), y] == b ? 1 : 0;
                for (int x = 0; x < W; x++)
                {
                    tmp[x, y, b] = sum;
                    sum -= biome[Mathf.Clamp(x - radius, 0, W - 1), y] == b ? 1 : 0;
                    sum += biome[Mathf.Clamp(x + radius + 1, 0, W - 1), y] == b ? 1 : 0;
                }
            }
            // Dikey geçiş
            for (int x = 0; x < W; x++)
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++) sum += tmp[x, Mathf.Clamp(y, 0, H - 1), b];
                for (int y = 0; y < H; y++)
                {
                    blend[x, y, b] = sum;
                    sum -= tmp[x, Mathf.Clamp(y - radius, 0, H - 1), b];
                    sum += tmp[x, Mathf.Clamp(y + radius + 1, 0, H - 1), b];
                }
            }
        }

        float area = (2 * radius + 1) * (2 * radius + 1);
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        for (int b = 0; b < n; b++)
            blend[x, y, b] /= area;
    }

    /// <summary>Biyom parametresinin sınırlarda yumuşatılmış değeri.</summary>
    static float Param(int x, int y, System.Func<Biome, float> get)
    {
        float v = 0;
        for (int b = 0; b < Biomes.Length; b++) v += blend[x, y, b] * get(Biomes[b]);
        return v;
    }

    static float Percentile(List<float> sorted, float p) =>
        sorted.Count == 0 ? 0.5f : sorted[Mathf.Clamp((int)(sorted.Count * p), 0, sorted.Count - 1)];

    static void SmoothBiomes()
    {
        var next = (int[,])biome.Clone();
        var counts = new int[Biomes.Length];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            System.Array.Clear(counts, 0, counts.Length);
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
                if (In(x + dx, y + dy)) counts[biome[x + dx, y + dy]]++;
            int best = biome[x, y];
            for (int b = 0; b < counts.Length; b++)
                if (counts[b] > counts[best]) best = b;
            next[x, y] = best;
        }
        biome = next;
    }

    static int MostCommonNeighbourBiome(List<Vector2Int> comp)
    {
        int own = biome[comp[0].x, comp[0].y];
        var counts = new int[Biomes.Length];
        foreach (var p in comp)
        foreach (var d in Dirs4)
        {
            var n = p + d;
            if (Land(n.x, n.y) && biome[n.x, n.y] != own) counts[biome[n.x, n.y]]++;
        }
        int best = own, bestCount = 0;
        for (int b = 0; b < counts.Length; b++)
            if (counts[b] > bestCount) { best = b; bestCount = counts[b]; }
        return best;
    }

    // ---------------------------------------------------------------- Tilemap

    static bool LoadTiles()
    {
        tiles = new Dictionary<Vector2Int, TileBase>[6];
        for (int i = 1; i <= 5; i++) tiles[i] = new Dictionary<Vector2Int, TileBase>();

        foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { TilesFolder + "/Sliced Tiles" }))
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
            if (tile == null || tile.sprite == null) continue;
            string tex = tile.sprite.texture.name;
            if (!tex.StartsWith("Tilemap_color") || !int.TryParse(tex.Substring(13), out int set) || set < 1 || set > 5)
                continue;
            var r = tile.sprite.rect;
            // Anahtar: (sütun, üstten satır)
            tiles[set][new Vector2Int((int)r.x / 64, 5 - (int)r.y / 64)] = tile;
        }

        for (int i = 1; i <= 5; i++)
            if (tiles[i].Count < 16)
            {
                Debug.LogError($"Tilemap_color{i} tile'ları eksik: {TilesFolder}/Sliced Tiles");
                return false;
            }
        return true;
    }

    // Tiny Swords düz zemin 4x4 bloğu: 3x3 köşe/kenar + dikey şerit + yatay şerit + tek parça
    static int ColFor(bool w, bool e) => w && e ? 1 : !w && e ? 0 : w ? 2 : 3;
    static int RowFor(bool n, bool s) => n && s ? 1 : !n && s ? 0 : n ? 2 : 3;

    static TileBase GroundTile(int set, bool n, bool e, bool s, bool w) =>
        tiles[set].TryGetValue(new Vector2Int(ColFor(w, e), RowFor(n, s)), out var t) ? t : null;

    static void BuildTilemaps()
    {
        var grid = new GameObject("Grid").AddComponent<Grid>();
        var water = MakeTilemap(grid, "Water", -20);
        var foam = MakeTilemap(grid, "Water Foam", -19);

        var waterTile = AssetDatabase.LoadAssetAtPath<TileBase>(TilesFolder + "/Water Background color.asset");
        var foamTile = AssetDatabase.LoadAssetAtPath<TileBase>(TilesFolder + "/Water Tile animated.asset");

        var waterBounds = new BoundsInt(-WaterPadding, -WaterPadding, 0, W + WaterPadding * 2, H + WaterPadding * 2, 1);
        var waterTiles = new TileBase[waterBounds.size.x * waterBounds.size.y];
        for (int i = 0; i < waterTiles.Length; i++) waterTiles[i] = waterTile;
        water.SetTilesBlock(waterBounds, waterTiles);

        var foamCells = new List<Vector3Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (land[x, y] && waterDist[x, y] == 1)
                foamCells.Add(new Vector3Int(x, y, 0));
        foam.SetTiles(foamCells.ToArray(), Enumerable.Repeat(foamTile, foamCells.Count).ToArray());

        // Taban katman: tüm kara, ilk biyomun tile'larıyla, kıyıya göre
        groundBase = MakeTilemap(grid, "Ground - " + Biomes[0].name, -18);
        FillLayer(groundBase, Biomes[0].tileset, (x, y) => Land(x, y));

        // Diğer biyomlar ayrı katmanlarda tabanın üstüne biner; kenarlarındaki yuvarlak
        // çerçeve sayesinde biyom sınırı alttaki zeminin üzerinde doğal bir geçiş gibi görünür.
        for (int b = 1; b < Biomes.Length; b++)
        {
            int id = b;
            var layer = MakeTilemap(grid, "Ground - " + Biomes[b].name, -18 + b);
            FillLayer(layer, Biomes[b].tileset, (x, y) => Land(x, y) && biome[x, y] == id);
        }
    }

    static void FillLayer(Tilemap map, int set, System.Func<int, int, bool> has)
    {
        var cells = new List<Vector3Int>();
        var layerTiles = new List<TileBase>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!has(x, y)) continue;
            cells.Add(new Vector3Int(x, y, 0));
            layerTiles.Add(GroundTile(set, has(x, y + 1), has(x + 1, y), has(x, y - 1), has(x - 1, y)));
        }
        map.SetTiles(cells.ToArray(), layerTiles.ToArray());
    }

    static Tilemap MakeTilemap(Grid grid, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(grid.transform, false);
        var tm = go.AddComponent<Tilemap>();
        go.AddComponent<TilemapRenderer>().sortingOrder = order;
        return tm;
    }

    // ---------------------------------------------------------------- Doğa

    // ---------------------------------------------------------------- Birimler

    const string PawnFolder = TS + "Pawn and Resources/Pawn/Blue Pawn/";

    /// <summary>Harita ortasına en yakın açık kara alanına bir mavi Pawn koyar ve çevresini boş bırakır.</summary>
    static GameObject PlacePawn()
    {
        var spot = new Vector2Int(W / 2, H / 2);
        for (int r = 0; r < Mathf.Max(W, H); r++)
        {
            bool found = false;
            for (int dx = -r; dx <= r && !found; dx++)
            for (int dy = -r; dy <= r && !found; dy++)
            {
                int x = W / 2 + dx, y = H / 2 + dy;
                if (Land(x, y) && waterDist[x, y] >= 8) { spot = new Vector2Int(x, y); found = true; }
            }
            if (found) break;
        }

        // Etrafına ağaç/çalı gelmesin
        const int clear = 4;
        for (int x = spot.x - clear; x <= spot.x + clear; x++)
        for (int y = spot.y - clear; y <= spot.y + clear; y++)
            if (In(x, y)) occupied[x, y] = true;

        var root = new GameObject("Units").transform;
        return CreatePawn(root, new Vector2(spot.x + 0.5f, spot.y + 0.5f));
    }

    static GameObject CreatePawn(Transform parent, Vector2 feet)
    {
        var idle = Frames(PawnFolder + "Pawn_Idle.png");
        var run = Frames(PawnFolder + "Pawn_Run.png");

        var go = new GameObject("Pawn");
        go.transform.SetParent(parent);
        go.transform.position = feet;

        // Pawn karesi 192x192 (3x3 birim); ayaklar karenin altından ~1 birim yukarıda
        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        var sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = idle.Length > 0 ? idle[0] : null;
        sr.sortingOrder = MapSorting.Order(feet.x, feet.y);

        var col = go.AddComponent<BoxCollider2D>();
        col.offset = new Vector2(0f, 0.5f);
        col.size = new Vector2(0.7f, 1f);

        var unit = go.AddComponent<Unit>();
        unit.body = sr;
        unit.idleFrames = idle;
        unit.runFrames = run;
        return go;
    }

    static void PlaceForests(Transform parent)
    {
        var root = new GameObject("Forests").transform;
        root.SetParent(parent);
        var off = RandomOffset();

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!land[x, y]) continue;
            float threshold = Param(x, y, b => b.forestThreshold);
            float n = Fbm(x / 26f, y / 26f, off, 3);
            if (n > threshold && rng.NextDouble() < Mathf.InverseLerp(threshold, threshold + 0.1f, n) * 0.8 + 0.2)
                PlaceTree(root, x, y);
        }
    }

    static void PlaceResources(Transform parent)
    {
        var gold = new GameObject("Gold Mines").transform; gold.SetParent(parent);
        var sheepRoot = new GameObject("Sheep").transform; sheepRoot.SetParent(parent);
        var sheep = TS + "Pawn and Resources/Meat/Sheep/Sheep_Idle.png";

        // Her biyomun kendi alanına göre kaynak sayısı
        var cellsPerBiome = new List<Vector2Int>[Biomes.Length];
        for (int b = 0; b < Biomes.Length; b++) cellsPerBiome[b] = new List<Vector2Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (land[x, y]) cellsPerBiome[biome[x, y]].Add(new Vector2Int(x, y));

        for (int b = 0; b < Biomes.Length; b++)
        {
            var cells = cellsPerBiome[b];
            if (cells.Count == 0) continue;

            int goldCount = Mathf.RoundToInt(cells.Count / 10000f * Biomes[b].goldPer10k);
            for (int i = 0, placed = 0; i < 500 && placed < goldCount; i++)
            {
                var p = cells[rng.Next(cells.Count)];
                if (TryPlace(gold, "Gold Mine", GoldStone(), p.x, p.y, 2, 2, 0.4f, minWaterDist: 3)) placed++;
            }

            int herds = Mathf.RoundToInt(cells.Count / 10000f * Biomes[b].sheepPer10k);
            for (int h = 0; h < herds; h++)
            {
                var c = cells[rng.Next(cells.Count)];
                int count = rng.Next(2, 6);
                for (int i = 0, tries = 0; i < count && tries < 30; tries++)
                {
                    int x = c.x + rng.Next(-3, 4), y = c.y + rng.Next(-3, 4);
                    if (TryPlace(sheepRoot, "Sheep", sheep, x, y, 1, 1, 0.2f, animate: true)) i++;
                }
            }
        }
    }

    static string GoldStone() => TS + $"Pawn and Resources/Gold/Gold Stones/Gold Stone {rng.Next(1, 7)}.png";

    static void PlaceScatter(Transform parent)
    {
        var trees = new GameObject("Trees").transform; trees.SetParent(parent);
        var bushes = new GameObject("Bushes").transform; bushes.SetParent(parent);
        var rocks = new GameObject("Rocks").transform; rocks.SetParent(parent);

        // Gürültüyle öbekleşen tek ağaç / çalı / taş
        var bushOff = RandomOffset();
        var rockOff = RandomOffset();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!land[x, y] || occupied[x, y]) continue;
            float bushNoise = Mathf.InverseLerp(0.4f, 0.7f, Fbm(x / 14f, y / 14f, bushOff, 2));
            float rockNoise = Mathf.InverseLerp(0.4f, 0.7f, Fbm(x / 18f, y / 18f, rockOff, 2));
            float pTree = Param(x, y, b => b.loneTree);
            float pBush = Param(x, y, b => b.bush) * (0.4f + 1.2f * bushNoise);
            float pRock = Param(x, y, b => b.rock) * (0.4f + 1.2f * rockNoise);
            double r = rng.NextDouble();

            if (r < pTree)
                PlaceTree(trees, x, y);
            else if (r < pTree + pBush)
            {
                int k = rng.Next(1, 5);
                TryPlace(bushes, "Bush", TS + $"Terrain/Decorations/Bushes/Bush {k}.png", x, y, 1, 1, 0.3f,
                    animate: true, minWaterDist: 1);
            }
            else if (r < pTree + pBush + pRock)
            {
                TryPlace(rocks, "Rock", TS + $"Terrain/Decorations/Rocks/Rock{rng.Next(1, 5)}.png", x, y, 1, 1, 0.25f,
                    minWaterDist: 1);
            }
        }
    }

    static void PlaceTree(Transform parent, int x, int y)
    {
        // Yan yana bitişik ağaç yok: ağaç sprite'ı 3 hücre geniş, bitişik olunca birbirini tamamen kapatıyor
        if (!Land(x, y) || TreeAt(x - 1, y) || TreeAt(x + 1, y)) return;

        // Tree1/2 çam, Tree3/4 yapraklı ağaç
        bool pine = rng.NextDouble() < Param(x, y, b => b.pineRatio);
        int k = pine ? rng.Next(1, 3) : rng.Next(3, 5);
        if (TryPlace(parent, "Tree", TS + $"Pawn and Resources/Wood/Trees/Tree{k}.png", x, y, 1, 1, 0.55f,
                animate: true))
            treeAt[x, y] = true;
    }

    static bool TreeAt(int x, int y) => In(x, y) && treeAt[x, y];

    static void PlaceWaterRocks(Transform parent)
    {
        var root = new GameObject("Water Rocks").transform;
        root.SetParent(parent);
        float area = W * H / 10000f;
        int placed = 0;
        for (int attempt = 0; attempt < 20000 && placed < 20 * area; attempt++)
        {
            int x = rng.Next(0, W), y = rng.Next(0, H);
            if (land[x, y] || NearLand(x, y, 1) || !NearLand(x, y, 4) || occupied[x, y]) continue;
            int k = rng.Next(1, 5);
            var frames = Frames(TS + $"Terrain/Decorations/Rocks in the Water/Water Rocks_0{k}.png");
            if (frames.Length == 0) continue;
            Spawn(root, "Water Rock", frames, x + 0.5f, y + 0.25f, 0.3f);
            occupied[x, y] = true;
            placed++;
        }
    }

    static bool NearLand(int x, int y, int r)
    {
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
            if (Land(x + dx, y + dy)) return true;
        return false;
    }

    /// <summary>(x, y) = kaplanan alanın sol-alt hücresi.</summary>
    static bool TryPlace(Transform parent, string name, string spritePath, int x, int y, int w, int h, float inset,
        bool animate = false, int minWaterDist = 2)
    {
        if (!AreaFree(x, y, w, h, minWaterDist)) return false;
        var frames = Frames(spritePath);
        if (frames.Length == 0) return false;
        if (!animate) frames = new[] { frames[0] };

        for (int i = x; i < x + w; i++)
        for (int j = y; j < y + h; j++)
            occupied[i, j] = true;

        float baseX = x + w / 2f, baseY = y + 0.15f;
        if (w == 1 && h == 1)
        {
            // Küçük objeler hücre içinde biraz kaysın, ızgara gibi dizilmesinler
            baseX += (float)rng.NextDouble() * 0.6f - 0.3f;
            baseY += (float)rng.NextDouble() * 0.35f;
        }
        Spawn(parent, name, frames, baseX, baseY, inset);
        return true;
    }

    static bool AreaFree(int x, int y, int w, int h, int minWaterDist)
    {
        for (int i = x; i < x + w; i++)
        for (int j = y; j < y + h; j++)
            if (!In(i, j) || !land[i, j] || occupied[i, j] || waterDist[i, j] < minWaterDist)
                return false;
        return true;
    }

    /// <summary>baseX/baseY: objenin zemine değdiği nokta. inset: sprite altındaki boşluk (birim).</summary>
    /// <param name="frames">Birden fazla kare varsa SpriteSheetAnimator ile döngüde oynatılır.</param>
    static GameObject Spawn(Transform parent, string name, Sprite[] frames, float baseX, float baseY, float inset)
    {
        var sprite = frames[0];
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        float centerY = baseY + sprite.bounds.extents.y - sprite.bounds.center.y - inset;
        go.transform.position = new Vector3(baseX, centerY, 0);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = MapSorting.Order(baseX, baseY);

        if (frames.Length > 1)
            go.AddComponent<SpriteSheetAnimator>().frames = frames;
        return go;
    }


    static readonly Dictionary<string, Sprite[]> frameCache = new Dictionary<string, Sprite[]>();

    /// <summary>Sprite sheet'in tüm kareleri, soldan sağa / yukarıdan aşağı.</summary>
    static Sprite[] Frames(string path)
    {
        if (frameCache.TryGetValue(path, out var f) && f.All(s => s != null)) return f;
        f = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderByDescending(sp => sp.rect.y).ThenBy(sp => sp.rect.x).ToArray();
        if (f.Length == 0) Debug.LogWarning("Sprite bulunamadı: " + path);
        frameCache[path] = f;
        return f;
    }

    // ---------------------------------------------------------------- Sahne & kamera

    static UnityEngine.SceneManagement.Scene CreateScene()
    {
        AssetDatabase.DeleteAsset(ScenePath);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScene) != null && AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        return scene;
    }

    static void SetupCamera(Vector3 focus)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.orthographicSize = 10f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(71, 171, 169, 255);
        cam.transform.position = new Vector3(focus.x, focus.y, -10f);

        var ctrl = cam.GetComponent<RTSCameraController>();
        if (ctrl == null) ctrl = cam.gameObject.AddComponent<RTSCameraController>();
        ctrl.bounds = new Rect(-4, -4, W + 8, H + 8);
        ctrl.maxZoom = 50f;
    }

    static void AddSceneToBuild()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------------------------------------------------------------- Yardımcılar

    static readonly Vector2Int[] Dirs4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    static readonly Vector2Int[] Dirs8 =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    static bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    static bool Land(int x, int y) => In(x, y) && land[x, y];
}
