using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tiny Swords asset'leriyle rastgele doğa haritası üretir. Kurallar: "MAP OLUŞTURMA KURALLARI" dokümanı.
///  - Yükseklik yok; biyomların sadece zemin tile'ları kullanılır (duvar / merdiven yok).
///  - 7 biyom: color1-4 çimen, bataklık (color5), çöl (eski sürüm Tilemap_Flat kum), kayalık (eski sürüm Tilemap_Elevation).
///  - Deniz, göl, nehir ve küçük su birikintileri.
/// Menü: RTS > Harita Oluştur  →  Assets/Scenes/RTSMap.unity
/// </summary>
public static class RTSMapGenerator
{
    const string TS = "Assets/Tiny Swords/";
    const string TSOld = "Assets/Tiny Swords Old/";
    const string TilesFolder = TS + "Terrain/Tileset/Tilemap Settings";
    const string OldTilesFolder = TSOld + "Generated Tiles";
    const string ScenePath = "Assets/Scenes/RTSMap.unity";
    const string TemplateScene = "Assets/Scenes/SampleScene.unity";

    // Referans haritanın (1536x695) en-boy oranı: 576 / 261 ≈ 2.21.
    // MapScale haritayı (kıtaları) büyütür; kare boyutu değişmez. Değiştirirsen MapSorting.MapHeight'ı da güncelle.
    const int MapScale = 3;
    const int AreaScale = MapScale * MapScale; // adetler (gölet, maden, kafatası...) alanla birlikte artsın
    const int W = 576 * MapScale;
    const int H = MapSorting.MapHeight; // = 261 * MapScale; çizim sırası formülü harita yüksekliğine bağlı
    const float S = 288f / 256f; // gürültü ölçeği (orman, bataklık suyu vb. öbek boyutu)

    // Kara, deniz, göl, büyük nehirler ve biyom yerleşimi bu Azgaar haritasından alınır.
    // Dosya yoksa harita tamamen gürültüyle üretilir.
    const string ReferenceMapPath = "Assets/Map Source/World Map.map";
    const int ReferenceRiverCount = 20; // referanstaki 208 nehirden sadece en büyükleri

    // KURAL: haritanın bu kadarı kara (su olmayan toprak). Referans haritada deniz seviyesi bu orana
    // ulaşana kadar düşürülür (en sığ denizler karaya döner); gürültü modunda eşik buna göre seçilir.
    const float LandRatio = 0.40f;
    // Göletler ve bataklık suyu karadan biraz yer; kara bunu telafi edecek kadar fazla üretilir
    const float LandRatioWaterAllowance = 0.015f;
    const int RiverExtendWidth = 3; // deniz geri çekilince nehirlerin yeni kıyıya uzatılan kısmının genişliği

    // Takımdaki herkes aynı haritayı üretsin diye sabit. Haritayı değiştirmek için bu sayıyı değiştirip
    // commit'leyin; "Rastgele" menüsünün Console'a yazdığı seed'i buraya yazarak beğendiğiniz haritayı sabitleyebilirsiniz.
    const int MapSeed = 20261020;

    // ================================================================ AYARLANACAK DEĞERLER
    // Dokümanda "Unity'de incelendikten sonra belirlenecek" denen değerler. Hepsi hücre (1 hücre = 64 piksel) cinsinden.
    // Adetler (gölet, balkabağı, maden, kafatası, kemik) MapScale = 1 haritası içindir; kod alanla orantılı çoğaltır.

    // Nehirler: denizden kaynağa doğru başlangıç genişliği, her [min, max] blokta bir NarrowBy kadar daralır
    const int RiverCount = 4;
    const int RiverMouthWidth = 4;
    const int RiverMinWidth = 1;
    const int RiverNarrowEveryMin = 15;
    const int RiverNarrowEveryMax = 30;
    const int RiverNarrowBy = 1;
    const int RiverMinSourceDistance = 30; // kaynağın denizden en az uzaklığı

    // Göller ve küçük su birikintileri
    const int LakeCount = 4;
    const int LakeRadiusMin = 5;
    const int LakeRadiusMax = 10;
    const int PondCount = 35;
    const int PondRadiusMin = 1;
    const int PondRadiusMax = 2;

    // Bataklık içi su: gürültü bu eşiği geçen bataklık hücreleri su olur (düşük = daha çok su)
    const float SwampWaterThreshold = 0.60f;
    const float SwampWaterScale = 7f;

    // Balkabağı adetleri [min, max] (tüm harita)
    static readonly Vector2Int PumpkinsColor2 = new Vector2Int(8, 14);
    static readonly Vector2Int PumpkinsColor3 = new Vector2Int(12, 20);

    // Altın madeni (GoldMine_Destroyed) adedi [min, max]
    static readonly Vector2Int GoldMineCount = new Vector2Int(10, 16);

    // Kafatası adedi ve rastgele kemik adedi
    static readonly Vector2Int SkullCount = new Vector2Int(12, 20);
    const int MaxBonesAroundSkull = 3;
    const int RandomBoneCount = 30;

    // Ağaç kütüğü olasılığı (ağaç çıkabilen biyomlarda, hücre başına)
    const float StumpChance = 0.0025f;

    // Bataklıkta hareket hızı çarpanı
    const float SwampSpeed = 0.8f;

    // ================================================================ Biyomlar

    enum Ground { NewColor, OldSand, OldRock }

    class Biome
    {
        public string name;
        public Ground ground;
        public int color;              // Ground.NewColor ise Tilemap_colorN

