using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;

namespace KingOfNothing.World.Generation
{
    /// <summary>
    /// Traces rivers along the drainage network and carves them:
    ///  - outlet rivers leaving lakes (count per lake configurable),
    ///  - highland rivers starting far from the sea,
    ///  - tributaries that start near a river and join it (branches / veins).
    /// Rivers stop when they reach the sea, a lake or another river, so they merge naturally.
    /// Width grows downstream in steps (= narrows towards the source) and the course gets smooth meanders.
    /// </summary>
    public sealed class RiverPass : MapGenerationPass
    {
        const int EndFadeCells = 10;

        public override string Name => "Rivers";

        MapGenerationContext context;
        MapLayout map;
        RiverSettings settings;
        bool[] onRiverPath;
        readonly List<List<int>> paths = new List<List<int>>();
        readonly List<RiverInfo> infos = new List<RiverInfo>();

        public override void Execute(MapGenerationContext ctx)
        {
            context = ctx;
            map = ctx.Layout;
            settings = ctx.Config.Rivers;
            if (!settings.enabled || ctx.DrainageNext == null) return;

            onRiverPath = new bool[map.CellCount];
            paths.Clear();
            infos.Clear();

            TraceLakeOutlets();
            TraceHighlandRivers();
            TraceBranchesOfExistingRivers();
            Carve();

            map.Rivers.AddRange(infos);
        }

        // ------------------------------------------------------------------ tracing

        /// <summary>Follows the drainage network downstream until sea, another lake or an existing river.</summary>
        List<int> Trace(int start, int ownLakeId)
        {
            var next = context.DrainageNext;
            var path = new List<int> { start };
            int i = start;
            while (true)
            {
                int j = next[i];
                if (j < 0) break;
                path.Add(j);
                var terrain = map.Terrain[j];
                if (terrain == CellTerrain.Sea) break;
                if (terrain == CellTerrain.Lake && map.LakeId[j] != ownLakeId) break;
                if (terrain == CellTerrain.River || onRiverPath[j]) break; // joins an existing river

                i = j;
            }
            return path;
        }

        void Accept(List<int> path, bool tributary, bool fromLake)
        {
            foreach (int c in path) onRiverPath[c] = true;
            paths.Add(path);
            var info = new RiverInfo { Id = infos.Count + 1, IsTributary = tributary, FromLake = fromLake };
            info.Cells.AddRange(path);
            infos.Add(info);
        }

        void TraceLakeOutlets()
        {
            var lakeSettings = context.Config.Lakes;
            if (!lakeSettings.enabled) return;

            var random = context.Random;
            var distance = context.DrainageDistance;
            int w = map.Width, h = map.Height;
            var shore = new List<int>();
            var shoreSet = new HashSet<int>();
            var used = new List<int>();

            foreach (var lake in map.Lakes)
            {
                if (random.NextFloat() > lakeSettings.outletChance) continue;
                int wanted = lakeSettings.outletsPerLake.Roll(random);
                if (wanted <= 0) continue;

                // Land cells touching this lake, best (lowest drainage distance) first
                shore.Clear();
                shoreSet.Clear();
                int extent = (int)Math.Ceiling(lake.Radius * 2.5f) + 2;
                for (int y = Math.Max(0, lake.CenterY - extent); y <= Math.Min(h - 1, lake.CenterY + extent); y++)
                for (int x = Math.Max(0, lake.CenterX - extent); x <= Math.Min(w - 1, lake.CenterX + extent); x++)
                {
                    int i = x + y * w;
                    if (map.LakeId[i] != lake.Id) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + GridAlgorithms.Dx4[k], ny = y + GridAlgorithms.Dy4[k];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        if (map.Terrain[j] == CellTerrain.Land && shoreSet.Add(j)) shore.Add(j);
                    }
                }
                shore.Sort((a, b) => distance[a].CompareTo(distance[b]));

                used.Clear();
                foreach (int start in shore)
                {
                    if (used.Count >= wanted) break;
                    if (onRiverPath[start]) continue;
                    int sx = start % w, sy = start / w;
                    bool tooClose = false;
                    foreach (int u in used)
                    {
                        float dx = sx - u % w, dy = sy - u / w;
                        if (Math.Sqrt(dx * dx + dy * dy) < lake.Radius * 1.2f) { tooClose = true; break; }
                    }
                    if (tooClose) continue;

                    var path = Trace(start, lake.Id);
                    if (path.Count < settings.minRiverLength / 2) continue;
                    int end = path[path.Count - 1];
                    if (map.Terrain[end] == CellTerrain.Lake && map.LakeId[end] == lake.Id) continue; // flows back into itself

                    used.Add(start);
                    Accept(path, false, true);
                }
            }
        }

