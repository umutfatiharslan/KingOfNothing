using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Reads an Azgaar Fantasy Map Generator save (.map) and rasterises it to a grid of the requested size.
    /// Uses the SVG layers stored in the file:
    ///   - featurePaths + the "land" mask: land masses (white) and lakes (black)
    ///   - the "biomes" layer: biome polygons (id="biomeN")
    ///   - the "rivers" layer: river polygons (only the largest rivers by discharge are kept)
    ///   - the height grid line ({"spacing":...}) for terrain height
    /// Pure C#, no Unity dependency.
    /// </summary>
    public static class AzgaarMapParser
    {
        /// <param name="maxRivers">Only this many rivers with the highest discharge are taken (all of them would shred the land).</param>
        /// <param name="riverWarp">Sideways bend (cells) of the river outlines so long straight stretches become curved.</param>
        /// <param name="riverWarpScale">Length (cells) of one bend.</param>
        public static ReferenceMapData Parse(string text, int gridWidth, int gridHeight, int maxRivers, float riverWarp = 0f, float riverWarpScale = 22f)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd('\r');

            var header = lines[0].Split('|');
            if (header.Length < 6) throw new FormatException("Not an Azgaar .map file (header).");
            var p = new Rasterizer(gridWidth, gridHeight, ParseFloat(header[4]), ParseFloat(header[5]));
            var data = new ReferenceMapData(gridWidth, gridHeight);

            string featureLine = FindLine(lines, "id=\"featurePaths\"");
            string maskLine = FindLine(lines, "<mask id=\"land\">");
            string viewbox = FindLine(lines, "id=\"viewbox\"");
            if (featureLine == null || maskLine == null || viewbox == null)
                throw new FormatException("The expected SVG layers were not found in the Azgaar .map file.");

            // Which features are land (white) and which are lakes (black)
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
                if (id == null || !id.StartsWith("feature_", StringComparison.Ordinal)) continue;
                string num = id.Substring(8);
                var rings = ParsePath(Attr(tag, "d"));
                if (landFeatures.Contains(num)) landRings.AddRange(rings);
                else if (lakeFeatures.Contains(num)) lakeRings.AddRange(rings);
            }

            // Every land mass is filled on its own (lakes inside islands are cut out separately)
            p.Fill(landRings, true, data.Land);
            p.Fill(lakeRings, true, data.Lake);
            Array.Copy(data.Land, data.OriginalLand, data.Land.Length);
            ReadHeights(lines, p, data);

            // Biomes
            string biomeGroup = Group(viewbox, "biomes");
            if (biomeGroup != null)
            {
                var mask = new bool[data.CellCount];
                foreach (var tag in Paths(biomeGroup))
                {
                    string id = Attr(tag, "id");
                    if (id == null || !id.StartsWith("biome", StringComparison.Ordinal) || !int.TryParse(id.Substring(5), out int b)) continue;
                    if (b < 0 || b >= ReferenceMapData.BiomeCount) continue;
                    Array.Clear(mask, 0, mask.Length);
                    p.Fill(ParsePath(Attr(tag, "d")), false, mask);
                    for (int i = 0; i < mask.Length; i++)
                        if (mask[i]) data.Biome[i] = (ReferenceBiome)b;
                }
            }

            // Rivers: filled polygon + every vertex (thin rivers are at least one cell wide)
            string riverGroup = Group(viewbox, "rivers");
            var keepRivers = LargestRivers(lines, maxRivers);
            if (riverGroup != null)
            {
                foreach (var tag in Paths(riverGroup))
                {
                    string id = Attr(tag, "id");
                    if (id == null || !keepRivers.Contains(id)) continue;
                    var rings = ParsePath(Attr(tag, "d"));
                    if (riverWarp > 0f) rings = p.Bend(rings, riverWarp, riverWarpScale);
                    p.Fill(rings, false, data.River);
                    // Mouth: the lowest end that is close to the sea (a source may also lie near a bay)
                    int mouth = -1;
                    float mouthHeight = float.MaxValue;
                    foreach (var ring in rings)
                    foreach (var point in ring)
                    {
                        if (!p.ToGrid(point[0], point[1], out int gx, out int gy)) continue;
                        int i = gx + gy * gridWidth;
                        data.River[i] = true;
                        if (data.Elevation[i] < mouthHeight && NearOriginalOcean(data, gx, gy, 2))
                        {
                            mouth = i;
                            mouthHeight = data.Elevation[i];
                        }
                    }
                    if (mouth >= 0) data.RiverMouths.Add(mouth);
                }
            }

            // Land = land masses - lakes - rivers
            for (int i = 0; i < data.CellCount; i++)
            {
                if (data.Lake[i] || data.River[i]) data.Land[i] = false;
                if (!data.Land[i]) data.Biome[i] = ReferenceBiome.Marine;
            }
            return data;
        }

        /// <summary>SVG ids ("riverN") of the rivers with the highest discharge.</summary>
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

        static bool NearOriginalOcean(ReferenceMapData data, int gx, int gy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = gx + dx, y = gy + dy;
                if (x < 0 || y < 0 || x >= data.Width || y >= data.Height) return true;
                if (!data.OriginalLand[x + y * data.Width]) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the heights of Azgaar's coarse grid and spreads them over our grid with bilinear interpolation.
        /// The grid line starts with {"spacing":...}; the heights are on the next line.
        /// </summary>
        static void ReadHeights(string[] lines, Rasterizer p, ReferenceMapData data)
        {
            int gridLine = -1;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("{\"spacing\":", StringComparison.Ordinal)) { gridLine = i; break; }
            if (gridLine < 0 || gridLine + 1 >= lines.Length) return;

            string g = lines[gridLine];
            double spacing = double.Parse(Regex.Match(g, "\"spacing\":([\\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
            int cellsX = int.Parse(Regex.Match(g, "\"cellsX\":(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            int cellsY = int.Parse(Regex.Match(g, "\"cellsY\":(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

            var parts = lines[gridLine + 1].Split(',');
            if (parts.Length < cellsX * cellsY) return;
            var h = new float[cellsX * cellsY];
            for (int i = 0; i < cellsX * cellsY; i++) h[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);

            double cellW = p.SvgWidth / data.Width, cellH = p.SvgHeight / data.Height;
            for (int y = 0; y < data.Height; y++)
            for (int x = 0; x < data.Width; x++)
            {
                double sx = (x + 0.5) * cellW, sy = (data.Height - 1 - y + 0.5) * cellH;
                double fx = sx / spacing - 0.5, fy = sy / spacing - 0.5;
                int x0 = Clamp((int)Math.Floor(fx), 0, cellsX - 1), y0 = Clamp((int)Math.Floor(fy), 0, cellsY - 1);
                int x1 = Clamp(x0 + 1, 0, cellsX - 1), y1 = Clamp(y0 + 1, 0, cellsY - 1);
                double tx = Math.Max(0, Math.Min(1, fx - x0)), ty = Math.Max(0, Math.Min(1, fy - y0));
                double top = h[x0 + y0 * cellsX] * (1 - tx) + h[x1 + y0 * cellsX] * tx;
                double bottom = h[x0 + y1 * cellsX] * (1 - tx) + h[x1 + y1 * cellsX] * tx;
                data.Elevation[x + y * data.Width] = (float)(top * (1 - ty) + bottom * ty);
            }
        }

        static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        // ------------------------------------------------------------------ rasterisation

        sealed class Rasterizer
        {
            public readonly int GridWidth, GridHeight;
            public readonly double SvgWidth, SvgHeight;

            public Rasterizer(int gridWidth, int gridHeight, double svgWidth, double svgHeight)
            {
                GridWidth = gridWidth;
                GridHeight = gridHeight;
                SvgWidth = svgWidth;
                SvgHeight = svgHeight;
            }

            /// <summary>
            /// Splits the outline into pieces shorter than half a cell and shifts every point along a smooth noise field
            /// (amplitude in cells). Both banks of a river move together, so it stays one connected ribbon; marking every
            /// point afterwards keeps even the thinnest river continuous.
            /// </summary>
            public List<List<double[]>> Bend(List<List<double[]>> rings, float amplitude, float scale)
            {
                var noiseX = new Utilities.GradientNoise(91731);
                var noiseY = new Utilities.GradientNoise(55117);
                double cellW = SvgWidth / GridWidth, cellH = SvgHeight / GridHeight;
                double step = Math.Min(cellW, cellH) * 0.5;
                var result = new List<List<double[]>>();
                foreach (var ring in rings)
                {
                    var bent = new List<double[]>();
                    int n = ring.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = ring[i];
                        var b = ring[(i + 1) % n];
                        double length = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                        int pieces = Math.Max(1, (int)Math.Ceiling(length / step));
                        for (int k = 0; k < pieces; k++)
                        {
                            double t = k / (double)pieces;
                            double x = a[0] + (b[0] - a[0]) * t, y = a[1] + (b[1] - a[1]) * t;
                            float gx = (float)(x / cellW) / scale, gy = (float)(y / cellH) / scale;
                            double dx = (noiseX.Fractal(gx, gy, 2) - 0.5) * 2.0 * amplitude * cellW;
                            double dy = (noiseY.Fractal(gx, gy, 2) - 0.5) * 2.0 * amplitude * cellH;
                            bent.Add(new[] { x + dx, y + dy });
                        }
                    }
                    if (bent.Count > 2) result.Add(bent);
                }
                return result;
            }

            public bool ToGrid(double sx, double sy, out int gx, out int gy)
            {
                gx = (int)Math.Floor(sx / SvgWidth * GridWidth);
                gy = GridHeight - 1 - (int)Math.Floor(sy / SvgHeight * GridHeight);
                return gx >= 0 && gy >= 0 && gx < GridWidth && gy < GridHeight;
            }

            /// <summary>
            /// Scanline fill (even-odd rule). separate = every ring filled on its own (separate islands);
            /// otherwise all rings form one shape (polygons with holes).
            /// </summary>
            public void Fill(List<List<double[]>> rings, bool separate, bool[] target)
            {
                if (separate)
                {
                    foreach (var ring in rings) FillRings(new List<List<double[]>> { ring }, target);
                }
                else FillRings(rings, target);
            }

            void FillRings(List<List<double[]>> rings, bool[] target)
            {
                double cellW = SvgWidth / GridWidth, cellH = SvgHeight / GridHeight;
                var xs = new List<double>();
                for (int gy = 0; gy < GridHeight; gy++)
                {
                    // Horizontal line through the cell centres (SVG y points down)
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
                            target[gx + gy * GridWidth] = true;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ SVG parsing

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

        /// <summary>SVG path to rings. Supports M/L/H/V/C/Q/Z (upper and lower case); curves become line segments.</summary>
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
                        cx = sx;
                        cy = sy;
                    }
                    continue;
                }

                bool relative = char.IsLower(cmd);
                double ox = relative ? cx : 0, oy = relative ? cy : 0;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        if (ring != null && ring.Count > 2) rings.Add(ring);
                        cx = ox + Num(tokens, i);
                        cy = oy + Num(tokens, i + 1);
                        i += 2;
                        sx = cx;
                        sy = cy;
                        ring = new List<double[]> { new[] { cx, cy } };
                        cmd = relative ? 'l' : 'L'; // pairs after M are treated as L
                        break;
                    case 'L':
                        cx = ox + Num(tokens, i);
                        cy = oy + Num(tokens, i + 1);
                        i += 2;
                        Add(ref ring, cx, cy);
                        break;
                    case 'H':
                        cx = ox + Num(tokens, i);
                        i += 1;
                        Add(ref ring, cx, cy);
                        break;
                    case 'V':
                        cy = oy + Num(tokens, i);
                        i += 1;
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
                        cx = x;
                        cy = y;
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
                        cx = x;
                        cy = y;
                        break;
                    }
                    default:
                        i++; // skip the number of an unknown command
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

        static double Num(List<string> tokens, int i) =>
            i < tokens.Count ? double.Parse(tokens[i], CultureInfo.InvariantCulture) : 0;

        static float ParseFloat(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
