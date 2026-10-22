using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Azgaar Fantasy Map Generator (.map) kayıt dosyasını okuyup istenen boyutta bir ızgaraya çevirir:
/// hangi hücre kara, hangi hücre hangi Azgaar biyomu.
///
/// Dosyadaki SVG katmanları kullanılır:
///   - featurePaths + "land" maskesi: kara parçaları (beyaz) ve göller (siyah)
///   - "biomes" katmanı: biyom çokgenleri (id="biomeN")
///   - "rivers" katmanı: nehir çokgenleri
///
/// Unity'ye bağımlı değil (sade C#), böylece Unity dışında da test edilebilir.
/// Not: PowerShell'in eski C# derleyicisiyle de derlensin diye yeni C# söz dizimi kullanılmadı.
/// </summary>
public class AzgaarMapReader
{
    // Azgaar biyom numaraları
    public const int Marine = 0, HotDesert = 1, ColdDesert = 2, Savanna = 3, Grassland = 4,
        TropicalSeasonalForest = 5, TemperateDeciduousForest = 6, TropicalRainforest = 7,
        TemperateRainforest = 8, Taiga = 9, Tundra = 10, Glacier = 11, Wetland = 12;

    public int GridWidth;
    public int GridHeight;
    public float SvgWidth;
    public float SvgHeight;

    /// <summary>[x, y], y = 0 haritanın altı (Unity yönü).</summary>
    public bool[,] Land;
    /// <summary>[x, y] Azgaar biyom numarası; denizde 0.</summary>
    public int[,] Biome;
    /// <summary>[x, y] nehir hücresi mi (Land=false olan hücrelerin bir kısmı).</summary>
    public bool[,] River;
    /// <summary>[x, y] göl hücresi mi.</summary>
    public bool[,] Lake;
    /// <summary>[x, y] Azgaar yüksekliği (0-100, deniz seviyesi 20); ızgara noktaları arasında yumuşatılmış.</summary>
    public float[,] Height;
    /// <summary>[x, y] referanstaki kara parçaları (göl/nehir çıkarılmadan önce).</summary>
    public bool[,] OriginalLand;
    /// <summary>Denize dökülen nehirlerin ağız hücreleri {x, y}. Deniz seviyesi düşürülünce nehri yeni kıyıya uzatmak için.</summary>
    public List<int[]> RiverMouths = new List<int[]>();