        public int[] trees = { };      // TreeN numaraları
        public int[] bushes = { };     // Bush N numaraları
        public float forestThreshold = 2f; // orman gürültüsü bu değeri geçince orman (2 = orman yok)
        public float loneTree;         // orman dışında tek ağaç olasılığı (hücre başına)
        public float bush;             // çalı olasılığı
        public float stone;            // küçük taş olasılığı
        public float mushroom;         // mantar olasılığı
        public float goldStone;        // altın taşı olasılığı
        public bool goldStoneLarge;    // Gold Stone 5/6 sadece burada
        public bool goldMine;          // GoldMine_Destroyed çıkabilir mi
        public float speed = 1f;       // hareket hızı çarpanı
    }

    // Sıra önemli: 0 = tüm karanın altındaki taban katman, diğerleri sırayla üstüne biner
    static readonly Biome[] Biomes =
    {
        new Biome { name = "Çimen 1 (color1)", ground = Ground.NewColor, color = 1,
            trees = new[] { 1, 3 }, bushes = new[] { 2, 3 }, forestThreshold = 0.62f, loneTree = 0.012f,
            bush = 0.06f, stone = 0.008f, goldStone = 0.0012f, goldMine = true },

        new Biome { name = "Çimen 2 (color2)", ground = Ground.NewColor, color = 2,
            trees = new[] { 2, 3 }, bushes = new[] { 2, 4 }, forestThreshold = 0.58f, loneTree = 0.018f,
            bush = 0.08f, stone = 0.008f, mushroom = 0.006f, goldStone = 0.0015f, goldMine = true },

        // color2'den daha yoğun
        new Biome { name = "Çimen 3 (color3)", ground = Ground.NewColor, color = 3,
            trees = new[] { 1, 2, 3 }, bushes = new[] { 2, 3, 4 }, forestThreshold = 0.50f, loneTree = 0.035f,
            bush = 0.12f, stone = 0.012f, mushroom = 0.012f, goldStone = 0.0025f, goldMine = true },

        new Biome { name = "Çimen 4 (color4)", ground = Ground.NewColor, color = 4,
            trees = new[] { 3, 4 }, bushes = new[] { 1, 4 }, forestThreshold = 0.55f, loneTree = 0.025f,
            bush = 0.06f, stone = 0.010f, goldStone = 0.0015f, goldMine = true },

        // Çok sık ağaç ve su; altın yok; yavaşlatır
        new Biome { name = "Bataklık (color5)", ground = Ground.NewColor, color = 5,
            trees = new[] { 1, 2, 3, 4 }, bushes = new[] { 1, 2, 3, 4 }, forestThreshold = 0.42f, loneTree = 0.08f,
            bush = 0.08f, stone = 0.006f, speed = SwampSpeed },

        // Sadece taş; altın yok
        new Biome { name = "Çöl (Tilemap_Flat kum)", ground = Ground.OldSand,
            stone = 0.025f },

        // Ağaç / çalı yok; altın taşları bol, Gold Stone 5/6 sadece burada
        new Biome { name = "Kayalık (Tilemap_Elevation)", ground = Ground.OldRock,
            stone = 0.05f, goldStone = 0.02f, goldStoneLarge = true, goldMine = true },
    };

    const int Grass1 = 0, Grass2 = 1, Grass3 = 2, Grass4 = 3, Swamp = 4, Desert = 5, Rock = 6;

    // ================================================================

    static System.Random rng;
    static Tilemap groundBase;
    static bool[,] land, occupied, treeAt;
    static int[,] waterDist, biome;
    static Dictionary<Vector2Int, TileBase>[] colorTiles;
    static Dictionary<Vector2Int, TileBase> oldFlatTiles, oldElevationTiles;

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
            if (System.IO.File.Exists(ReferenceMapPath))
            {
                LoadReferenceMap();
                ComputeWaterDistance();
                // Göller ve nehirler referanstan geliyor; küçük su birikintileri kurallara göre eklenir
                CarveBlobs(PondCount * AreaScale, PondRadiusMin, PondRadiusMax, minShoreDistance: 4, RandomOffset(), wobble: 0.3f);
            }
            else
            {
                Debug.LogWarning($"Referans harita bulunamadı ({ReferenceMapPath}), harita gürültüyle üretiliyor.");
                BuildLand();
                CarveLakesAndPonds();
                ComputeWaterDistance();
                CarveRivers();
                ComputeWaterDistance();
                BuildBiomes();
            }
            BuildBiomeBlend(10);
            AddSwampWater();
            RemoveSmallRegions(true, 8);
            ComputeWaterDistance();

            EditorUtility.DisplayProgressBar("RTS Harita", "Tilemap dolduruluyor...", 0.3f);
            var scene = CreateScene();
            BuildTilemaps();
            CreateMapData();

            EditorUtility.DisplayProgressBar("RTS Harita", "Doğa yerleştiriliyor...", 0.6f);
            occupied = new bool[W, H];
            treeAt = new bool[W, H];
            var pawn = PlacePawn();
            var objects = new GameObject("Nature").transform;
            PlaceGoldMines(objects);
            PlacePumpkins(objects);
            PlaceSkullsAndBones(objects);
            PlaceForests(objects);
            PlaceScatter(objects);
            PlaceWaterRocks(objects);
            SetupCamera(pawn.transform.position);
            new GameObject("Selection Manager").AddComponent<SelectionManager>().walkableGround = groundBase;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild();
            Debug.Log($"RTS haritası oluşturuldu ({W}x{H}, kara %{LandFraction() * 100f:F1}, seed {seed}): {ScenePath}");
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

    /// <summary>Ayar değerlerindeki adetler MapScale = 1 haritası içindir; alanla orantılı büyütülür.</summary>
    static int RandomCount(Vector2Int range) => rng.Next(range.x, range.y + 1) * AreaScale;

    // ---------------------------------------------------------------- Kara ve deniz

