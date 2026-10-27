using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Brings a unit next to a target: it walks to the free cell around the object that is closest to the unit
    /// (not to a fixed side), then takes a short straight step (the hitbox stops it at the object's side).
    /// Gives up after a few attempts (unreachable target).
    /// </summary>
    public sealed class Approach
    {
        const int MaxAttempts = 4;
        int attempts;
        bool straightTried;

        public void Reset()
        {
            attempts = 0;
            straightTried = false;
        }

        /// <summary>Distance from the unit's feet to the area.</summary>
        public static float DistanceTo(Unit unit, Rect area)
        {
            Vector2 p = unit.transform.position;
            var closest = new Vector2(Mathf.Clamp(p.x, area.xMin, area.xMax), Mathf.Clamp(p.y, area.yMin, area.yMax));
            return Vector2.Distance(p, closest);
        }

        /// <summary>
        /// Call every frame. True when the unit is within reach of the area (the unit is then stopped);
        /// false while walking. <paramref name="failed"/> becomes true when the target cannot be reached.
        /// </summary>
        public bool Tick(Unit unit, Rect area, float reach, out bool failed, WorldObject target = null)
        {
            failed = false;
            float distance = DistanceTo(unit, area);
            if (distance <= reach)
            {
                if (unit.IsMoving) unit.Stop();
                return true;
            }
            if (unit.IsMoving) return false;

            if (++attempts > MaxAttempts)
            {
                failed = true;
                return false;
            }
            // Close by: walk straight at it (collision stops at the side). Far: walk to the nearest free side.
            if (!straightTried && distance < 2.5f)
            {
                straightTried = true;
                unit.MoveStraight(area.center);
            }
            else
            {
                straightTried = false;
                unit.MoveTo(NearestSide(unit, area, target));
            }
            return false;
        }

        /// <summary>Centre of the walkable cell around the target that is closest to the unit.</summary>
        static Vector2 NearestSide(Unit unit, Rect area, WorldObject target)
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return area.center;
            var footprint = target != null
                ? target.Area
                : new RectInt(Mathf.FloorToInt(area.xMin), Mathf.FloorToInt(area.yMin),
                              Mathf.Max(1, Mathf.CeilToInt(area.width)), Mathf.Max(1, Mathf.CeilToInt(area.height)));
            Vector2 from = unit.transform.position;
            Vector2 best = area.center;
            float bestDistance = float.MaxValue;
            for (int r = 1; r <= 2 && bestDistance == float.MaxValue; r++)
            for (int y = footprint.yMin - r; y < footprint.yMax + r; y++)
            for (int x = footprint.xMin - r; x < footprint.xMax + r; x++)
            {
                bool ring = x == footprint.xMin - r || x == footprint.xMax + r - 1 || y == footprint.yMin - r || y == footprint.yMax + r - 1;
                if (!ring) continue;
                var cell = new Vector2Int(x, y);
                if (!map.IsWalkable(cell)) continue;
                var center = MapData.CellCenter(cell);
                float d = (center - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = center;
                }
            }
            return best;
        }

        public static float Reach(Unit unit)
        {
            var map = MapData.Instance;
            if (map == null) return 0.95f;
            return map.Interaction.interactRange + map.Movement.characterCollisionRadius;
        }
    }
}