    /// <param name="maxRivers">Sadece debisi en yüksek bu kadar nehir alınır (hepsi alınırsa kara paramparça olur).</param>
    public static AzgaarMapReader Load(string path, int gridWidth, int gridHeight, int maxRivers)
    {
        var lines = File.ReadAllLines(path);
        var r = new AzgaarMapReader();
        r.GridWidth = gridWidth;
        r.GridHeight = gridHeight;

        var header = lines[0].Split('|');
        r.SvgWidth = ParseFloat(header[4]);
        r.SvgHeight = ParseFloat(header[5]);

        string featureLine = FindLine(lines, "id=\"featurePaths\"");
        string maskLine = FindLine(lines, "<mask id=\"land\">");
        string viewbox = FindLine(lines, "id=\"viewbox\"");
        if (featureLine == null || maskLine == null || viewbox == null)
            throw new Exception("Azgaar .map dosyasında beklenen SVG katmanları bulunamadı: " + path);

        // Hangi feature kara (white), hangisi göl (black)
        var landFeatures = new HashSet<string>();
        var lakeFeatures = new HashSet<string>();
        foreach (Match m in Regex.Matches(maskLine, "href=\"#feature_(\\d+)\"[^>]*fill=\"(white|black)\""))
        {
            if (m.Groups[2].Value == "white") landFeatures.Add(m.Groups[1].Value);
            else lakeFeatures.Add(m.Groups[1].Value);
        }

        var landRings = new List<List<double[]>>();
        var lakeRings = new List<List<double[]>>();
        foreach (var tag in Paths(featureLine))
        {
            string id = Attr(tag, "id");
            if (id == null || !id.StartsWith("feature_")) continue;
            string num = id.Substring(8);
            var rings = ParsePath(Attr(tag, "d"));
            if (landFeatures.Contains(num)) landRings.AddRange(rings);
            else if (lakeFeatures.Contains(num)) lakeRings.AddRange(rings);
        }

        r.Land = new bool[gridWidth, gridHeight];
        r.Lake = new bool[gridWidth, gridHeight];
        r.River = new bool[gridWidth, gridHeight];
        r.Biome = new int[gridWidth, gridHeight];

        // Her kara parçası ayrı doldurulur (ada içindeki göller ayrıca çıkarılır)
        r.Fill(landRings, true, r.Land);
        r.Fill(lakeRings, true, r.Lake);
        r.OriginalLand = (bool[,])r.Land.Clone();
        r.ReadHeights(lines);

        // Biyomlar: her biyomun çokgenleri
        string biomeGroup = Group(viewbox, "biomes");
        if (biomeGroup != null)
        {
            foreach (var tag in Paths(biomeGroup))
            {
                string id = Attr(tag, "id");
                int b;
                if (id == null || !id.StartsWith("biome") || !int.TryParse(id.Substring(5), out b)) continue;
                var mask = new bool[gridWidth, gridHeight];
                r.Fill(ParsePath(Attr(tag, "d")), false, mask);
                for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                    if (mask[x, y]) r.Biome[x, y] = b;
            }
        }

        // Nehirler: dolu çokgen + her köşe noktası (ince nehirler en az 1 hücre olsun)
        string riverGroup = Group(viewbox, "rivers");
        var keepRivers = LargestRivers(lines, maxRivers);
        if (riverGroup != null)
        {
            foreach (var tag in Paths(riverGroup))
            {
                string id = Attr(tag, "id");
                if (id == null || !keepRivers.Contains(id)) continue;
                var rings = ParsePath(Attr(tag, "d"));
                r.Fill(rings, false, r.River);
                // Ağız: kıyıya yakın uçlardan en alçakta olanı (kaynak da bir koya yakın olabilir)
                int[] mouth = null;
                float mouthHeight = float.MaxValue;
                foreach (var ring in rings)
                    foreach (var p in ring)
                    {
                        int gx, gy;
                        if (!r.ToGrid(p[0], p[1], out gx, out gy)) continue;
                        r.River[gx, gy] = true;
                        if (r.Height[gx, gy] < mouthHeight && r.NearOriginalOcean(gx, gy, 2))
                        {
                            mouth = new[] { gx, gy };
                            mouthHeight = r.Height[gx, gy];
                        }
                    }
                if (mouth != null) r.RiverMouths.Add(mouth);
            }
        }

        // Kara = kara parçaları - göller - nehirler
        for (int x = 0; x < gridWidth; x++)
        for (int y = 0; y < gridHeight; y++)
        {
            if (r.Lake[x, y] || r.River[x, y]) r.Land[x, y] = false;
            if (!r.Land[x, y]) r.Biome[x, y] = Marine;
        }

        r.FillMissingBiomes();
        return r;
    }

    /// <summary>Nehir listesinden (JSON satırı) debisi en yüksek olanların SVG id'leri ("riverN").</summary>
    static HashSet<string> LargestRivers(string[] lines, int max)
    {
        var result = new HashSet<string>();
        string riverLine = FindLine(lines, "\"discharge\":");
        if (riverLine == null || max <= 0) return result;

        var rivers = new List<KeyValuePair<double, string>>();
        foreach (Match m in Regex.Matches(riverLine, "\\{\"i\":(\\d+),[^{}]*?\"discharge\":([\\d.]+)"))
            rivers.Add(new KeyValuePair<double, string>(
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), "river" + m.Groups[1].Value));