    static void BuildLand()
    {
        land = new bool[W, H];
        var elevationOff = RandomOffset();
        var warpX = RandomOffset();
        var warpY = RandomOffset();
        var value = new float[W, H];

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

            value[x, y] = e - (1f - mask) * 0.6f;
        }

        // KURAL: kara oranı LandRatio olsun; eşik buna göre seçilir
        var sorted = new List<float>(W * H);
        foreach (float v in value) sorted.Add(v);
        sorted.Sort((a, b) => b.CompareTo(a));
        float threshold = sorted[Mathf.Clamp(Mathf.RoundToInt((LandRatio + LandRatioWaterAllowance) * W * H), 0, sorted.Count - 1)];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            land[x, y] = value[x, y] > threshold;

        // Harita kenarında her zaman deniz kalsın
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (x < 4 || y < 4 || x >= W - 4 || y >= H - 4)
                land[x, y] = false;

        for (int i = 0; i < 3; i++) Smooth(land);
        RemoveSmallRegions(true, 200);  // minik adacıklar
        RemoveSmallRegions(false, 120); // gürültüden kalan minik su birikintileri (göl/gölet ayrıca eklenir)
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

    // ---------------------------------------------------------------- Göl, gölet, nehir

    static void CarveLakesAndPonds()
    {
        ComputeWaterDistance();
        var off = RandomOffset();

        CarveBlobs(LakeCount, LakeRadiusMin, LakeRadiusMax, minShoreDistance: 8, off, wobble: 0.45f);
        CarveBlobs(PondCount * AreaScale, PondRadiusMin, PondRadiusMax, minShoreDistance: 4, off, wobble: 0.3f);
    }

    static void CarveBlobs(int count, int rMin, int rMax, int minShoreDistance, Vector2 noiseOff, float wobble)
    {
        int placed = 0;
        for (int attempt = 0; attempt < count * 50 && placed < count; attempt++)
        {
            int r = rng.Next(rMin, rMax + 1);
            var c = new Vector2Int(rng.Next(0, W), rng.Next(0, H));
            if (!land[c.x, c.y] || waterDist[c.x, c.y] < r + minShoreDistance) continue;

            for (int x = c.x - r - 2; x <= c.x + r + 2; x++)
            for (int y = c.y - r - 2; y <= c.y + r + 2; y++)
            {
                if (!In(x, y)) continue;
                float d = Vector2.Distance(new Vector2(x, y), c) / (r + 0.5f);
                float n = (Fbm(x / 6f, y / 6f, noiseOff, 2) - 0.5f) * 2f * wobble;
                if (d + n < 1f) land[x, y] = false;
            }
            placed++;
        }
    }

    /// <summary>
    /// Nehirler kara içindeki bir kaynaktan denize (ya da göle) doğru, denize olan uzaklığın azaldığı yönde kıvrılarak akar.
    /// Genişlik ağızda RiverMouthWidth'tir; kaynağa doğru her [min, max] blokta RiverNarrowBy kadar daralır.
    /// </summary>
    static void CarveRivers()
    {
        for (int i = 0; i < RiverCount; i++)
        {
            Vector2Int source = default;
            bool found = false;
            for (int attempt = 0; attempt < 400 && !found; attempt++)
            {
                source = new Vector2Int(rng.Next(0, W), rng.Next(0, H));
                found = land[source.x, source.y] && waterDist[source.x, source.y] >= RiverMinSourceDistance;
            }
            if (!found) continue;

            var path = TraceRiver(source);
            if (path.Count < 10) continue;

            // Ağızdan kaynağa doğru genişlik
            path.Reverse();
            int width = RiverMouthWidth;
            int nextNarrow = rng.Next(RiverNarrowEveryMin, RiverNarrowEveryMax + 1);
            for (int k = 0; k < path.Count; k++)
            {
                if (k >= nextNarrow)
                {
                    width = Mathf.Max(RiverMinWidth, width - RiverNarrowBy);
                    nextNarrow += rng.Next(RiverNarrowEveryMin, RiverNarrowEveryMax + 1);
                }
                CarveSquare(path[k], width);
            }
        }
    }

    static List<Vector2Int> TraceRiver(Vector2Int source)
    {
        var path = new List<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        var cur = source;
        Vector2 heading = Random2D();

        for (int step = 0; step < W * 4; step++)
        {
            path.Add(cur);
            visited.Add(cur);
            if (!land[cur.x, cur.y] || waterDist[cur.x, cur.y] == 0) break;

            // Denize uzaklığı azaltan komşular arasından, gidiş yönüne yakın olanı biraz rastgelelikle seç
            Vector2Int best = cur;
            float bestScore = float.MinValue;
            foreach (var d in Dirs8)
            {
                var n = cur + d;
                if (!In(n.x, n.y) || visited.Contains(n)) continue;
                int delta = waterDist[n.x, n.y] - waterDist[cur.x, cur.y];
                if (delta > 0) continue;
                float score = Vector2.Dot(((Vector2)d).normalized, heading) * 0.8f
                              + (float)rng.NextDouble() * 1.0f
                              - delta * 0.6f;
                if (score > bestScore) { bestScore = score; best = n; }
            }
            if (best == cur) break;

            // Çapraz adımda ara hücreyi de ekle ki nehir kopuk görünmesin
            if (best.x != cur.x && best.y != cur.y)
                path.Add(new Vector2Int(best.x, cur.y));

            heading = Vector2.Lerp(heading, (Vector2)(best - cur), 0.3f).normalized;
            cur = best;
        }
        return path;
    }