        void TraceHighlandRivers()
        {
            var random = context.Random;
            var distance = context.DrainageDistance;
            int w = map.Width, h = map.Height;

            var candidates = new List<int>();
            for (int i = 0; i < map.CellCount; i++)
                if (map.Terrain[i] == CellTerrain.Land && distance[i] > settings.minSourceDistanceToSea && !float.IsInfinity(distance[i]))
                    candidates.Add(i);
            if (candidates.Count == 0) return;

            int wanted = settings.sourceCount.Roll(random);
            var sources = new List<int>();
            for (int attempt = 0; attempt < wanted * 80 && sources.Count < wanted; attempt++)
            {
                int start = candidates[random.Range(0, candidates.Count)];
                if (onRiverPath[start]) continue;
                int sx = start % w, sy = start / w;
                bool tooClose = false;
                foreach (int s in sources)
                {
                    float dx = sx - s % w, dy = sy - s / w;
                    if (Math.Sqrt(dx * dx + dy * dy) < settings.minDistanceBetweenSources) { tooClose = true; break; }
                }
                if (tooClose) continue;

                var path = Trace(start, 0);
                if (path.Count < settings.minRiverLength) continue;
                sources.Add(start);
                Accept(path, false, false);

                int tributaries = settings.tributariesPerRiver.Roll(random);
                for (int t = 0; t < tributaries; t++)
                {
                    for (int tryCount = 0; tryCount < 30; tryCount++)
                    {
                        // Tributaries start beside the upper half of the main river
                        int k = random.RangeInclusive(0, path.Count / 2);
                        int pc = path[k];
                        int px = pc % w, py = pc / w;
                        float angle = random.NextFloat() * (float)Math.PI * 2f;
                        float dist = settings.tributaryDistance.Roll(random);
                        int tx = (int)(px + Math.Cos(angle) * dist), ty = (int)(py + Math.Sin(angle) * dist);
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        int ti = tx + ty * w;
                        if (map.Terrain[ti] != CellTerrain.Land || onRiverPath[ti] || distance[ti] < distance[pc]) continue;

                        var tributary = Trace(ti, 0);
                        if (tributary.Count < settings.minTributaryLength) continue;
                        Accept(tributary, true, false);
                        break;
                    }
                }
            }
        }

        /// <summary>Branches for rivers that already exist before this pass (the reference map's rivers).</summary>
        void TraceBranchesOfExistingRivers()
        {
            var random = context.Random;
            var distance = context.DrainageDistance;
            int w = map.Width, h = map.Height;

            var riverCells = new List<int>();
            for (int i = 0; i < map.CellCount; i++)
                if (map.Terrain[i] == CellTerrain.River) riverCells.Add(i);
            if (riverCells.Count == 0) return;

            int wanted = settings.existingRiverBranches.Roll(random);
            int made = 0;
            for (int attempt = 0; attempt < wanted * 40 && made < wanted; attempt++)
            {
                int rc = riverCells[random.Range(0, riverCells.Count)];
                int rx = rc % w, ry = rc / w;
                float angle = random.NextFloat() * (float)Math.PI * 2f;
                float dist = settings.tributaryDistance.Roll(random);
                int tx = (int)(rx + Math.Cos(angle) * dist), ty = (int)(ry + Math.Sin(angle) * dist);
                if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                int ti = tx + ty * w;
                if (map.Terrain[ti] != CellTerrain.Land || onRiverPath[ti] || float.IsInfinity(distance[ti])) continue;
                if (distance[ti] < distance[rc]) continue;

                var branch = Trace(ti, 0);
                if (branch.Count < settings.minTributaryLength) continue;
                // Only keep it if it really joins a river (not a separate one running to the sea)
                if (map.Terrain[branch[branch.Count - 1]] != CellTerrain.River) continue;
                Accept(branch, true, false);
                made++;
            }
        }

        // ------------------------------------------------------------------ carving

