using System;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Base class of every object standing on the map grid (trees, stones, mines, decorations, later buildings).
    /// Knows the cells it covers and registers itself in MapData, which is what placement rules use
    /// ("nothing can be built on a tree or a stump"). Every object has a unique id (Uid) on its map; the edited
    /// map file refers to objects by it. Creating, removing and moving raise static events, which is how
    /// every change of the map is recorded no matter who made it.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class WorldObject : MonoBehaviour
    {
        [SerializeField] SpawnableDefinition definition;
        [SerializeField] int uid;
        [SerializeField] Vector2Int cell;
        [SerializeField] Vector2 offset;
        [SerializeField] Vector2Int footprint = Vector2Int.one;
        [Tooltip("How many items this object stands for (a pile of 8 wood = 8). 1 for normal objects.")]
        [Min(1)] [SerializeField] int amount = 1;
        [Tooltip("Buildings cannot be placed on this object's cells.")]
        [SerializeField] bool blocksBuilding = true;

        MapData registeredIn;

        /// <summary>A new object is on the map (raised by WorldObjectFactory once it is fully set up).</summary>
        public static event Action<WorldObject> AnyCreated;
        /// <summary>An object is about to leave the map (still registered, all data readable).</summary>
        public static event Action<WorldObject> AnyRemoving;
        /// <summary>An object moved: (object, old cell, new cell).</summary>
        public static event Action<WorldObject, Vector2Int, Vector2Int> AnyMoved;
        /// <summary>The amount of a pile changed: (object, before, after).</summary>
        public static event Action<WorldObject, int, int> AnyAmountChanged;

        public SpawnableDefinition Definition => definition;
        /// <summary>Unique id of the object on its map (0 = not registered yet).</summary>
        public int Uid => uid;
        /// <summary>Bottom-left cell of the footprint.</summary>
        public Vector2Int Cell => cell;
        /// <summary>Small shift of the sprite inside its cell.</summary>
        public Vector2 Offset => offset;
        public Vector2Int Footprint => footprint;
        /// <summary>Number of items of a dropped pile (1 for normal objects).</summary>
        public int Amount
        {
            get => amount;
            set => amount = Mathf.Max(1, value);
        }
        /// <summary>Adds items to a pile on the map (recorded in the edited map).</summary>
        public void AddAmount(int delta)
        {
            if (delta == 0) return;
            int before = amount;
            amount = Mathf.Max(1, amount + delta);
            if (amount != before) AnyAmountChanged?.Invoke(this, before, amount);
        }

        /// <summary>Characters can carry it in Default mode.</summary>
        public bool MovableByCharacters => definition != null && definition.MovableByCharacters;
        /// <summary>Being carried by a character (not on the map grid meanwhile).</summary>
        public bool IsCarried { get; private set; }
        public RectInt Area => new RectInt(cell, footprint);
        public bool BlocksBuilding => blocksBuilding;
        public virtual string DisplayName =>
            (definition != null ? definition.DisplayName : name) + (amount > 1 ? $" x{amount}" : "");

        /// <summary>Characters cannot pass through it (paths go around, the hitbox stops them).</summary>
        public bool BlocksMovement => definition != null && !definition.WalkThrough;
        /// <summary>Speed multiplier for characters inside the object (1 for solid objects).</summary>
        public float WalkThroughSpeed => definition != null ? definition.WalkThroughSpeed : 1f;
        public bool FadesWhenHidingCharacter => definition == null || definition.FadeWhenHidingCharacter;

        /// <summary>Point where the object touches the ground (sorting uses it).</summary>
        public Vector2 GroundPoint => WorldObjectFactory.BasePoint(footprint, cell, offset);

        /// <summary>Solid part of the object in world units, always inside the footprint cells.</summary>
        public Rect Hitbox
        {
            get
            {
                var size = definition != null ? definition.HitboxSize : new Vector2(0.6f, 0.4f);
                var shift = definition != null ? definition.HitboxOffset : new Vector2(0f, -0.1f);
                var ground = GroundPoint;
                float xMin = Mathf.Max(cell.x, ground.x + shift.x - size.x * 0.5f);
                float xMax = Mathf.Min(cell.x + footprint.x, ground.x + shift.x + size.x * 0.5f);
                float yMin = Mathf.Max(cell.y, ground.y + shift.y);
                float yMax = Mathf.Min(cell.y + footprint.y, ground.y + shift.y + size.y);
                return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
            }
        }

        SpriteRenderer[] renderers;

        /// <summary>Sprite renderers of the object (cached).</summary>
        public SpriteRenderer[] Renderers
        {
            get
            {
                if (renderers == null || renderers.Length == 0)
                {
                    var all = GetComponentsInChildren<SpriteRenderer>();
                    var own = new System.Collections.Generic.List<SpriteRenderer>(all.Length);
                    foreach (var r in all)
                        if (r.GetComponent<SpriteEffectMarker>() == null) own.Add(r);
                    renderers = own.ToArray();
                }
                return renderers;
            }
        }

        /// <summary>World-space area covered by the object's sprites.</summary>
        public Bounds VisualBounds
        {
            get
            {
                var r = Renderers;
                if (r.Length == 0) return new Bounds(GroundPoint, Vector3.zero);
                var b = r[0].bounds;
                for (int i = 1; i < r.Length; i++) b.Encapsulate(r[i].bounds);
                return b;
            }
        }

        /// <summary>Another object this one is tied to (the tree a stump grows back into). Null if none.</summary>
        public virtual SpawnableDefinition LinkedDefinition => null;

        public virtual void SetLinkedDefinition(SpawnableDefinition linked) { }

        /// <summary>Called by the factory before Initialize when the object must keep a known id.</summary>
        public void AssignUid(int id) => uid = id;

        /// <summary>Called by the factory right after the object is created.</summary>
        public virtual void Initialize(SpawnableDefinition source, Vector2Int originCell, Vector2 cellOffset, MapData map)
        {
            definition = source;
            cell = originCell;
            offset = cellOffset;
            if (source != null)
            {
                footprint = source.Footprint;
                blocksBuilding = source.BlocksBuilding;
            }
            Register(map);
        }

        internal static void RaiseCreated(WorldObject worldObject) => AnyCreated?.Invoke(worldObject);

        protected virtual void Start()
        {
            // Objects saved in a scene (persistent bake) register when the game starts
            if (registeredIn == null) Register(MapData.Instance);
        }

        protected virtual void OnDestroy() => Unregister();

        void Register(MapData map)
        {
            if (map == null || registeredIn == map) return;
            Unregister();
            uid = map.RegisterObject(this, uid);
            registeredIn = map;
            ApplyDepth();
        }

        /// <summary>Depth step per object id (objects are drawn in front of the tiles at z = 0, the camera is at -10).</summary>
        const float DepthPerUid = 0.00001f;

        /// <summary>
        /// Objects on the same row share a sorting order; without a fixed tie-break the drawing order of two
        /// overlapping objects can change from frame to frame (an animated tree flickering over a stump). A tiny
        /// depth offset by id keeps it stable: the most recently added object is always drawn on top.
        /// </summary>
        void ApplyDepth()
        {
            var p = transform.position;
            float z = -Mathf.Min(uid, 500000) * DepthPerUid;
            if (Mathf.Approximately(p.z, z)) return;
            transform.position = new Vector3(p.x, p.y, z);
        }

        protected void Unregister()
        {
            if (registeredIn == null) return;
            registeredIn.UnregisterObject(this);
            registeredIn = null;
        }

        /// <summary>Moves the object (sprite, sorting and grid cells) so its footprint starts at the given cell.</summary>
        public virtual void MoveTo(Vector2Int newCell)
        {
            if (newCell == cell) return;
            var map = registeredIn != null ? registeredIn : MapData.Instance;
            var from = cell;
            HitShake.Stop(this);
            Unregister();

            var delta = new Vector3(newCell.x - cell.x, newCell.y - cell.y, 0f);
            transform.position += delta;
            cell = newCell;
            var basePoint = WorldObjectFactory.BasePoint(footprint, cell, offset);
            foreach (var r in Renderers)
                r.sortingOrder = MapSorting.Order(basePoint.x, basePoint.y);

            Register(map);
            AnyMoved?.Invoke(this, from, newCell);
        }

        MapData carriedFrom;
        Vector2Int carryStartCell;
        Vector3 spriteOffset;

        /// <summary>Takes the object off the grid so a character can carry it (nothing is recorded until it is put down).</summary>
        public void PickUp()
        {
            if (IsCarried) return;
            carriedFrom = registeredIn != null ? registeredIn : MapData.Instance;
            carryStartCell = cell;
            HitShake.Stop(this);
            spriteOffset = transform.position - (Vector3)GroundPoint;
            Unregister();
            IsCarried = true;
        }

        /// <summary>Position of a carried object: its ground point follows the given world point.</summary>
        public void SetCarriedPosition(Vector2 groundPoint, int sortingOrder)
        {
            if (!IsCarried) return;
            transform.position = (Vector3)groundPoint + spriteOffset;
            foreach (var r in Renderers) r.sortingOrder = sortingOrder;
        }

        /// <summary>Puts a carried object down on a cell; recorded as a move from where it was picked up.</summary>
        public void PutDown(Vector2Int newCell)
        {
            if (!IsCarried) return;
            IsCarried = false;
            cell = newCell;
            var basePoint = GroundPoint;
            transform.position = (Vector3)basePoint + spriteOffset;
            foreach (var r in Renderers) r.sortingOrder = MapSorting.Order(basePoint.x, basePoint.y);
            Register(carriedFrom != null ? carriedFrom : MapData.Instance);
            if (newCell != carryStartCell) AnyMoved?.Invoke(this, carryStartCell, newCell);
        }

        /// <summary>Removes the object from the map and destroys it.</summary>
        public virtual void Remove()
        {
            if (registeredIn != null) AnyRemoving?.Invoke(this);
            Unregister();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