    static Vector2 Random2D()
    {
        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    static void CarveSquare(Vector2Int c, int width)
    {
        int lo = -(width - 1) / 2, hi = width / 2;
        for (int dx = lo; dx <= hi; dx++)
        for (int dy = lo; dy <= hi; dy++)
            if (In(c.x + dx, c.y + dy))
                land[c.x + dx, c.y + dy] = false;
    }

    // ---------------------------------------------------------------- Biyom haritası

    // Nem / sıcaklık / kayalık gürültüsünden biyom seçilir:
    //   kayalık gürültüsü çok yüksek → Kayalık
    //   kuru + sıcak → Çöl, kuru + serin → Çimen 1
    //   orta nem → sıcaksa Çimen 2, serinse Çimen 4
    //   nemli → sıcaksa Çimen 3, serinse Çimen 4
    //   çok nemli → Bataklık
    static void BuildBiomes()
    {
        biome = new int[W, H];
        var moistOff = RandomOffset();
        var tempOff = RandomOffset();
        var rockOff = RandomOffset();
        var warpOff = RandomOffset();
        var jitterOff = RandomOffset();

        var moist = new float[W, H];
        var temp = new float[W, H];
        var rocky = new float[W, H];
        var landM = new List<float>();
        var landT = new List<float>();
        var landR = new List<float>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            float wx = x + (Fbm(x / (40f * S), y / (40f * S), warpOff, 2) - 0.5f) * 40f * S;
            float wy = y + (Fbm(y / (40f * S), x / (40f * S), warpOff, 2) - 0.5f) * 40f * S;
            // Hafif titreşim: biyom sınırı cetvelle çizilmiş gibi durmasın
            float jm = (Fbm(x / 12f, y / 12f, jitterOff, 2) - 0.5f) * 0.025f;
            float jt = (Fbm(y / 12f, x / 12f, jitterOff, 2) - 0.5f) * 0.025f;
            moist[x, y] = Fbm(wx / (75f * S), wy / (75f * S), moistOff, 3) + jm;
            temp[x, y] = Fbm(wx / (95f * S), wy / (95f * S), tempOff, 3) + jt;
            rocky[x, y] = Fbm(wx / (45f * S), wy / (45f * S), rockOff, 3);
            if (land[x, y]) { landM.Add(moist[x, y]); landT.Add(temp[x, y]); landR.Add(rocky[x, y]); }
        }

        // Yüzdelik eşikler: her haritada tüm biyomların makul payı olsun
        landM.Sort(); landT.Sort(); landR.Sort();
        float mDry = Percentile(landM, 0.22f), mMid = Percentile(landM, 0.50f), mWet = Percentile(landM, 0.84f);
        float tMid = Percentile(landT, 0.5f);
        float rRock = Percentile(landR, 0.91f);

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            float m = moist[x, y], t = temp[x, y];
            int b;
            if (rocky[x, y] > rRock && m < mWet) b = Rock;
            else if (m < mDry) b = t > tMid ? Desert : Grass1;
            else if (m < mMid) b = t > tMid ? Grass2 : Grass4;
            else if (m < mWet) b = t > tMid ? Grass3 : Grass4;
            else b = Swamp;
            biome[x, y] = b;
        }

