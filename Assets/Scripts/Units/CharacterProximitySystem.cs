using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Physical interaction around the characters. For every character separately it collects the map objects
    /// within the interaction radius (MovementSettings, default 5 cells); only those objects have active hitboxes
    /// for that character, so tens of thousands of trees cost nothing. It also makes objects transparent
    /// (default 50%) while a character walks through them or stands right behind them, and fades them back.
    /// Created automatically by the first Unit; all values are in the MovementSettings asset.
    /// </summary>
    [DefaultExecutionOrder(50)] // after the units moved this frame
    public class CharacterProximitySystem : MonoBehaviour
    {
        sealed class FadeState
        {
            public WorldObject Target;
            public float Alpha = 1f;
            public bool HidesCharacter;
        }

        static CharacterProximitySystem instance;

        readonly Dictionary<Unit, List<WorldObject>> nearbySolids = new Dictionary<Unit, List<WorldObject>>();
        readonly Dictionary<WorldObject, FadeState> fading = new Dictionary<WorldObject, FadeState>();
        readonly HashSet<WorldObject> seen = new HashSet<WorldObject>();
        readonly List<WorldObject> finished = new List<WorldObject>();
        readonly List<Unit> staleUnits = new List<Unit>();
        static readonly List<WorldObject> Empty = new List<WorldObject>();
        float nextScan;

        public static CharacterProximitySystem Instance => instance;

        /// <summary>Makes sure one system exists in the scene.</summary>
        public static CharacterProximitySystem Ensure()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<CharacterProximitySystem>();
            if (instance == null) instance = new GameObject("Character Proximity").AddComponent<CharacterProximitySystem>();
            return instance;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (var state in fading.Values) if (state.Target != null) SetAlpha(state.Target, state.Target.BaseAlpha);
        }

        /// <summary>Solid objects whose hitboxes are active for this character (within the interaction radius).</summary>
        public IReadOnlyList<WorldObject> SolidsNear(Unit unit) =>
            unit != null && nearbySolids.TryGetValue(unit, out var list) ? list : Empty;

        void LateUpdate()
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return;
            var settings = map.Movement;

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + settings.proximityUpdateInterval;
                Scan(map, settings);
            }
            UpdateFades(settings);
        }

        void Scan(MapData map, MovementSettings settings)
        {
            foreach (var state in fading.Values) state.HidesCharacter = false;

            // Forget characters that are gone
            staleUnits.Clear();
            foreach (var unit in nearbySolids.Keys)
                if (unit == null || !unit.isActiveAndEnabled) staleUnits.Add(unit);
            foreach (var unit in staleUnits) nearbySolids.Remove(unit);

            float radius = settings.interactionRadius;
            int r = Mathf.CeilToInt(radius);
            float radiusSq = radius * radius;
            var body = settings.characterBodySize;

            foreach (var unit in Unit.All)
            {
                if (unit == null) continue;
                if (!nearbySolids.TryGetValue(unit, out var solids))
                {
                    solids = new List<WorldObject>();
                    nearbySolids[unit] = solids;
                }
                solids.Clear();
                seen.Clear();
                // Inside a building or on top of a tower: no collisions, nothing fades for it
                if (unit.IsHidden || unit.IsPerched) continue;

                Vector2 feet = unit.transform.position;
                var center = MapData.WorldToCell(feet);
                var bodyRect = new Rect(feet.x - body.x * 0.5f, feet.y, body.x, body.y);

                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > radiusSq) continue;
                    var o = map.GetObjectAt(new Vector2Int(center.x + dx, center.y + dy));
                    if (o == null || !seen.Add(o)) continue;

                    if (o.BlocksMovement) solids.Add(o);
                    if (o.FadesWhenHidingCharacter && HidesCharacter(o, feet, bodyRect)) MarkHiding(o);
                }
            }
        }

        /// <summary>The character is inside the object, or right behind it and covered by its sprite.</summary>
        static bool HidesCharacter(WorldObject o, Vector2 feet, Rect bodyRect)
        {
            // Only the visible pixels count (not the transparent border of the image), so a building fades as soon
            // as the character is right behind it and not while the character is still far behind it
            var visual = o.VisiblePixels;
            if (!visual.Overlaps(bodyRect)) return false;
            bool inside = o.Area.Contains(Vector2Int.FloorToInt(feet));
            bool behind = feet.y > o.GroundPoint.y; // drawn behind the object (sorting by ground point)
            return inside || behind;
        }

        void MarkHiding(WorldObject o)
        {
            if (!fading.TryGetValue(o, out var state))
            {
                state = new FadeState { Target = o, Alpha = o.BaseAlpha };
                fading[o] = state;
            }
            state.HidesCharacter = true;
        }

        void UpdateFades(MovementSettings settings)
        {
            finished.Clear();
            float step = settings.fadeSpeed * Time.deltaTime;
            foreach (var pair in fading)
            {
                var state = pair.Value;
                if (state.Target == null)
                {
                    finished.Add(pair.Key);
                    continue;
                }
                float baseAlpha = state.Target.BaseAlpha;
                float wanted = state.HidesCharacter ? settings.occludedAlpha * baseAlpha : baseAlpha;
                if (!Mathf.Approximately(state.Alpha, wanted))
                {
                    state.Alpha = Mathf.MoveTowards(state.Alpha, wanted, step);
                    SetAlpha(state.Target, state.Alpha);
                }
                if (!state.HidesCharacter && Mathf.Approximately(state.Alpha, baseAlpha)) finished.Add(pair.Key);
            }
            foreach (var o in finished) fading.Remove(o);
        }

        static void SetAlpha(WorldObject o, float alpha)
        {
            if (o == null) return;
            foreach (var r in o.Renderers)
            {
                if (r == null) continue;
                var c = r.color;
                c.a = alpha;
                r.color = c;
            }
        }

        void OnDrawGizmos()
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady || !map.Movement.showHitboxes) return;
            Gizmos.color = map.Movement.hitboxColor;
            foreach (var pair in nearbySolids)
            {
                if (pair.Key == null) continue;
                foreach (var o in pair.Value)
                {
                    if (o == null) continue;
                    var h = o.Hitbox;
                    Gizmos.DrawWireCube(h.center, h.size);
                }
                Gizmos.DrawWireSphere(pair.Key.transform.position, map.Movement.characterCollisionRadius);
            }
        }
    }
}
