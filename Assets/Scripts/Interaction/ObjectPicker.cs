using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// Finds the map object under the mouse by its visible shape (the sprite's tight mesh), not by the whole grid
    /// cell: clicking the grass next to a tree hits nothing. Of overlapping objects the one drawn in front wins;
    /// on a tie the most recently added one (developer mode overlaps).
    /// </summary>
    public static class ObjectPicker
    {
        static readonly List<WorldObject> Candidates = new List<WorldObject>();

        /// <summary>How far around the mouse cell objects are checked (tall trees are drawn several cells above their cell).</summary>
        static readonly RectInt SearchArea = new RectInt(-3, -4, 7, 6);

        public static WorldObject Pick(MapData map, Vector2 world)
        {
            if (map == null || !map.IsReady) return null;
            var cell = MapData.WorldToCell(world);
            Candidates.Clear();
            map.CollectObjects(new RectInt(cell.x + SearchArea.x, cell.y + SearchArea.y, SearchArea.width, SearchArea.height), Candidates);

            WorldObject best = null;
            int bestOrder = int.MinValue;
            foreach (var o in Candidates)
            {
                if (o == null || o.IsCarried) continue;
                int order = int.MinValue;
                foreach (var r in o.Renderers)
                    if (r != null && r.enabled && Contains(r, world)) order = Mathf.Max(order, r.sortingOrder);
                if (order == int.MinValue) continue;
                if (best == null || order > bestOrder || (order == bestOrder && o.Uid > best.Uid))
                {
                    best = o;
                    bestOrder = order;
                }
            }
            return best;
        }

        /// <summary>Is the world point inside the visible part of the sprite?</summary>
        public static bool Contains(SpriteRenderer renderer, Vector2 world)
        {
            var sprite = renderer.sprite;
            if (sprite == null || !renderer.bounds.Contains(new Vector3(world.x, world.y, renderer.bounds.center.z))) return false;
            Vector2 p = renderer.transform.InverseTransformPoint(world);
            if (renderer.flipX) p.x = -p.x;
            if (renderer.flipY) p.y = -p.y;

            var vertices = sprite.vertices;
            var triangles = sprite.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                if (InTriangle(p, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]])) return true;
            return false;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
