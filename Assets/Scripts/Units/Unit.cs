using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// A unit that can be selected and moved. The object's position is the point where the feet touch the
    /// ground; the visual (body) is a child slightly above it. Movement follows a path from the map's
    /// pathfinder, so units walk around deep water and solid objects instead of getting stuck; terrain and
    /// walk-through objects change the speed (swamp x0.25, shallow water x0.5, bush x0.7 - set in the biomes,
    /// the MovementSettings asset and the object assets). The hitboxes of solid objects near the unit
    /// (CharacterProximitySystem) stop it and make it slide along them.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Unit : MonoBehaviour
    {
        public static readonly List<Unit> All = new List<Unit>();

        [Header("Movement")]
        [Min(0f)] [SerializeField] float moveSpeed = 3f;
        [Tooltip("Use the movement multiplier of the terrain under the unit (swamp, shallow water...).")]
        [SerializeField] bool useTerrainSpeed = true;
        [Tooltip("Use the map pathfinder (off = walk in a straight line).")]
        [SerializeField] bool usePathfinding = true;
        [Tooltip("Collide with the hitboxes of solid objects (trees, stones...).")]
        [SerializeField] bool collideWithObjects = true;
        [Tooltip("How far the body sinks while wading in water (units), so it looks like walking in water.")]
        [Min(0f)] [SerializeField] float waterSinkDepth = 0.25f;

        [Header("Visual")]
        [SerializeField] SpriteRenderer body;
        [Tooltip("All pawn animations (idle, run, tools, carrying). Empty = only the two frame lists below.")]
        [SerializeField] PawnAnimationSet animations;
        [SerializeField] Sprite[] idleFrames;
        [SerializeField] Sprite[] runFrames;
        [Min(0f)] [SerializeField] float fps = 10f;

        [Header("Selection ring")]
        [SerializeField] Color selectionColor = new Color(0.45f, 1f, 0.45f, 0.95f);
        [SerializeField] Vector2 selectionSize = new Vector2(0.9f, 0.45f);

        public bool IsSelected { get; private set; }
        /// <summary>The villager component on the same object (null for plain units).</summary>
        public KingOfNothing.Characters.Villager Villager { get; private set; }
        public bool IsMoving { get; private set; }
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0f, value);
        }

        SpriteRenderer ring;
        Vector2 target;
        Sprite[] currentFrames;
        float animationTime;
        readonly List<Vector2> path = new List<Vector2>();
        int pathIndex;
        Vector3 bodyBasePosition;
        float currentSink;
        Vector2 finalDestination;
        float bestWaypointDistance;
        float lastProgressTime;
        int repathAttempts;

        /// <summary>Remaining waypoints (world positions); the last one is the destination.</summary>
        public IReadOnlyList<Vector2> Path => path;
        public int PathIndex => pathIndex;
        public Vector2 Destination => path.Count > 0 ? path[path.Count - 1] : target;
        /// <summary>Speed multipliers from traits etc. (1 = normal).</summary>
        public float SpeedModifier { get; set; } = 1f;
        /// <summary>Speed multiplier of the current work (carrying something = 0.85...).</summary>
        public float WorkSpeedModifier { get; set; } = 1f;
        /// <summary>Sorting order of the body (objects carried by the unit are drawn just above it).</summary>
        public int SortingOrder => body != null ? body.sortingOrder : MapSorting.Order(transform.position.x, transform.position.y);
        /// <summary>Jobs (hit, carry...) of this unit.</summary>
        public UnitWorker Worker { get; private set; }
        /// <summary>Loot the unit holds in its hands ("Carry loot"), kept across move orders.</summary>
        public LootCarrier Carrier { get; private set; }
        /// <summary>Sprite renderer of the body.</summary>
        public SpriteRenderer Body => body;

        void OnEnable()
        {
            All.Add(this);
            CharacterProximitySystem.Ensure();
        }
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            if (body == null) body = GetComponentInChildren<SpriteRenderer>();
            if (body != null) bodyBasePosition = body.transform.localPosition;
            target = transform.position;
            if (GetComponent<UnitPathIndicator>() == null) gameObject.AddComponent<UnitPathIndicator>();
            Worker = GetComponent<UnitWorker>();
            if (Worker == null) Worker = gameObject.AddComponent<UnitWorker>();
            Villager = GetComponent<KingOfNothing.Characters.Villager>();
            Carrier = GetComponent<LootCarrier>();
            if (Carrier == null) Carrier = gameObject.AddComponent<LootCarrier>();
            CreateSelectionRing();
            SetSelected(false);
            // Units should not animate in sync
            animationTime = Random.Range(0f, 1f);
        }

        /// <summary>Used when a unit is built from code instead of a prefab.</summary>
        public void SetVisuals(SpriteRenderer newBody, Sprite[] idle, Sprite[] run)
        {
            body = newBody;
            idleFrames = idle;
            runFrames = run;
        }

        public PawnAnimationSet Animations
        {
            get => animations;
            set => animations = value;
        }

        // ------------------------------------------------------------------ work animation state

        InteractionTool workTool;
        bool working;
        float swingStart = -1f, swingDuration;
        ResourceType carrying;

        /// <summary>Tool of the last action (its idle / run animations stay until the next order).</summary>
        public InteractionTool WorkTool => workTool;

        /// <summary>Back to the plain animations (Pawn_Idle / Pawn_Run): a move order without an action.</summary>
        public void ClearWorkAnimation()
        {
            SetWorkAnimation(InteractionTool.Auto, false);
            carrying = ResourceType.None;
        }

        /// <summary>Tool in hand (walking to / standing at an object) and whether the unit is working with it.</summary>
        public void SetWorkAnimation(InteractionTool tool, bool atWork)
        {
            workTool = tool;
            working = atWork && tool != InteractionTool.Auto;
            if (!working) swingStart = -1f;
        }

        Sprite[] swingFrames;
        bool alternateSwing;

        /// <summary>
        /// Plays the interact (work / attack) animation of the current tool once, over the given time (one hit).
        /// The direction picks directional attacks (lancer) and turns the body; warriors alternate their two attacks.
        /// </summary>
        public void PlaySwing(float duration, Vector2 direction = default)
        {
            swingStart = Time.time;
            swingDuration = Mathf.Max(0.05f, duration);
            var tool = animations != null ? animations.ForTool(workTool) : null;
            swingFrames = tool != null ? tool.InteractFor(direction, alternateSwing) : null;
            alternateSwing = !alternateSwing;
            if (direction.sqrMagnitude > 0.0001f) Face(direction);
        }

        /// <summary>What the unit carries (wood / gold / meat have their own pawn sprites; None = nothing or another object).</summary>
        public void SetCarrying(ResourceType resource) => carrying = resource;

        /// <summary>True if the pawn sprite itself shows this carried resource (the object is hidden then).</summary>
        public bool ShowsCarried(ResourceType resource) => animations != null && animations.Carrying(resource, false) != null;

        // ------------------------------------------------------------------ buildings (inside / on a tower)

        /// <summary>Building the unit is inside of or standing on (null = outside).</summary>
        public IUnitHost Host { get; private set; }
        /// <summary>Inside a building: invisible, cannot be clicked, does not collide.</summary>
        public bool IsHidden { get; private set; }
        /// <summary>Standing on top of a tower: visible and selectable but does not walk.</summary>
        public bool IsPerched { get; private set; }
        int sortingOverride;

        /// <summary>Called by a building: the unit goes inside (hidden) or on top of it (perched at the given point).</summary>
        public void EnterHost(IUnitHost host, bool hidden, Vector2 perchPoint, int perchSortingOrder)
        {
            Worker?.ClearJobs();
            Stop();
            Host = host;
            IsHidden = hidden;
            IsPerched = !hidden;
            if (IsPerched)
            {
                transform.position = new Vector3(perchPoint.x, perchPoint.y, transform.position.z);
                sortingOverride = perchSortingOrder;
            }
            SetVisible(!hidden);
            if (hidden && IsSelected) SelectionManager.Instance?.Deselect(this);
        }

        /// <summary>Draw order while standing on a tower (just above the tower).</summary>
        public void SetPerchSortingOrder(int order) => sortingOverride = order;

        /// <summary>Called by the building when the unit comes out (at the door / at the foot of the tower).</summary>
        public void LeaveHost(Vector2 exitPoint)
        {
            if (Host == null) return;
            Host = null;
            IsHidden = false;
            IsPerched = false;
            transform.position = new Vector3(exitPoint.x, exitPoint.y, transform.position.z);
            target = exitPoint;
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != ring) r.enabled = visible;
            if (ring != null) ring.enabled = visible && IsSelected;
            var hit = GetComponent<Collider2D>();
            if (hit != null) hit.enabled = visible;
            if (body != null) body.color = Color.white;
        }

        /// <summary>Leaves a building before walking somewhere else.</summary>
        void LeaveHostForMove()
        {
            if (Host != null) Host.Release(this);
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (ring != null) ring.enabled = selected;
        }

        public void MoveTo(Vector2 destination)
        {
            LeaveHostForMove();
            repathAttempts = 0;
            PlanPath(destination);
        }

        void PlanPath(Vector2 destination)
        {
            finalDestination = destination;
            target = destination;
            ResetProgress();
            path.Clear();
            pathIndex = 0;
            var map = MapData.Instance;
            if (usePathfinding && map != null && map.IsReady && map.Pathfinder != null)
            {
                if (!map.Pathfinder.FindPath(transform.position, destination, path) || path.Count == 0)
                {
                    Stop();
                    return;
                }
            }
            else path.Add(destination);
            IsMoving = true;
        }

        /// <summary>Walks in a straight line (short distances next to a target; collisions still apply).</summary>
        public void MoveStraight(Vector2 destination)
        {
            LeaveHostForMove();
            repathAttempts = 0;
            finalDestination = destination;
            target = destination;
            ResetProgress();
            path.Clear();
            pathIndex = 0;
            path.Add(destination);
            IsMoving = true;
        }

        /// <summary>Turns the body towards a direction.</summary>
        public void Face(Vector2 direction)
        {
            if (body != null && Mathf.Abs(direction.x) > 0.01f) body.flipX = direction.x < 0;
        }

        float lungeTime;
        Vector2 lungeDirection;
        float lungeDistance;
        const float LungeDuration = 0.22f;

        /// <summary>Short lean of the body towards a direction (hitting something).</summary>
        public void PlayLunge(Vector2 direction, float distance)
        {
            Face(direction);
            lungeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
            lungeDistance = distance;
            lungeTime = LungeDuration;
        }

        public void Stop()
        {
            target = transform.position;
            path.Clear();
            pathIndex = 0;
            IsMoving = false;
        }

        void Update()
        {
            if (IsMoving && Host == null) Move();
            Animate();
            UpdateWading();
            UpdateSorting();
        }

        void Move()
        {
            if (pathIndex >= path.Count)
            {
                IsMoving = false;
                return;
            }
            Vector2 position = transform.position;
            Vector2 waypoint = path[pathIndex];
            Vector2 toWaypoint = waypoint - position;
            float terrain = useTerrainSpeed && MapData.Instance != null && MapData.Instance.IsReady ? MapData.Instance.SpeedMultiplierAt(position) : 1f;
            float step = moveSpeed * SpeedModifier * WorkSpeedModifier * terrain * Time.deltaTime;

            if (toWaypoint.magnitude <= step)
            {
                if (KeepOnWalkableGround(position, waypoint) != waypoint)
                {
                    // The waypoint lies in deep water (a straight walk next to a bridge): stop at the edge
                    Stop();
                    return;
                }
                transform.position = waypoint;
                pathIndex++;
                repathAttempts = 0;
                ResetProgress();
                if (pathIndex >= path.Count)
                {
                    target = waypoint;
                    IsMoving = false;
                }
                return;
            }
            Vector2 next = position + toWaypoint.normalized * step;
            if (collideWithObjects) next = ResolveCollisions(next);
            next = KeepOnWalkableGround(position, next);
            transform.position = next;
            if (body != null && Mathf.Abs(toWaypoint.x) > 0.01f) body.flipX = toWaypoint.x < 0;
            CheckStuck(Vector2.Distance(next, waypoint));
        }

        // ------------------------------------------------------------------ collisions

        /// <summary>
        /// Characters never step from walkable ground into a cell they cannot walk on (deep water more than the
        /// wadeable depth from the shore, e.g. off the side of a bridge far out at sea). They slide along the edge
        /// instead. A character already standing on such a cell (its bridge was destroyed) may walk out.
        /// </summary>
        Vector2 KeepOnWalkableGround(Vector2 from, Vector2 to)
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return to;
            var toCell = MapData.WorldToCell(to);
            if (map.IsTerrainWalkable(toCell)) return to;
            if (!map.IsTerrainWalkable(MapData.WorldToCell(from))) return to;
            var alongX = new Vector2(to.x, from.y);
            if (map.IsTerrainWalkable(MapData.WorldToCell(alongX))) return alongX;
            var alongY = new Vector2(from.x, to.y);
            if (map.IsTerrainWalkable(MapData.WorldToCell(alongY))) return alongY;
            return from;
        }

        /// <summary>Pushes the feet (a circle) out of the hitboxes of the solid objects near the unit.</summary>
        Vector2 ResolveCollisions(Vector2 p)
        {
            var system = CharacterProximitySystem.Instance;
            var map = MapData.Instance;
            if (system == null || map == null) return p;
            float radius = map.Movement.characterCollisionRadius;
            var solids = system.SolidsNear(this);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < solids.Count; i++)
                {
                    var o = solids[i];
                    if (o == null) continue;
                    var box = o.Hitbox;
                    var closest = new Vector2(Mathf.Clamp(p.x, box.xMin, box.xMax), Mathf.Clamp(p.y, box.yMin, box.yMax));
                    var away = p - closest;
                    float distance = away.magnitude;
                    if (distance >= radius) continue;
                    if (distance > 0.0001f)
                    {
                        p += away / distance * (radius - distance);
                        continue;
                    }
                    // Centre inside the box: leave by the shortest side
                    float left = p.x - box.xMin, right = box.xMax - p.x, down = p.y - box.yMin, up = box.yMax - p.y;
                    float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
                    if (min == left) p.x = box.xMin - radius;
                    else if (min == right) p.x = box.xMax + radius;
                    else if (min == down) p.y = box.yMin - radius;
                    else p.y = box.yMax + radius;
                }
            }
            return p;
        }

        void ResetProgress()
        {
            bestWaypointDistance = float.MaxValue;
            lastProgressTime = Time.time;
        }

        /// <summary>No progress towards the waypoint for a while (an object appeared on the way): plan again.</summary>
        void CheckStuck(float waypointDistance)
        {
            if (waypointDistance < bestWaypointDistance - 0.02f)
            {
                bestWaypointDistance = waypointDistance;
                lastProgressTime = Time.time;
                return;
            }
            var map = MapData.Instance;
            float limit = map != null ? map.Movement.stuckRepathTime : 0.75f;
            if (Time.time - lastProgressTime < limit) return;
            if (++repathAttempts > 3)
            {
                Stop();
                return;
            }
            PlanPath(finalDestination);
        }

        /// <summary>Sinks the body a little while the unit stands in water, and plays the hit lunge.</summary>
        void UpdateWading()
        {
            if (body == null) return;
            var map = MapData.Instance;
            var feetCell = MapData.WorldToCell(transform.position);
            bool inWater = waterSinkDepth > 0f && !IsPerched && map != null && map.IsReady && map.IsWater(feetCell) && !map.IsBridge(feetCell);
            float wanted = inWater ? waterSinkDepth : 0f;
            bool lunging = lungeTime > 0f;
            if (Mathf.Approximately(currentSink, wanted) && !lunging && !lungedLastFrame) return;
            currentSink = Mathf.MoveTowards(currentSink, wanted, Time.deltaTime * 2f);

            Vector3 lunge = Vector3.zero;
            if (lunging)
            {
                lungeTime = Mathf.Max(0f, lungeTime - Time.deltaTime);
                float t = 1f - lungeTime / LungeDuration;
                lunge = (Vector3)(lungeDirection * (Mathf.Sin(t * Mathf.PI) * lungeDistance));
            }
            lungedLastFrame = lunging;
            body.transform.localPosition = bodyBasePosition - new Vector3(0f, currentSink, 0f) + lunge;
        }

        bool lungedLastFrame;

        void Animate()
        {
            if (body == null) return;
            // One swing of the interact animation per hit
            if (animations != null && working && swingStart >= 0f)
            {
                var tool = animations.ForTool(workTool);
                var swing = swingFrames != null && swingFrames.Length > 0 ? swingFrames : tool != null ? tool.interact : null;
                if (swing != null && swing.Length > 0)
                {
                    float t = (Time.time - swingStart) / swingDuration;
                    if (t < 1f)
                    {
                        currentFrames = swing;
                        body.sprite = swing[Mathf.Clamp((int)(t * swing.Length), 0, swing.Length - 1)];
                        return;
                    }
                }
            }

            var frames = PickLoopFrames();
            if (frames == null || frames.Length == 0) return;
            if (frames != currentFrames)
            {
                currentFrames = frames;
                animationTime = 0f;
            }
            animationTime += Time.deltaTime;
            float speed = animations != null ? animations.framesPerSecond : fps;
            body.sprite = frames[(int)(animationTime * speed) % frames.Length];
        }

        Sprite[] PickLoopFrames()
        {
            if (animations == null)
                return IsMoving && runFrames != null && runFrames.Length > 0 ? runFrames : idleFrames;

            var carried = animations.Carrying(carrying, IsMoving);
            if (carried != null) return carried;
            var tool = animations.ForTool(workTool);
            if (tool != null)
            {
                var toolFrames = IsMoving ? tool.run : tool.idle;
                if (toolFrames != null && toolFrames.Length > 0) return toolFrames;
            }
            var basic = IsMoving ? animations.run : animations.idle;
            if (basic != null && basic.Length > 0) return basic;
            return IsMoving && runFrames != null && runFrames.Length > 0 ? runFrames : idleFrames;
        }

        void UpdateSorting()
        {
            int order = IsPerched ? sortingOverride : MapSorting.Order(transform.position.x, transform.position.y);
            if (body != null) body.sortingOrder = order;
            if (ring != null) ring.sortingOrder = order - 1;
        }

        // ------------------------------------------------------------------ selection ring

        static Sprite ringSprite;

        /// <summary>Shared white elliptic ring sprite (selection ring, destination markers).</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (ringSprite == null) ringSprite = BuildEllipseRingSprite(64, 32, 3);
                return ringSprite;
            }
        }

        void CreateSelectionRing()
        {
            var go = KingOfNothing.Utilities.RuntimeChild.Replace(transform, "Selection Ring");
            go.transform.localScale = new Vector3(selectionSize.x, selectionSize.y * 2f, 1f);
            ring = go.AddComponent<SpriteRenderer>();
            ring.sprite = RingSprite;
            ring.color = selectionColor;
        }

        /// <summary>White elliptic ring; 64 px = 1 unit wide.</summary>
        static Sprite BuildEllipseRingSprite(int width, int height, float thickness)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float rx = width / 2f - 1f, ry = height / 2f - 1f;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = (x + 0.5f - width / 2f) / rx;
                float dy = (y + 0.5f - height / 2f) / ry;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Approximate pixel distance to the ring
                float distancePx = Mathf.Abs(d - 1f) * Mathf.Min(rx, ry) * 1.6f;
                float alpha = Mathf.Clamp01(thickness / 2f + 0.5f - distancePx);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
        }
    }
}