        void Carve()
        {
            int w = map.Width;
            var next = context.DrainageNext;

            // Flow: how many rivers pass through each river cell (downstream of a confluence = 2+)
            var flow = new int[map.CellCount];
            foreach (var path in paths)
            {
                int c = path[0];
                int guard = map.CellCount;
                while (c >= 0 && onRiverPath[c] && map.Terrain[c] != CellTerrain.Sea && guard-- > 0)
                {
                    flow[c]++;
                    c = next[c];
                }
            }

            var meanderNoise = new GradientNoise(context.NoiseSeed(401));
            var random = context.Random;
            // Final (meandered) position of every carved path cell, so tributaries can connect to it
            var displaced = new Dictionary<int, (int x, int y)>();

            for (int p = 0; p < paths.Count; p++)
            {
                var path = paths[p];
                int count = path.Count;
                var px = new float[count];
                var py = new float[count];
                var length = new float[count];
                var width = new int[count];

                int current = Math.Max(1, settings.sourceWidth);
                float nextStep = settings.widthStepLength.Roll(random);
                float travelled = 0f;
                for (int k = 0; k < count; k++)
                {
                    px[k] = path[k] % w;
                    py[k] = path[k] / w;
                    if (k > 0)
                    {
                        float dx = px[k] - px[k - 1], dy = py[k] - py[k - 1];
                        travelled += (float)Math.Sqrt(dx * dx + dy * dy);
                    }
                    length[k] = travelled;
                    while (travelled >= nextStep)
                    {
                        current = Math.Min(settings.maxWidth, current + settings.widthStep);
                        nextStep += Math.Max(1, settings.widthStepLength.Roll(random));
                    }
                    int bonus = flow[path[k]] >= 2 ? settings.confluenceBonus : 0;
                    width[k] = Math.Min(settings.maxWidth, current + bonus);
                }

                Smooth(px, settings.courseSmoothing, out var sx);
                Smooth(py, settings.courseSmoothing, out var sy);

                int lastX = int.MinValue, lastY = int.MinValue;
                for (int k = 0; k < count; k++)
                {
                    // Tangent by central differences, normal = tangent rotated 90 degrees
                    int a = Math.Max(0, k - 1), b = Math.Min(count - 1, k + 1);
                    float tx = sx[b] - sx[a], ty = sy[b] - sy[a];
                    float len = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-6f;
                    float nx = -ty / len, ny = tx / len;

                    float fade = Math.Min(1f, Math.Min(k, count - 1 - k) / (float)EndFadeCells);
                    float offset = (meanderNoise.Sample(length[k] / settings.meanderWavelength, p * 3.7f) - 0.5f)
                                   * 2f * settings.meanderAmplitude * fade;
                    int cx = (int)Math.Round(sx[k] + nx * offset);
                    int cy = (int)Math.Round(sy[k] + ny * offset);

                    if (lastX != int.MinValue) StampLine(lastX, lastY, cx, cy, width[k]);
                    else Stamp(cx, cy, width[k]);
                    lastX = cx;
                    lastY = cy;

                    if (!displaced.ContainsKey(path[k])) displaced[path[k]] = (cx, cy);
                }

                // Make sure the river really reaches its end (sea, lake or the meandered course of another river)
                int end = path[count - 1];
                int endX = end % w, endY = end / w;
                StampLine(lastX, lastY, endX, endY, width[count - 1]);
                if (displaced.TryGetValue(end, out var target) && (target.x != endX || target.y != endY))
                    StampLine(endX, endY, target.x, target.y, width[count - 1]);
            }
        }

        static void Smooth(float[] values, int radius, out float[] result)
        {
            int n = values.Length;
            result = new float[n];
            if (radius <= 0 || n <= radius * 2)
            {
                Array.Copy(values, result, n);
                return;
            }
            float norm = 1f / (radius * 2 + 1);
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int j = i + k;
                    if (j < 0) j = 0;
                    else if (j >= n) j = n - 1;
                    sum += values[j];
                }
                result[i] = sum * norm;
            }
        }

        /// <summary>4-connected line so a thin river never touches only diagonally.</summary>
        void StampLine(int x0, int y0, int x1, int y1, int width)
        {
            int x = x0, y = y0;
            Stamp(x, y, width);
            int guard = Math.Abs(x1 - x0) + Math.Abs(y1 - y0) + 2;
            while ((x != x1 || y != y1) && guard-- > 0)
            {
                if (Math.Abs(x1 - x) >= Math.Abs(y1 - y)) x += x1 > x ? 1 : -1;
                else y += y1 > y ? 1 : -1;
                Stamp(x, y, width);
            }
        }

        void Stamp(int cx, int cy, int width)
        {
            float r = (width - 1) * 0.5f + 0.35f;
            int ri = (int)Math.Ceiling(r);
            float r2 = r * r + 0.01f;
            for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = cx + dx, y = cy + dy;
                if (!map.InBounds(x, y)) continue;
                int i = x + y * map.Width;
                if (map.Terrain[i] == CellTerrain.Land) map.Terrain[i] = CellTerrain.River;
            }
        }
    }
}