        // Sınırları yumuşat, küçük biyom parçalarını komşusuna kat: her biyom geniş bir bölge olsun
        for (int i = 0; i < 3; i++) SmoothBiomes();
        for (int pass = 0; pass < 2; pass++)
            foreach (var comp in Components((x, y) => land[x, y], biome))
            {
                if (comp.Count >= 300) continue;
                int replacement = MostCommonNeighbourBiome(comp);
                foreach (var p in comp) biome[p.x, p.y] = replacement;
            }
    }

    // ---------------------------------------------------------------- Referans harita (Azgaar)

    static void LoadReferenceMap()
    {
        var reference = AzgaarMapReader.Load(ReferenceMapPath, W, H, ReferenceRiverCount);
        land = reference.Land; // aynı dizi: aşağıdaki değişiklikler reference.FillMissingBiomes'a da yansır
        ClearBorder();

        LowerSeaLevel(reference, LandRatio + LandRatioWaterAllowance);
        ExtendRiversToSea(reference);
        ClearBorder();

        RemoveSmallRegions(true, 25 * AreaScale); // çevirmeden kalan minik kara kırıntıları
        RemoveSmallRegions(false, 3 * MapScale); // ve minik su delikleri

        // Yeni kazanılan kara en yakın biyomu alır
        reference.FillMissingBiomes();
        biome = new int[W, H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            biome[x, y] = FromAzgaar(reference.Biome[x, y]);

        // Referanstaki biyom sınırları hücre hücre (köşeli); yuvarlat ve küçük parçaları komşusuna kat.
        // Çöl referansta sadece birkaç küçük leke; bu temizlik onu tamamen siliyordu, o yüzden korunuyor.
        for (int i = 0; i < 1 + MapScale; i++) SmoothBiomes(protect: Desert);
        foreach (var comp in Components((x, y) => land[x, y], biome))
        {
            if (comp.Count >= 60 * MapScale || biome[comp[0].x, comp[0].y] == Desert) continue;
            int replacement = MostCommonNeighbourBiome(comp);
            foreach (var p in comp) biome[p.x, p.y] = replacement;
        }
    }

    // Harita kenarında her zaman deniz kalsın
    static void ClearBorder()
    {
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (x < 2 || y < 2 || x >= W - 2 || y >= H - 2)
                land[x, y] = false;
    }

    static float LandFraction()
    {
        int count = 0;
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (land[x, y]) count++;
        return count / (float)(W * H);
    }

    /// <summary>
    /// Deniz seviyesini, kara oranı hedefe ulaşana kadar düşürür: denizdeki hücreler referansın yüksekliğine
    /// (sığ deniz = yüksek) ve biraz gürültüye göre sıralanır, en sığdan başlayarak karaya çevrilir.
    /// Göl ve nehirler su kalır.
    /// </summary>
    static void LowerSeaLevel(AzgaarMapReader reference, float targetRatio)
    {
        int need = Mathf.RoundToInt(targetRatio * W * H) - Mathf.RoundToInt(LandFraction() * W * H);
        if (need <= 0) return;

        var broad = RandomOffset();
        var fine = RandomOffset();
        var score = new float[W, H];
        var candidates = new List<float>();
        for (int x = 2; x < W - 2; x++)
        for (int y = 2; y < H - 2; y++)
        {
            if (land[x, y] || reference.Lake[x, y] || reference.River[x, y]) continue;
            // Yükseklik kaba ızgaradan geldiği için yeni kıyılar düz olurdu; gürültüyle girintili çıkıntılı yap
            float s = reference.Height[x, y]
                      + (Fbm(x / (30f * MapScale), y / (30f * MapScale), broad, 3) - 0.5f) * 10f
                      + (Fbm(x / 9f, y / 9f, fine, 2) - 0.5f) * 4f;
            score[x, y] = s;
            candidates.Add(s);
        }
        if (candidates.Count == 0) return;

        candidates.Sort((a, b) => b.CompareTo(a));
        float threshold = candidates[Mathf.Clamp(need - 1, 0, candidates.Count - 1)];

        for (int x = 2; x < W - 2; x++)
        for (int y = 2; y < H - 2; y++)
            if (!land[x, y] && !reference.Lake[x, y] && !reference.River[x, y] && score[x, y] >= threshold)
                land[x, y] = true;

        // Yeni kıyıları yumuşat; göl ve nehirler yine su kalsın
        for (int i = 0; i < 2; i++) Smooth(land);
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (reference.Lake[x, y] || reference.River[x, y]) land[x, y] = false;
    }

    /// <summary>
    /// Deniz geri çekilince nehir ağızları karanın içinde kalır. Her ağızdan açık denize (harita kenarına
    /// bağlı su) olan uzaklığın azaldığı yönde ilerleyip nehri yeni kıyıya kadar uzatır.
    /// </summary>
    static void ExtendRiversToSea(AzgaarMapReader reference)
    {
        // Açık deniz: harita kenarından başlayan su bölgesi
        var openSea = new bool[W, H];
        var q = new Queue<Vector2Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if ((x == 0 || y == 0 || x == W - 1 || y == H - 1) && !land[x, y])
            {
                openSea[x, y] = true;
                q.Enqueue(new Vector2Int(x, y));
            }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Dirs4)
            {
                var n = p + d;
                if (In(n.x, n.y) && !openSea[n.x, n.y] && !land[n.x, n.y])
                {
                    openSea[n.x, n.y] = true;
                    q.Enqueue(n);
                }
            }
        }

        // Her hücrenin açık denize uzaklığı
        var seaDist = new int[W, H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            seaDist[x, y] = openSea[x, y] ? 0 : int.MaxValue;
            if (openSea[x, y]) q.Enqueue(new Vector2Int(x, y));
        }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Dirs8)
            {
                var n = p + d;
                if (In(n.x, n.y) && seaDist[n.x, n.y] > seaDist[p.x, p.y] + 1)
                {
                    seaDist[n.x, n.y] = seaDist[p.x, p.y] + 1;
                    q.Enqueue(n);
                }
            }
        }

        foreach (var m in reference.RiverMouths)
        {
            var cur = new Vector2Int(m[0], m[1]);
            for (int step = 0; step < W && In(cur.x, cur.y) && !openSea[cur.x, cur.y]; step++)
            {
                // Açık denize yaklaştıran komşulardan birini, biraz kıvrılsın diye rastgele seç
                Vector2Int best = cur;
                float bestScore = float.MaxValue;
                foreach (var d in Dirs8)
                {
                    var n = cur + d;
                    if (!In(n.x, n.y) || seaDist[n.x, n.y] >= seaDist[cur.x, cur.y]) continue;
                    float s = seaDist[n.x, n.y] + (float)rng.NextDouble() * 0.9f;
                    if (s < bestScore) { bestScore = s; best = n; }
                }
                if (best == cur) break;

                // Çapraz adımda ara hücreyi de aç ki nehir kopuk görünmesin
                if (best.x != cur.x && best.y != cur.y) CarveSquare(new Vector2Int(best.x, cur.y), RiverExtendWidth);
                CarveSquare(best, RiverExtendWidth);
                cur = best;
            }
        }
    }

    /// <summary>Azgaar'ın 13 biyomunu bizim 7 biyoma eşler.</summary>
    static int FromAzgaar(int azgaar)
    {
        switch (azgaar)
        {
            case AzgaarMapReader.HotDesert:
            case AzgaarMapReader.ColdDesert: return Desert;
            case AzgaarMapReader.Savanna:
            case AzgaarMapReader.TropicalSeasonalForest: return Grass1;
            case AzgaarMapReader.Grassland:
            case AzgaarMapReader.TropicalRainforest: return Grass2;
            case AzgaarMapReader.TemperateDeciduousForest:
            case AzgaarMapReader.TemperateRainforest: return Grass3;
            case AzgaarMapReader.Taiga: return Grass4;
            case AzgaarMapReader.Tundra:
            case AzgaarMapReader.Glacier: return Rock;   // karlı asset yok
            case AzgaarMapReader.Wetland: return Swamp;
            default: return Grass2;
        }
    }

    static float Percentile(List<float> sorted, float p) =>
        sorted.Count == 0 ? 0.5f : sorted[Mathf.Clamp((int)(sorted.Count * p), 0, sorted.Count - 1)];

    /// <param name="protect">Bu biyomun hücreleri, çevrelerinde az da olsa aynı biyom varsa kendini korur (küçük lekeler silinmesin).</param>
    static void SmoothBiomes(int protect = -1)
    {
        var next = (int[,])biome.Clone();
        var counts = new int[Biomes.Length];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            System.Array.Clear(counts, 0, counts.Length);
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
                // Sadece kara sayılır; denizin biyom değeri anlamsız ve kıyıyı tek biyoma boyardı
                if (Land(x + dx, y + dy)) counts[biome[x + dx, y + dy]]++;
            int best = biome[x, y];
            if (best == protect && counts[protect] >= 6) continue;
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

    // Bataklıkta sık sık küçük su birikintileri
    static void AddSwampWater()
    {
        var off = RandomOffset();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!land[x, y] || biome[x, y] != Swamp) continue;
            // Bataklığın kenarına su koyma, biyom geçişi bozulmasın
            if (SwampDepth(x, y) < 3) continue;
            if (Fbm(x / SwampWaterScale, y / SwampWaterScale, off, 2) > SwampWaterThreshold)
                land[x, y] = false;
        }
    }

    static int SwampDepth(int x, int y)
    {
        for (int r = 1; r <= 3; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (!In(x + dx, y + dy) || biome[x + dx, y + dy] != Swamp) return r - 1;
        return 3;
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

    static Biome BiomeAt(int x, int y) => Biomes[biome[x, y]];

    // ---------------------------------------------------------------- Tilemap

    static bool LoadTiles()
    {
        colorTiles = new Dictionary<Vector2Int, TileBase>[6];
        for (int i = 1; i <= 5; i++) colorTiles[i] = new Dictionary<Vector2Int, TileBase>();

        foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { TilesFolder + "/Sliced Tiles" }))
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
            if (tile == null || tile.sprite == null) continue;
            string tex = tile.sprite.texture.name;
            if (!tex.StartsWith("Tilemap_color") || !int.TryParse(tex.Substring(13), out int set) || set < 1 || set > 5)
                continue;
            var r = tile.sprite.rect;
            // Anahtar: (sütun, üstten satır)
            colorTiles[set][new Vector2Int((int)r.x / 64, 5 - (int)r.y / 64)] = tile;
        }

        for (int i = 1; i <= 5; i++)
            if (colorTiles[i].Count < 16)
            {
                Debug.LogError($"Tilemap_color{i} tile'ları eksik: {TilesFolder}/Sliced Tiles");
                return false;
            }

        // Eski sürümün kum ve taş zeminleri: gerekiyorsa dilimlenir ve Tile asset'leri üretilir
        oldFlatTiles = TilesetSlicer.EnsureTiles(TSOld + "Terrain/Ground/Tilemap_Flat.png", OldTilesFolder + "/Tilemap_Flat");
        oldElevationTiles = TilesetSlicer.EnsureTiles(TSOld + "Terrain/Ground/Tilemap_Elevation.png", OldTilesFolder + "/Tilemap_Elevation");
        if (oldFlatTiles.Count < 40 || oldElevationTiles.Count < 32)
        {
            Debug.LogError("Eski Tiny Swords zemin tile'ları üretilemedi: " + TSOld + "Terrain/Ground");
            return false;
        }
        return true;
    }

    // Tiny Swords düz zemin 4x4 bloğu: 3x3 köşe/kenar + dikey şerit + yatay şerit + tek parça
    static int ColFor(bool w, bool e) => w && e ? 1 : !w && e ? 0 : w ? 2 : 3;
    static int RowFor(bool n, bool s) => n && s ? 1 : !n && s ? 0 : n ? 2 : 3;

    static TileBase GroundTile(Biome b, bool n, bool e, bool s, bool w)
    {
        int col = ColFor(w, e), row = RowFor(n, s);
        Dictionary<Vector2Int, TileBase> set;
        switch (b.ground)
        {
            // Tilemap_Flat: solda çimen, sağda (5. sütundan itibaren) kum
            case Ground.OldSand: set = oldFlatTiles; col += 5; break;
            // Tilemap_Elevation: 3. ve 5. satırlar uçurum duvarı; yatay şerit 4. satırda
            case Ground.OldRock: set = oldElevationTiles; if (row == 3) row = 4; break;
            default: set = colorTiles[b.color]; break;
        }
        return set.TryGetValue(new Vector2Int(col, row), out var t) ? t : null;
    }

    static void BuildTilemaps()
    {
        // Su için ayrı bir tilemap yok: düz renkli su tile'ı kameranın arka plan rengiyle aynı.
        // Büyük haritada milyonlarca su tile'ı sahne dosyasını yüzlerce MB yapıyordu.
        var grid = new GameObject("Grid").AddComponent<Grid>();
        var foam = MakeTilemap(grid, "Water Foam", -19);
        var foamTile = AssetDatabase.LoadAssetAtPath<TileBase>(TilesFolder + "/Water Tile animated.asset");

        var foamCells = new List<Vector3Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (land[x, y] && waterDist[x, y] == 1)
                foamCells.Add(new Vector3Int(x, y, 0));
        foam.SetTiles(foamCells.ToArray(), Enumerable.Repeat(foamTile, foamCells.Count).ToArray());

        // Taban katman: tüm kara, ilk biyomun tile'larıyla, kıyıya göre
        groundBase = MakeTilemap(grid, "Ground - " + Biomes[0].name, -18);
        FillLayer(groundBase, Biomes[0], (x, y) => Land(x, y));

        // Diğer biyomlar ayrı katmanlarda tabanın üstüne biner; kenarlarındaki yuvarlak
        // çerçeve sayesinde biyom sınırı alttaki zeminin üzerinde doğal bir geçiş gibi görünür.
        for (int b = 1; b < Biomes.Length; b++)
        {
            int id = b;
            var layer = MakeTilemap(grid, "Ground - " + Biomes[b].name, -18 + b);
            FillLayer(layer, Biomes[b], (x, y) => Land(x, y) && biome[x, y] == id);
        }
    }

    static void FillLayer(Tilemap map, Biome b, System.Func<int, int, bool> has)
    {
        var cells = new List<Vector3Int>();
        var layerTiles = new List<TileBase>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!has(x, y)) continue;
            cells.Add(new Vector3Int(x, y, 0));
            layerTiles.Add(GroundTile(b, has(x, y + 1), has(x + 1, y), has(x, y - 1), has(x - 1, y)));
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

    static void CreateMapData()
    {
        var data = new GameObject("Map Data").AddComponent<MapData>();
        data.width = W;
        data.height = H;
        data.biomeNames = Biomes.Select(b => b.name).ToArray();
        data.biomeSpeed = Biomes.Select(b => b.speed).ToArray();
        data.cells = new byte[W * H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            data.cells[x + y * W] = land[x, y] ? (byte)biome[x, y] : MapData.Water;
    }

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
                if (Land(x, y) && waterDist[x, y] >= 8 && biome[x, y] != Swamp)
                {
                    spot = new Vector2Int(x, y);
                    found = true;
                }
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

    // ---------------------------------------------------------------- Kaynaklar ve dekor

    static string Tree(int n) => TS + $"Pawn and Resources/Wood/Trees/Tree{n}.png";
    static string Stump(int n) => TS + $"Pawn and Resources/Wood/Trees/Stump {n}.png";
    static string Bush(int n) => TS + $"Terrain/Decorations/Bushes/Bush {n}.png";
    static string Stone(int n) => TS + $"Terrain/Decorations/Rocks/Rock{n}.png";
    static string GoldStone(int n) => TS + $"Pawn and Resources/Gold/Gold Stones/Gold Stone {n}_Highlight.png";
    static string WaterRock(int n) => TS + $"Terrain/Decorations/Rocks in the Water/Water Rocks_0{n}.png";
    static string Deco(int n) => TSOld + $"Deco/{n:D2}.png";
    const string GoldMinePath = TSOld + "Resources/Gold Mine/GoldMine_Destroyed.png";

    // Eski sürüm Deco numaraları
    static readonly int[] Mushrooms = { 1, 2, 3 };
    static readonly int[] Pumpkins = { 12, 13 };
    const int Bone1 = 14, Bone2 = 15, Skull = 16;

    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent);
        return t;
    }

    static T Pick<T>(T[] items) => items[rng.Next(items.Length)];

    static List<Vector2Int> LandCells(System.Func<int, bool> biomeFilter)
    {
        var cells = new List<Vector2Int>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            if (land[x, y] && biomeFilter(biome[x, y])) cells.Add(new Vector2Int(x, y));
        return cells;
    }

    // Altın madeni: bataklık, çöl ve su hariç; seyrek
    static void PlaceGoldMines(Transform parent)
    {
        var root = Group(parent, "Gold Mines");
        var cells = LandCells(b => Biomes[b].goldMine);
        if (cells.Count == 0) return;

        int target = RandomCount(GoldMineCount);
        var placedAt = new List<Vector2Int>();
        for (int i = 0; i < 3000 && placedAt.Count < target; i++)
        {
            var p = cells[rng.Next(cells.Count)];
            // Birbirlerinden uzak olsunlar
            if (placedAt.Any(q => Vector2Int.Distance(p, q) < 30)) continue;
            // 192x128 → 3x2 hücre; tüm hücreler izinli biyomda olmalı
            if (!AreaInBiomes(p.x, p.y, 3, 2, b => Biomes[b].goldMine)) continue;
            if (TryPlace(root, "Gold Mine", GoldMinePath, p.x, p.y, 3, 2, 0.25f, minWaterDist: 3))
                placedAt.Add(p);
        }
    }

    static bool AreaInBiomes(int x, int y, int w, int h, System.Func<int, bool> ok)
    {
        for (int i = x; i < x + w; i++)
        for (int j = y; j < y + h; j++)
            if (!In(i, j) || !ok(biome[i, j])) return false;
        return true;
    }

    // Balkabağı: color2 ve color3'te, adetleri [min, max] aralığında
    static void PlacePumpkins(Transform parent)
    {
        var root = Group(parent, "Pumpkins");
        PlaceCount(root, "Pumpkin", () => Deco(Pick(Pumpkins)), LandCells(b => b == Grass2), RandomCount(PumpkinsColor2));
        PlaceCount(root, "Pumpkin", () => Deco(Pick(Pumpkins)), LandCells(b => b == Grass3), RandomCount(PumpkinsColor3));
    }

    static int PlaceCount(Transform root, string name, System.Func<string> sprite, List<Vector2Int> cells, int count)
    {
        int placed = 0;
        for (int i = 0; i < count * 40 && placed < count && cells.Count > 0; i++)
        {
            var p = cells[rng.Next(cells.Count)];
            if (TryPlace(root, name, sprite(), p.x, p.y, 1, 1, 0.3f)) placed++;
        }
        return placed;
    }

    // Kafatası + etrafında (bir blok ötesinde) en fazla 3 kemik; ayrıca rastgele kemikler
    static void PlaceSkullsAndBones(Transform parent)
    {
        var root = Group(parent, "Skulls and Bones");
        // Kayalığın üstüne dekor konmuyor
        var cells = LandCells(b => b != Rock);

        int skulls = RandomCount(SkullCount);
        for (int i = 0, placed = 0; i < skulls * 40 && placed < skulls; i++)
        {
            var p = cells[rng.Next(cells.Count)];
            if (!TryPlace(root, "Skull", Deco(Skull), p.x, p.y, 1, 1, 0.3f)) continue;
            placed++;

            int bones = rng.Next(0, MaxBonesAroundSkull + 1);
            var around = Dirs8.OrderBy(_ => rng.Next()).ToList();
            foreach (var d in around)
            {
                if (bones == 0) break;
                var b = p + d;
                if (TryPlace(root, "Bone", Deco(rng.Next(2) == 0 ? Bone1 : Bone2), b.x, b.y, 1, 1, 0.3f))
                    bones--;
            }
        }

        PlaceCount(root, "Bone", () => Deco(rng.Next(2) == 0 ? Bone1 : Bone2), cells, RandomBoneCount * AreaScale);
    }

    static void PlaceForests(Transform parent)
    {
        var root = Group(parent, "Forests");
        var off = RandomOffset();

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!land[x, y] || BiomeAt(x, y).trees.Length == 0) continue;
            float threshold = Param(x, y, b => b.forestThreshold);
            float n = Fbm(x / 26f, y / 26f, off, 3);
            if (n > threshold && rng.NextDouble() < Mathf.InverseLerp(threshold, threshold + 0.1f, n) * 0.8 + 0.2)
                PlaceTree(root, x, y);
        }
    }

    static void PlaceScatter(Transform parent)
    {
        var trees = Group(parent, "Trees");
        var stumps = Group(parent, "Stumps");
        var bushes = Group(parent, "Bushes");
        var stones = Group(parent, "Stones");
        var mushrooms = Group(parent, "Mushrooms");
        var gold = Group(parent, "Gold Stones");

        var bushOff = RandomOffset();
        var stoneOff = RandomOffset();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (!land[x, y] || occupied[x, y]) continue;
            var b = BiomeAt(x, y);

            float bushNoise = Mathf.InverseLerp(0.4f, 0.7f, Fbm(x / 14f, y / 14f, bushOff, 2));
            float stoneNoise = Mathf.InverseLerp(0.4f, 0.7f, Fbm(x / 18f, y / 18f, stoneOff, 2));

            // Ağaç/çalı/mantar/altın kuralları biyoma özel; yoğunluk sınırda yumuşatılıyor ama
            // yasak olan biyomda (ör. kayalıkta ağaç) hiç çıkmıyor
            float pTree = b.trees.Length > 0 ? Param(x, y, q => q.loneTree) : 0f;
            float pStump = b.trees.Length > 0 ? StumpChance : 0f;
            float pBush = b.bushes.Length > 0 ? Param(x, y, q => q.bush) * (0.4f + 1.2f * bushNoise) : 0f;
            float pMushroom = b.mushroom;
            float pGold = b.goldStone;
            float pStone = Param(x, y, q => q.stone) * (0.4f + 1.2f * stoneNoise);

            double r = rng.NextDouble();
            if ((r -= pTree) < 0)
                PlaceTree(trees, x, y);
            else if ((r -= pStump) < 0)
                // Kütük, o biyomda çıkabilen ağaçlardan birine ait olur (tree1 → Stump 1, ...)
                TryPlace(stumps, "Stump", Stump(Pick(b.trees)), x, y, 1, 1, 0.3f);
            else if ((r -= pBush) < 0)
                TryPlace(bushes, "Bush", Bush(Pick(b.bushes)), x, y, 1, 1, 0.3f, animate: true, minWaterDist: 1);
            else if ((r -= pMushroom) < 0)
                TryPlace(mushrooms, "Mushroom", Deco(Pick(Mushrooms)), x, y, 1, 1, 0.3f, minWaterDist: 1);
            else if ((r -= pGold) < 0)
            {
                // Gold Stone 5/6 sadece kayalıkta
                int n = b.goldStoneLarge ? rng.Next(1, 7) : rng.Next(1, 5);
                TryPlace(gold, "Gold Stone", GoldStone(n), x, y, 1, 1, 0.3f, animate: true);
            }
            else if ((r -= pStone) < 0)
                TryPlace(stones, "Stone", Stone(rng.Next(1, 5)), x, y, 1, 1, 0.25f, minWaterDist: 1);
        }
    }

    static void PlaceTree(Transform parent, int x, int y)
    {
        // Yan yana bitişik ağaç yok: ağaç sprite'ı 3 hücre geniş, bitişik olunca birbirini tamamen kapatıyor
        if (!Land(x, y) || TreeAt(x - 1, y) || TreeAt(x + 1, y)) return;

        var trees = BiomeAt(x, y).trees;
        if (trees.Length == 0) return;
        if (TryPlace(parent, "Tree", Tree(Pick(trees)), x, y, 1, 1, 0.55f, animate: true))
            treeAt[x, y] = true;
    }

    static bool TreeAt(int x, int y) => In(x, y) && treeAt[x, y];

    // Su içindeki taşlar: deniz, göl ve nehirlerde
    static void PlaceWaterRocks(Transform parent)
    {
        var root = Group(parent, "Water Rocks");
        float area = W * H / 10000f;
        int placed = 0;
        for (int attempt = 0; attempt < 30000 * MapScale && placed < 7 * area / MapScale; attempt++)
        {
            int x = rng.Next(0, W), y = rng.Next(0, H);
            if (land[x, y] || NearLand(x, y, 1) || !NearLand(x, y, 4) || occupied[x, y]) continue;
            var frames = Frames(WaterRock(rng.Next(1, 5)));
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
        if (path.StartsWith(TSOld)) EnsureOldSpriteImport(path);
        f = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderByDescending(sp => sp.rect.y).ThenBy(sp => sp.rect.x).ToArray();
        if (f.Length == 0) Debug.LogWarning("Sprite bulunamadı: " + path);
        frameCache[path] = f;
        return f;
    }

    /// <summary>Eski sürüm görselleri Unity'nin varsayılan ayarlarıyla içe aktarıldıysa düzeltir (64 PPU, keskin piksel).</summary>
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
        ctrl.maxZoom = 80f;
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
