using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Base class of every object standing on the map grid (trees, stones, mines, decorations, later buildings).
    /// Knows the cells it covers and registers itself in MapData, which is what placement rules use
    /// ("nothing can be built on a tree or a stump").
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class WorldObject : MonoBehaviour
    {
        [SerializeField] SpawnableDefinition definition;
        [SerializeField] Vector2Int cell;
        [SerializeField] Vector2Int footprint = Vector2Int.one;
        [Tooltip("Buildings cannot be placed on this object's cells.")]
        [SerializeField] bool blocksBuilding = true;

        MapData registeredIn;

        public SpawnableDefinition Definition => definition;
        /// <summary>Bottom-left cell of the footprint.</summary>
        public Vector2Int Cell => cell;
        public Vector2Int Footprint => footprint;
        public RectInt Area => new RectInt(cell, footprint);
        public bool BlocksBuilding => blocksBuilding;
        public virtual string DisplayName => definition != null ? definition.DisplayName : name;

        /// <summary>Called by the factory right after the object is created.</summary>
        public virtual void Initialize(SpawnableDefinition source, Vector2Int originCell, MapData map)
        {
            definition = source;
            cell = originCell;
            if (source != null)
            {
                footprint = source.Footprint;
                blocksBuilding = source.BlocksBuilding;
            }
            Register(map);
        }

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
            map.RegisterObject(this);
            registeredIn = map;
        }

        protected void Unregister()
        {
            if (registeredIn == null) return;
            registeredIn.UnregisterObject(this);
            registeredIn = null;
        }

        /// <summary>Removes the object from the map and destroys it.</summary>
        public virtual void Remove()
        {
            Unregister();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
