using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Brings a unit next to a target. Two ways:
    ///  - <see cref="TickWork"/>: to a work spot right beside the object's hitbox, level with its base (left or right
    ///    side, whichever is closer; below / above only if both sides are blocked), so the tool of the work animation
    ///    really touches the object. Several characters on a big object (a building) get different spots.
    ///  - <see cref="Tick"/>: anywhere within a reach of an area (putting objects down, entering a door...).
    /// A new order always wins: the first tick stops whatever walk the unit was still doing (a move order given
    /// just before), so the unit turns towards the new target at once.
    /// Gives up after a few attempts (unreachable target).
    /// </summary>
    public sealed class Approach
    {
        const int MaxAttempts = 5;
        int attempts;
        bool straightTried;
        bool started;
        bool keepCurrentMove;
        bool hasSpot;
        Vector2 spot;

        /// <param name="keepCurrentMove">True if the caller itself just started the unit's walk (it is not interrupted).</param>
        public void Reset(bool keepCurrentMove = false)
        {
            attempts = 0;
            straightTried = false;
            started = false;
            hasSpot = false;
            this.keepCurrentMove = keepCurrentMove;
        }

        /// <summary>The work spot chosen by TickWork (valid after the first tick).</summary>
        public Vector2 Spot => spot;

        void FirstTick(Unit unit)
        {
            if (started) return;
            started = true;
            // Interrupt the previous order half way (e.g. a move order given just before this one)
            if (!keepCurrentMove && unit.IsMoving) unit.Stop();
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
            FirstTick(unit);
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
            // Walk-through targets (loot): walk right onto them instead of around them
            if (target != null && !target.BlocksMovement)
            {
                if (distance < 2.5f) unit.MoveStraight(area.center);
                else unit.MoveTo(area.center);
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

        /// <summary>
        /// Call every frame. True when the unit stands at its work spot beside the hitbox (then stopped and turned
        /// towards the object); false while walking. <paramref name="failed"/> = the object cannot be reached.
        /// </summary>
        public bool TickWork(Unit unit, Rect hitbox, out bool failed, WorldObject target = null)
        {
            failed = false;
            FirstTick(unit);
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            if (!hasSpot)
            {
                spot = WorkSpots.Choose(unit, hitbox, target, out bool found);
                hasSpot = true;
                if (!found)
                {
                    failed = true;
                    return false;
                }
            }

            Vector2 position = unit.transform.position;
            float toSpot = Vector2.Distance(position, spot);
            if (toSpot <= settings.workSpotTolerance)
            {
                if (unit.IsMoving) unit.Stop();
                return true;
            }
            if (unit.IsMoving) return false;

            // Stopped by a collision close to the spot: good enough when the object is in reach
            bool inReach = DistanceTo(unit, hitbox) <= Reach(unit) && WorkSpots.StandingAllowed(unit, target);
            if (attempts > 0 && inReach && toSpot < 0.6f) return true;
            if (++attempts > MaxAttempts)
            {
                if (inReach) return true;
                failed = true;
                return false;
            }
            // Another unit may have taken the spot meanwhile, or it was not reachable: choose again
            if (attempts == 3)
            {
                spot = WorkSpots.Choose(unit, hitbox, target, out bool found);
                if (!found)
                {
                    failed = true;
                    return false;
                }
                toSpot = Vector2.Distance(position, spot);
            }
            if (toSpot < 1.5f) unit.MoveStraight(spot);
            else unit.MoveTo(spot);
            return false;
        }

        /// <summary>Forget the claimed work spot (the unit stops working on the object).</summary>
        public static void Release(Unit unit) => WorkSpots.Release(unit);

        /// <summary>Centre of the walkable cell around the target that is closest to the unit.</summary>
        public static Vector2 NearestSide(Unit unit, Rect area, WorldObject target)
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

    /// <summary>
    /// Work spots beside objects. A spot is level with the object's base (a hair in front of it, so the character
    /// is drawn over the object and its tool stays visible) at the side of the hitbox plus the stand distance.
    /// Small objects offer the left and right side (below / above only as a fallback); big ones (buildings) offer
    /// several spots along both sides and the front. Spots claimed by other working units are skipped.
    /// </summary>
    public static class WorkSpots
    {
        static readonly Dictionary<Unit, Vector2> Claims = new Dictionary<Unit, Vector2>();
        static readonly List<Unit> Stale = new List<Unit>();
        static readonly List<WorldObject> Nearby = new List<WorldObject>();
        struct Candidate
        {
            public Vector2 Point;
            public float Penalty;
        }
        static readonly List<Candidate> Candidates = new List<Candidate>();

        public static void Release(Unit unit)
        {
            if (unit != null) Claims.Remove(unit);
        }

        public static Vector2 Choose(Unit unit, Rect box, WorldObject target) => Choose(unit, box, target, out _);

        /// <param name="found">False if there is no allowed spot at all (a bridge without dry land at its ends).</param>
        public static Vector2 Choose(Unit unit, Rect box, WorldObject target, out bool found)
        {
            found = true;
            var map = MapData.Instance;
            var settings = map != null ? map.Interaction : InteractionSettings.Fallback;
            float radius = map != null ? map.Movement.characterCollisionRadius : 0.2f;
            float gap = Mathf.Max(settings.workStandDistance, radius + 0.03f);
            float baseY = target != null ? target.GroundPoint.y : box.yMin + 0.1f;
            float frontY = Mathf.Clamp(baseY - settings.workStandFrontOffset, box.yMin - gap * 0.5f, box.yMax);

            Candidates.Clear();
            // Bridges are built from the shore (or a finished bridge): never from the water
            bool dryOnly = target is KingOfNothing.World.Buildings.Building building && building.Info != null && building.Info.isBridge;
            bool big = box.width >= 1.5f || box.height >= 1.5f;
            if (dryOnly)
            {
                bool horizontal = box.width >= box.height;
                if (horizontal)
                {
                    Candidates.Add(new Candidate { Point = new Vector2(box.xMin - gap, box.center.y) });
                    Candidates.Add(new Candidate { Point = new Vector2(box.xMax + gap, box.center.y) });
                }
                else
                {
                    Candidates.Add(new Candidate { Point = new Vector2(box.center.x, box.yMin - gap) });
                    Candidates.Add(new Candidate { Point = new Vector2(box.center.x, box.yMax + gap) });
                }
                // A second row for more builders
                int n = Candidates.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = Candidates[i].Point;
                    var away = horizontal ? new Vector2(p.x < box.center.x ? -0.7f : 0.7f, 0f) : new Vector2(0f, p.y < box.center.y ? -0.7f : 0.7f);
                    Candidates.Add(new Candidate { Point = p + away, Penalty = 0.5f });
                }
                // Along both long sides of every cell: a bridge joined from the side (a horizontal bridge on a
                // vertical one) is built from the bridge it touches, like one continuing straight ahead
                int cells = Mathf.Max(1, Mathf.RoundToInt(horizontal ? box.width : box.height));
                for (int i = 0; i < cells; i++)
                {
                    if (horizontal)
                    {
                        float x = box.xMin + i + 0.5f;
                        Candidates.Add(new Candidate { Point = new Vector2(x, box.yMin - gap), Penalty = 0.2f });
                        Candidates.Add(new Candidate { Point = new Vector2(x, box.yMax + gap), Penalty = 0.2f });
                    }
                    else
                    {
                        float y = box.yMin + i + 0.5f;
                        Candidates.Add(new Candidate { Point = new Vector2(box.xMin - gap, y), Penalty = 0.2f });
                        Candidates.Add(new Candidate { Point = new Vector2(box.xMax + gap, y), Penalty = 0.2f });
                    }
                }
            }
            else if (!big)
            {
                Candidates.Add(new Candidate { Point = new Vector2(box.xMin - gap, frontY) });
                Candidates.Add(new Candidate { Point = new Vector2(box.xMax + gap, frontY) });
                Candidates.Add(new Candidate { Point = new Vector2(box.center.x, box.yMin - gap - 0.1f), Penalty = 0.8f });
                Candidates.Add(new Candidate { Point = new Vector2(box.center.x, box.yMax + gap), Penalty = 1.2f });
            }
            else
            {
                // Both sides, from the front corner upwards (front spots preferred: no fading behind the building)
                float step = Mathf.Max(0.3f, settings.workSpotSpacing);
                int row = 0;
                for (float y = frontY; y <= box.yMax - 0.1f || row == 0; y += step, row++)
                {
                    float penalty = row * 0.4f;
                    Candidates.Add(new Candidate { Point = new Vector2(box.xMin - gap, y), Penalty = penalty });
                    Candidates.Add(new Candidate { Point = new Vector2(box.xMax + gap, y), Penalty = penalty });
                }
                // Along the front
                for (float x = box.xMin + step * 0.6f; x <= box.xMax - step * 0.6f; x += step)
                    Candidates.Add(new Candidate { Point = new Vector2(x, box.yMin - gap), Penalty = 0.3f });
            }

            PruneClaims();
            Vector2 from = unit.transform.position;
            Vector2 best = Vector2.zero;
            float bestScore = float.MaxValue;
            for (int pass = 0; pass < 2 && bestScore == float.MaxValue; pass++)
            {
                foreach (var c in Candidates)
                {
                    if (!IsFree(map, c.Point, radius, target)) continue;
                    if (dryOnly && !IsDry(map, c.Point)) continue;
                    if (pass == 0 && IsClaimed(unit, c.Point, settings.workSpotSpacing * 0.9f)) continue;
                    float score = Vector2.Distance(from, c.Point) + c.Penalty;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c.Point;
                    }
                }
            }
            if (bestScore == float.MaxValue)
            {
                if (dryOnly)
                {
                    // Nowhere dry to stand: the job gives up
                    found = false;
                    Claims.Remove(unit);
                    return from;
                }
                best = Approach.NearestSide(unit, box, target);
            }
            Claims[unit] = best;
            return best;
        }

        /// <summary>False while the unit stands in the water next to a bridge (bridges are built from dry land).</summary>
        public static bool StandingAllowed(Unit unit, WorldObject target)
        {
            bool bridge = target is KingOfNothing.World.Buildings.Building b && b.Info != null && b.Info.isBridge;
            return !bridge || IsDry(MapData.Instance, unit.transform.position);
        }

        /// <summary>Land or a finished bridge (no wading).</summary>
        static bool IsDry(MapData map, Vector2 point)
        {
            if (map == null || !map.IsReady) return true;
            var cell = MapData.WorldToCell(point);
            return map.IsLand(cell) || map.IsBridge(cell);
        }

        static void PruneClaims()
        {
            Stale.Clear();
            foreach (var pair in Claims)
                if (pair.Key == null || pair.Key.Worker == null || pair.Key.Worker.Current == null) Stale.Add(pair.Key);
            foreach (var u in Stale) Claims.Remove(u);
        }

        static bool IsClaimed(Unit unit, Vector2 point, float spacing)
        {
            foreach (var pair in Claims)
                if (pair.Key != unit && (pair.Value - point).sqrMagnitude < spacing * spacing) return true;
            return false;
        }

        /// <summary>Walkable cell and no other solid hitbox under the feet.</summary>
        static bool IsFree(MapData map, Vector2 point, float radius, WorldObject target)
        {
            if (map == null || !map.IsReady) return true;
            var cell = MapData.WorldToCell(point);
            if (!map.IsTerrainWalkable(cell)) return false;
            Nearby.Clear();
            map.CollectObjects(new RectInt(cell.x - 1, cell.y - 1, 3, 3), Nearby);
            foreach (var o in Nearby)
            {
                if (o == null || o == target || !o.BlocksMovement) continue;
                var h = o.Hitbox;
                var closest = new Vector2(Mathf.Clamp(point.x, h.xMin, h.xMax), Mathf.Clamp(point.y, h.yMin, h.yMax));
                if ((closest - point).sqrMagnitude < radius * radius) return false;
            }
            return true;
        }
    }
}
