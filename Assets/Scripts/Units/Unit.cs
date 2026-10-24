using System.Collections.Generic;
using KingOfNothing.World;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// A unit that can be selected and moved. The object's position is the point where the feet touch the
    /// ground; the visual (body) is a child slightly above it. Movement follows a path from the map's
    /// pathfinder, so units walk around deep water instead of getting stuck at the bank; terrain changes the
    /// speed (swamp x0.25, shallow water x0.5 - set in the biomes and MapData).
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
        [Tooltip("How far the body sinks while wading in water (units), so it looks like walking in water.")]
        [Min(0f)] [SerializeField] float waterSinkDepth = 0.25f;

        [Header("Visual")]
        [SerializeField] SpriteRenderer body;
        [SerializeField] Sprite[] idleFrames;
        [SerializeField] Sprite[] runFrames;
        [Min(0f)] [SerializeField] float fps = 10f;

        [Header("Selection ring")]
        [SerializeField] Color selectionColor = new Color(0.45f, 1f, 0.45f, 0.95f);
        [SerializeField] Vector2 selectionSize = new Vector2(0.9f, 0.45f);

        public bool IsSelected { get; private set; }
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

        /// <summary>Remaining waypoints (world positions); the last one is the destination.</summary>
        public IReadOnlyList<Vector2> Path => path;
        public int PathIndex => pathIndex;
        public Vector2 Destination => path.Count > 0 ? path[path.Count - 1] : target;
        /// <summary>Speed multipliers from traits etc. (1 = normal).</summary>
        public float SpeedModifier { get; set; } = 1f;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            if (body == null) body = GetComponentInChildren<SpriteRenderer>();
            if (body != null) bodyBasePosition = body.transform.localPosition;
            target = transform.position;
            if (GetComponent<UnitPathIndicator>() == null) gameObject.AddComponent<UnitPathIndicator>();
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

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (ring != null) ring.enabled = selected;
        }

        public void MoveTo(Vector2 destination)
        {
            target = destination;
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

        public void Stop()
        {
            target = transform.position;
            path.Clear();
            pathIndex = 0;
            IsMoving = false;
        }

        void Update()
        {
            if (IsMoving) Move();
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
            float step = moveSpeed * SpeedModifier * terrain * Time.deltaTime;

            if (toWaypoint.magnitude <= step)
            {
                transform.position = waypoint;
                pathIndex++;
                if (pathIndex >= path.Count)
                {
                    target = waypoint;
                    IsMoving = false;
                }
                return;
            }
            transform.position = position + toWaypoint.normalized * step;
            if (body != null && Mathf.Abs(toWaypoint.x) > 0.01f) body.flipX = toWaypoint.x < 0;
        }

        /// <summary>Sinks the body a little while the unit stands in water.</summary>
        void UpdateWading()
        {
            if (body == null || waterSinkDepth <= 0f) return;
            var map = MapData.Instance;
            bool inWater = map != null && map.IsReady && map.IsWater(MapData.WorldToCell(transform.position));
            float wanted = inWater ? waterSinkDepth : 0f;
            if (Mathf.Approximately(currentSink, wanted)) return;
            currentSink = Mathf.MoveTowards(currentSink, wanted, Time.deltaTime * 2f);
            body.transform.localPosition = bodyBasePosition - new Vector3(0f, currentSink, 0f);
        }

        void Animate()
        {
            var frames = IsMoving && runFrames != null && runFrames.Length > 0 ? runFrames : idleFrames;
            if (body == null || frames == null || frames.Length == 0) return;

            if (frames != currentFrames)
            {
                currentFrames = frames;
                animationTime = 0f;
            }
            animationTime += Time.deltaTime;
            body.sprite = frames[(int)(animationTime * fps) % frames.Length];
        }

        void UpdateSorting()
        {
            int order = MapSorting.Order(transform.position.x, transform.position.y);
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
            var go = new GameObject("Selection Ring");
            go.transform.SetParent(transform, false);
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