        rivers.Sort((a, b) => b.Key.CompareTo(a.Key));
        for (int i = 0; i < rivers.Count && i < max; i++) result.Add(rivers[i].Value);
        return result;
    }

    bool NearOriginalOcean(int gx, int gy, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            int x = gx + dx, y = gy + dy;
            if (x < 0 || y < 0 || x >= GridWidth || y >= GridHeight) return true;
            if (!OriginalLand[x, y]) return true;
        }
        return false;
    }

    /// <summary>
    /// Azgaar'ın kaba ızgarasındaki (ör. 149x67) yükseklikleri okur ve bizim ızgaraya iki doğrusal
    /// ara değerlemeyle yayar. Izgara satırı "{"spacing":...}" ile başlar, yükseklikler bir sonraki satırdadır.
    /// </summary>
    void ReadHeights(string[] lines)
    {
        Height = new float[GridWidth, GridHeight];
        int gridLine = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("{\"spacing\":")) { gridLine = i; break; }
        if (gridLine < 0 || gridLine + 1 >= lines.Length) return;

        string g = lines[gridLine];
        double spacing = double.Parse(Regex.Match(g, "\"spacing\":([\\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        int cellsX = int.Parse(Regex.Match(g, "\"cellsX\":(\\d+)").Groups[1].Value);
        int cellsY = int.Parse(Regex.Match(g, "\"cellsY\":(\\d+)").Groups[1].Value);

        var parts = lines[gridLine + 1].Split(',');
        if (parts.Length < cellsX * cellsY) return;
        var h = new float[cellsX, cellsY];
        for (int i = 0; i < cellsX * cellsY; i++)
            h[i % cellsX, i / cellsX] = float.Parse(parts[i], CultureInfo.InvariantCulture);

        double cellW = SvgWidth / GridWidth, cellH = SvgHeight / GridHeight;
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
        {
            double sx = (x + 0.5) * cellW, sy = (GridHeight - 1 - y + 0.5) * cellH;
            double fx = sx / spacing - 0.5, fy = sy / spacing - 0.5;
            int x0 = Clamp((int)Math.Floor(fx), 0, cellsX - 1), y0 = Clamp((int)Math.Floor(fy), 0, cellsY - 1);
            int x1 = Clamp(x0 + 1, 0, cellsX - 1), y1 = Clamp(y0 + 1, 0, cellsY - 1);
            double tx = Math.Max(0, Math.Min(1, fx - x0)), ty = Math.Max(0, Math.Min(1, fy - y0));
            double top = h[x0, y0] * (1 - tx) + h[x1, y0] * tx;
            double bottom = h[x0, y1] * (1 - tx) + h[x1, y1] * tx;
            Height[x, y] = (float)(top * (1 - ty) + bottom * ty);
        }
    }

    static int Clamp(int v, int min, int max)
    {
        return v < min ? min : v > max ? max : v;
    }

    /// <summary>
    /// Biyomu olmayan kara hücrelerini en yakın biyomla doldurur. Kıyıdaki bazı hücreler biyom çokgeninin
    /// dışında kalabildiği gibi, kara sonradan genişletilirse (deniz seviyesi düşürülünce) yeni kara da böyle dolar.
    /// </summary>
    public void FillMissingBiomes()
    {
        var queue = new Queue<int[]>();
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
            if (Land[x, y] && Biome[x, y] != Marine) queue.Enqueue(new[] { x, y });

        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = p[0] + dx[k], ny = p[1] + dy[k];
                if (nx < 0 || ny < 0 || nx >= GridWidth || ny >= GridHeight) continue;
                if (!Land[nx, ny] || Biome[nx, ny] != Marine) continue;
                Biome[nx, ny] = Biome[p[0], p[1]];
                queue.Enqueue(new[] { nx, ny });
            }
        }

        // Hiç biyomu olmayan kara (çok küçük adalar) çayır olsun
        for (int x = 0; x < GridWidth; x++)
        for (int y = 0; y < GridHeight; y++)
            if (Land[x, y] && Biome[x, y] == Marine) Biome[x, y] = Grassland;
    }

    bool ToGrid(double sx, double sy, out int gx, out int gy)
    {
        gx = (int)Math.Floor(sx / SvgWidth * GridWidth);
        gy = GridHeight - 1 - (int)Math.Floor(sy / SvgHeight * GridHeight);
        return gx >= 0 && gy >= 0 && gx < GridWidth && gy < GridHeight;
    }

    /// <summary>
    /// Halkaları tarama çizgisiyle doldurur (çift-tek kuralı). separate=true ise her halka ayrı doldurulur
    /// (ayrı ayrı adalar); false ise hepsi tek şekil sayılır (delikli çokgenler).
    /// </summary>
    void Fill(List<List<double[]>> rings, bool separate, bool[,] target)
    {
        if (separate)
        {
            foreach (var ring in rings) FillRings(new List<List<double[]>> { ring }, target);
        }
        else FillRings(rings, target);
    }

    void FillRings(List<List<double[]>> rings, bool[,] target)
    {
        double cellW = SvgWidth / GridWidth, cellH = SvgHeight / GridHeight;
        var xs = new List<double>();
        for (int gy = 0; gy < GridHeight; gy++)
        {
            // Hücrenin ortasından geçen yatay çizgi (SVG'de y aşağı doğru)
            double sy = (GridHeight - 1 - gy + 0.5) * cellH;
            xs.Clear();
            foreach (var ring in rings)
            {
                int n = ring.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % n];
                    if ((a[1] <= sy && b[1] > sy) || (b[1] <= sy && a[1] > sy))
                        xs.Add(a[0] + (sy - a[1]) / (b[1] - a[1]) * (b[0] - a[0]));
                }
            }
            if (xs.Count < 2) continue;
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = (int)Math.Ceiling(xs[k] / cellW - 0.5);
                int x1 = (int)Math.Floor(xs[k + 1] / cellW - 0.5);
                for (int gx = Math.Max(0, x0); gx <= Math.Min(GridWidth - 1, x1); gx++)
                    target[gx, gy] = true;
            }
        }
    }

    // ---------------------------------------------------------------- SVG ayrıştırma

    static string FindLine(string[] lines, string marker)
    {
        foreach (var l in lines)
            if (l.Contains(marker)) return l;
        return null;
    }

    static string Group(string svg, string id)
    {
        int start = svg.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        if (start < 0) return null;
        int end = svg.IndexOf("</g>", start, StringComparison.Ordinal);
        return end < 0 ? svg.Substring(start) : svg.Substring(start, end - start);
    }

    static IEnumerable<string> Paths(string svg)
    {
        foreach (Match m in Regex.Matches(svg, "<path\\b[^>]*>"))
            yield return m.Value;
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, "\\s" + Regex.Escape(name) + "=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    static readonly Regex Token = new Regex("[MLCQHVZmlcqhvz]|-?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE][-+]?\\d+)?");

    /// <summary>SVG path → halkalar. M/L/H/V/C/Q/Z (büyük ve küçük harf) desteklenir; eğriler düz parçalara bölünür.</summary>
    public static List<List<double[]>> ParsePath(string d)
    {
        var rings = new List<List<double[]>>();
        if (string.IsNullOrEmpty(d)) return rings;

        var tokens = new List<string>();
        foreach (Match m in Token.Matches(d)) tokens.Add(m.Value);

        List<double[]> ring = null;
        double cx = 0, cy = 0, sx = 0, sy = 0;
        char cmd = 'M';
        int i = 0;
        while (i < tokens.Count)
        {
            string t = tokens[i];
            if (char.IsLetter(t[0]))
            {
                cmd = t[0];
                i++;
                if (cmd == 'Z' || cmd == 'z')
                {
                    if (ring != null && ring.Count > 2) rings.Add(ring);
                    ring = null;
                    cx = sx; cy = sy;
                }
                continue;
            }

            bool rel = char.IsLower(cmd);
            double ox = rel ? cx : 0, oy = rel ? cy : 0;
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    if (ring != null && ring.Count > 2) rings.Add(ring);
                    cx = ox + Num(tokens, i); cy = oy + Num(tokens, i + 1); i += 2;
                    sx = cx; sy = cy;
                    ring = new List<double[]> { new[] { cx, cy } };
                    cmd = rel ? 'l' : 'L'; // M'den sonraki çiftler L sayılır
                    break;
                case 'L':
                    cx = ox + Num(tokens, i); cy = oy + Num(tokens, i + 1); i += 2;
                    Add(ref ring, cx, cy);
                    break;
                case 'H':
                    cx = ox + Num(tokens, i); i += 1;
                    Add(ref ring, cx, cy);
                    break;
                case 'V':
                    cy = oy + Num(tokens, i); i += 1;
                    Add(ref ring, cx, cy);
                    break;
                case 'C':
                {
                    double x1 = ox + Num(tokens, i), y1 = oy + Num(tokens, i + 1);
                    double x2 = ox + Num(tokens, i + 2), y2 = oy + Num(tokens, i + 3);
                    double x = ox + Num(tokens, i + 4), y = oy + Num(tokens, i + 5);
                    i += 6;
                    for (int s = 1; s <= 4; s++)
                    {
                        double u = s / 4.0, v = 1 - u;
                        Add(ref ring,
                            v * v * v * cx + 3 * v * v * u * x1 + 3 * v * u * u * x2 + u * u * u * x,
                            v * v * v * cy + 3 * v * v * u * y1 + 3 * v * u * u * y2 + u * u * u * y);
                    }
                    cx = x; cy = y;
                    break;
                }
                case 'Q':
                {
                    double x1 = ox + Num(tokens, i), y1 = oy + Num(tokens, i + 1);
                    double x = ox + Num(tokens, i + 2), y = oy + Num(tokens, i + 3);
                    i += 4;
                    for (int s = 1; s <= 3; s++)
                    {
                        double u = s / 3.0, v = 1 - u;
                        Add(ref ring, v * v * cx + 2 * v * u * x1 + u * u * x, v * v * cy + 2 * v * u * y1 + u * u * y);
                    }
                    cx = x; cy = y;
                    break;
                }
                default:
                    i++; // bilinmeyen komutun sayısını atla
                    break;
            }
        }
        if (ring != null && ring.Count > 2) rings.Add(ring);
        return rings;
    }

    static void Add(ref List<double[]> ring, double x, double y)
    {
        if (ring == null) ring = new List<double[]>();
        ring.Add(new[] { x, y });
    }

    static double Num(List<string> tokens, int i)
    {
        return i < tokens.Count ? double.Parse(tokens[i], CultureInfo.InvariantCulture) : 0;
    }

    static float ParseFloat(string s)
    {
        return float.Parse(s, CultureInfo.InvariantCulture);
    }
}
