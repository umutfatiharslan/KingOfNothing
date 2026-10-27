using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// The map objects currently selected (Shift + drag in Default mode, Shift + drag in the developer Select tool,
    /// drag in the developer Move tool). Selected objects get an orange outline and overlay (SelectionHighlight)
    /// and show their health bar.
    /// </summary>
    public static class ObjectSelection
    {
        static readonly List<WorldObject> selected = new List<WorldObject>();
        static readonly List<WorldObject> scratch = new List<WorldObject>();

        /// <summary>Upper limit of cells scanned by one box selection (protects against a box over the whole map).</summary>
        public const int MaxBoxCells = 250000;

        public static IReadOnlyList<WorldObject> Selected
        {
            get
            {
                selected.RemoveAll(o => o == null);
                return selected;
            }
        }

        public static int Count => Selected.Count;

        public static bool Contains(WorldObject o) => o != null && selected.Contains(o);

        public static void Add(WorldObject o)
        {
            if (o == null || selected.Contains(o)) return;
            selected.Add(o);
            Tint(o, true);
        }

        public static void Remove(WorldObject o)
        {
            if (o == null || !selected.Remove(o)) return;
            Tint(o, false);
        }

        public static void Toggle(WorldObject o)
        {
            if (Contains(o)) Remove(o);
            else Add(o);
        }

        public static void Clear()
        {
            foreach (var o in selected)
                if (o != null) Tint(o, false);
            selected.Clear();
        }

        /// <summary>Selects every object whose ground point lies inside the world rectangle.</summary>
        public static void SelectInWorldRect(Rect world, MapData map, bool additive)
        {
            if (!additive) Clear();
            if (map == null || !map.IsReady) return;
            int xMin = Mathf.FloorToInt(world.xMin) - 1, xMax = Mathf.CeilToInt(world.xMax);
            int yMin = Mathf.FloorToInt(world.yMin) - 1, yMax = Mathf.CeilToInt(world.yMax);
            if ((long)(xMax - xMin) * (yMax - yMin) > MaxBoxCells) return;

            scratch.Clear();
            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                var o = map.GetObjectAt(new Vector2Int(x, y));
                if (o == null || scratch.Contains(o)) continue;
                if (world.Contains(o.GroundPoint)) scratch.Add(o);
            }
            foreach (var o in scratch) Add(o);
        }

        /// <summary>Draws a selection box between two screen points (call from OnGUI).</summary>
        public static void DrawScreenBox(Vector2 a, Vector2 b)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                                       Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
            var old = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.3f, 0.15f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            const float t = 2f;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - t, rect.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, t, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - t, rect.yMin, t, rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Tint(WorldObject o, bool on)
        {
            if (on) SelectionHighlight.Show(o);
            else SelectionHighlight.Hide(o);
        }

        /// <summary>The object under a world point (tall sprites are drawn above their cell, so cells below count too).</summary>
        public static WorldObject FindAt(MapData map, Vector2 world, int cellsBelow = 2) => ObjectPicker.Pick(map, world);

        /// <summary>Old cell based lookup (kept for tools that need the cell owner).</summary>
        public static WorldObject FindInCellColumn(MapData map, Vector2 world, int cellsBelow = 2)
        {
            if (map == null || !map.IsReady) return null;
            var cell = MapData.WorldToCell(world);
            for (int dy = 0; dy <= cellsBelow; dy++)
            {
                var o = map.GetObjectAt(new Vector2Int(cell.x, cell.y - dy));
                if (o != null) return o;
            }
            return null;
        }
    }
}
