using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;
using UnityEngine;

namespace KingOfNothing.World.Navigation
{
    /// <summary>
    /// A* pathfinding on the map grid. Walkable cells: land and shallow water (see MapData). The cost of a cell is
    /// 1 / its speed multiplier, so characters go around swamps and water when that is faster.
    /// If the goal cannot be reached (deep water, another island) the path ends at the closest reachable cell,
    /// so characters never get stuck at a river bank.
    /// </summary>
    public sealed class GridPathfinder
    {
        public const int DefaultMaxSearchNodes = 400000;

        readonly MapData map;
        readonly int width, height, count;
        /// <summary>Cost to enter each cell; infinity = not walkable.</summary>
        readonly float[] cost;
        float minCost;

        readonly float[] g;
        readonly int[] parent;
        readonly int[] openStamp;
        readonly int[] closedStamp;
        readonly MinHeap heap = new MinHeap(4096);
        readonly List<int> cells = new List<int>();
        int stamp;

        public int MaxSearchNodes { get; set; } = DefaultMaxSearchNodes;

        public GridPathfinder(MapData map)
        {
            this.map = map;
            width = map.Width;
            height = map.Height;
            count = width * height;
            cost = new float[count];
            minCost = float.MaxValue;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = new Vector2Int(x, y);
                int i = x + y * width;
                if (!map.IsWalkable(c))
                {
                    cost[i] = float.PositiveInfinity;
                    continue;
                }
                cost[i] = 1f / Mathf.Max(0.01f, map.SpeedMultiplierAt(c));
                if (cost[i] < minCost) minCost = cost[i];
            }
            if (minCost == float.MaxValue) minCost = 1f;
            g = new float[count];
            parent = new int[count];
            openStamp = new int[count];
            closedStamp = new int[count];
        }

        /// <summary>Recomputes one cell after an object appeared / disappeared there.</summary>
        public void RefreshCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            var c = new Vector2Int(x, y);
            int i = x + y * width;
            if (!map.IsWalkable(c))
            {
                cost[i] = float.PositiveInfinity;
                return;
            }
            cost[i] = 1f / Mathf.Max(0.01f, map.SpeedMultiplierAt(c));
            if (cost[i] < minCost) minCost = cost[i];
        }

        bool Walkable(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && !float.IsPositiveInfinity(cost[x + y * width]);

        float Heuristic(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            // Octile distance
            return (Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy)) * minCost;
        }

        /// <summary>Nearest walkable cell within the radius (spiral search), or -1.</summary>
        int NearestWalkable(int cx, int cy, int radius)
        {
            if (Walkable(cx, cy)) return cx + cy * width;
            for (int r = 1; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                if (Walkable(cx + dx, cy + dy)) return cx + dx + (cy + dy) * width;
            }
            return -1;
        }

        /// <summary>
        /// Finds a path and writes its waypoints (world positions, excluding the start) into <paramref name="path"/>.
        /// Returns false if nothing walkable was found near the start.
        /// </summary>
        public bool FindPath(Vector2 start, Vector2 goal, List<Vector2> path)
        {
            path.Clear();
            int sx = Mathf.FloorToInt(start.x), sy = Mathf.FloorToInt(start.y);
            int startCell = NearestWalkable(sx, sy, 4);
            if (startCell < 0) return false;
            int gxRaw = Mathf.FloorToInt(goal.x), gyRaw = Mathf.FloorToInt(goal.y);
            int goalCell = NearestWalkable(gxRaw, gyRaw, 30);
            if (goalCell < 0) return false;
            bool exactGoal = goalCell == gxRaw + gyRaw * width;
            int gx = goalCell % width, gy = goalCell / width;

            if (++stamp == int.MaxValue)
            {
                Array.Clear(openStamp, 0, count);
                Array.Clear(closedStamp, 0, count);
                stamp = 1;
            }
            heap.Clear();
            g[startCell] = 0f;
            parent[startCell] = -1;
            openStamp[startCell] = stamp;
            heap.Push(Heuristic(startCell % width, startCell / width, gx, gy), startCell);

            int best = startCell;
            float bestH = float.MaxValue;
            int expanded = 0;
            bool found = false;
            while (heap.TryPop(out _, out int i, out _))
            {
                if (closedStamp[i] == stamp) continue;
                closedStamp[i] = stamp;
                int x = i % width, y = i / width;
                float h = Heuristic(x, y, gx, gy);
                if (h < bestH)
                {
                    bestH = h;
                    best = i;
                }
                if (i == goalCell)
                {
                    found = true;
                    break;
                }
                if (++expanded > MaxSearchNodes) break;

                for (int k = 0; k < 8; k++)
                {
                    int dx = Generation.GridAlgorithms.Dx8[k], dy = Generation.GridAlgorithms.Dy8[k];
                    int nx = x + dx, ny = y + dy;
                    if (!Walkable(nx, ny)) continue;
                    // No corner cutting: a diagonal step needs both side cells walkable
                    if (dx != 0 && dy != 0 && (!Walkable(x + dx, y) || !Walkable(x, y + dy))) continue;
                    int j = nx + ny * width;
                    if (closedStamp[j] == stamp) continue;
                    float ng = g[i] + Generation.GridAlgorithms.Length8[k] * 0.5f * (cost[i] + cost[j]);
                    if (openStamp[j] == stamp && ng >= g[j]) continue;
                    openStamp[j] = stamp;
                    g[j] = ng;
                    parent[j] = i;
                    heap.Push(ng + Heuristic(nx, ny, gx, gy), j);
                }
            }

            int end = found ? goalCell : best;
            cells.Clear();
            for (int c = end; c >= 0; c = parent[c]) cells.Add(c);
            cells.Reverse();
            Smooth(start, cells, path);
            if (found && exactGoal) path[path.Count - 1] = goal;
            return true;
        }

        /// <summary>Removes waypoints that can be skipped in a straight line without leaving cheaper terrain.</summary>
        void Smooth(Vector2 start, List<int> route, List<Vector2> path)
        {
            if (route.Count <= 1)
            {
                path.Add(Center(route[route.Count - 1]));
                return;
            }
            int anchor = 0;
            while (anchor < route.Count - 1)
            {
                int next = anchor + 1;
                for (int k = route.Count - 1; k > anchor + 1; k--)
                {
                    if (!LineOfSight(route[anchor], route[k])) continue;
                    next = k;
                    break;
                }
                path.Add(Center(route[next]));
                anchor = next;
            }
        }

        Vector2 Center(int i) => new Vector2(i % width + 0.5f, i / width + 0.5f);

        /// <summary>Every cell on the line is walkable and not slower than the slower end point.</summary>
        bool LineOfSight(int a, int b)
        {
            int x0 = a % width, y0 = a / width, x1 = b % width, y1 = b / width;
            float limit = Math.Max(cost[a], cost[b]) + 1e-4f;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            while (true)
            {
                if (!Walkable(x, y) || cost[x + y * width] > limit) return false;
                if (x == x1 && y == y1) return true;
                int e2 = 2 * err;
                // Moving diagonally: both side cells must be passable too (4-connected line)
                if (e2 > -dy && e2 < dx)
                {
                    if (!Walkable(x + sx, y) || !Walkable(x, y + sy)) return false;
                    if (cost[x + sx + y * width] > limit || cost[x + (y + sy) * width] > limit) return false;
                }
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }
        }
    }
}
