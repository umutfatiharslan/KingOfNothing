using System;
using System.Collections.Generic;
using KingOfNothing.World.Generation;
using KingOfNothing.World.Navigation;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Runtime access to the generated map: terrain and biome of every cell, walkability, movement speed,
    /// snow rules and which object stands on which cell. Filled by MapGenerator.
    /// World position (x, y) lies in cell (floor(x), floor(y)); 1 cell = 1 unit = 64 px.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapData : MonoBehaviour
    {
        public static MapData Instance { get; private set; }

        [SerializeField] int width;
        [SerializeField] int height;
        [SerializeField] List<BiomeDefinition> biomes = new List<BiomeDefinition>();

        [Header("Movement")]
        [Tooltip("Characters can walk this many cells into the water (counted from the shore).")]
        [Min(0)] [SerializeField] int wadeableWaterDepth = 5;
        [Tooltip("Speed multiplier while wading in shallow water.")]
        [Range(0.05f, 1f)] [SerializeField] float shallowWaterSpeed = 0.5f;
        [Tooltip("Deeper water needs a boat (a craftable item planned for later). Turn on to let characters walk everywhere for tests.")]
        [SerializeField] bool deepWaterWalkable;
        [Tooltip("Speed multiplier in deep water when Deep Water Walkable is on.")]
        [Range(0.05f, 1f)] [SerializeField] float deepWaterSpeed = 0.35f;

        // Only filled when the generator is told to keep its result in the scene
        [SerializeField, HideInInspector] byte[] savedTerrain;
        [SerializeField, HideInInspector] short[] savedBiomes;

        [NonSerialized] CellTerrain[] terrain;
        [NonSerialized] short[] biomeIndex;
        [NonSerialized] bool[] snowBlocked;
        [NonSerialized] WorldObject[] objects;
        /// <summary>Distance (cells) of every water cell to the nearest land; 0 on land.</summary>
        [NonSerialized] byte[] waterDepth;
        [NonSerialized] GridPathfinder pathfinder;

        /// <summary>Raised after Initialize (a new map is ready).</summary>
        public event Action<MapData> Initialized;

        public int Width => width;
        public int Height => height;
        public bool IsReady => terrain != null;
        public IReadOnlyList<BiomeDefinition> Biomes => biomes;
        public int WadeableWaterDepth => wadeableWaterDepth;

        /// <summary>A* pathfinder on this map (created on first use).</summary>
        public GridPathfinder Pathfinder
        {
            get
            {
                if (pathfinder == null && IsReady) pathfinder = new GridPathfinder(this);
                return pathfinder;
            }
        }

        void Awake()
        {
            Instance = this;
            if (terrain == null && savedTerrain != null && savedTerrain.Length == width * height && width > 0)
                LoadSaved();
            if (height > 0) MapSorting.Configure(height);
        }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <param name="persist">Also store the data in serialized fields so it is saved with the scene.</param>
        public void Initialize(MapLayout layout, IReadOnlyList<BiomeDefinition> biomeDefinitions, bool persist)
        {
            width = layout.Width;
            height = layout.Height;
            biomes = new List<BiomeDefinition>(biomeDefinitions);
            terrain = (CellTerrain[])layout.Terrain.Clone();
            biomeIndex = (short[])layout.Biome.Clone();
            objects = new WorldObject[width * height];
            snowBlocked = null;
            pathfinder = null;
            BuildWaterDepth();
            MapSorting.Configure(height);
            Instance = this;

            if (persist)
            {
                savedTerrain = new byte[terrain.Length];
                for (int i = 0; i < terrain.Length; i++) savedTerrain[i] = (byte)terrain[i];
                savedBiomes = (short[])biomeIndex.Clone();
            }
            else
            {
                savedTerrain = null;
                savedBiomes = null;
            }
            Initialized?.Invoke(this);
        }

        public void Clear()
        {
            terrain = null;
            biomeIndex = null;
            objects = null;
            snowBlocked = null;
            waterDepth = null;
            pathfinder = null;
            savedTerrain = null;
            savedBiomes = null;
        }

        void LoadSaved()
        {
            terrain = new CellTerrain[savedTerrain.Length];
            for (int i = 0; i < terrain.Length; i++) terrain[i] = (CellTerrain)savedTerrain[i];
            biomeIndex = savedBiomes != null && savedBiomes.Length == terrain.Length ? (short[])savedBiomes.Clone() : new short[terrain.Length];
            objects = new WorldObject[terrain.Length];
            BuildWaterDepth();
        }

        void BuildWaterDepth()
        {
            int n = width * height;
            waterDepth = new byte[n];
            var queue = new Queue<int>();
            var visited = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (terrain[i] != CellTerrain.Land) continue;
                visited[i] = true;
                queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % width, y = i / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int j = nx + ny * width;
                    if (visited[j]) continue;
                    visited[j] = true;
                    waterDepth[j] = (byte)Math.Min(255, waterDepth[i] + 1);
                    queue.Enqueue(j);
                }
            }
        }

        // ------------------------------------------------------------------ cells

        public static Vector2Int WorldToCell(Vector2 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        public static Vector2 CellCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);

        public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

        int Index(Vector2Int cell) => cell.x + cell.y * width;

        /// <summary>Terrain of a cell; outside the map counts as sea.</summary>
        public CellTerrain GetTerrain(Vector2Int cell) =>
            terrain != null && InBounds(cell) ? terrain[Index(cell)] : CellTerrain.Sea;

        public CellTerrain GetTerrain(Vector2 world) => GetTerrain(WorldToCell(world));

        /// <summary>Biome index (into Biomes) or -1 for sea, lakes and rivers.</summary>
        public int GetBiomeIndex(Vector2Int cell) =>
            biomeIndex != null && InBounds(cell) ? biomeIndex[Index(cell)] : MapLayout.NoBiome;

        public BiomeDefinition GetBiome(Vector2Int cell)
        {
            int b = GetBiomeIndex(cell);
            return b >= 0 && b < biomes.Count ? biomes[b] : null;
        }

        public BiomeDefinition GetBiome(Vector2 world) => GetBiome(WorldToCell(world));

        public bool IsLand(Vector2Int cell) => GetTerrain(cell) == CellTerrain.Land;

        /// <summary>Distance of a water cell to the shore (1 = touching land); 0 on land, 255 outside the map.</summary>
        public int WaterDepth(Vector2Int cell) => waterDepth != null && InBounds(cell) ? waterDepth[Index(cell)] : 255;

        public bool IsWater(Vector2Int cell) => GetTerrain(cell) != CellTerrain.Land;

        /// <summary>Land, or water at most Wadeable Water Depth cells from the shore (deeper water needs a boat).</summary>
        public bool IsWalkable(Vector2Int cell)
        {
            if (terrain == null || !InBounds(cell)) return false;
            int i = Index(cell);
            if (terrain[i] == CellTerrain.Land) return true;
            return deepWaterWalkable || waterDepth[i] <= wadeableWaterDepth;
        }

        public bool IsWalkable(Vector2 world) => IsWalkable(WorldToCell(world));

        /// <summary>Movement speed multiplier of a cell: biome speed on land (swamp 0.25), shallow water speed in water.</summary>
        public float SpeedMultiplierAt(Vector2Int cell)
        {
            if (terrain == null || !InBounds(cell)) return 1f;
            int i = Index(cell);
            var biome = GetBiome(cell);
            float biomeSpeed = biome != null ? biome.MovementSpeedMultiplier : 1f;
            if (terrain[i] == CellTerrain.Land) return biomeSpeed;
            float water = waterDepth[i] <= wadeableWaterDepth ? shallowWaterSpeed : deepWaterSpeed;
            // Pools inside a biome (swamp marsh) are never faster than the biome itself
            return Mathf.Min(water, biomeSpeed);
        }

        public float SpeedMultiplierAt(Vector2 world) => SpeedMultiplierAt(WorldToCell(world));

        // ------------------------------------------------------------------ snow

        /// <summary>
        /// Rule: no snow (and no snowy assets) in and around the desert. A cell is blocked if it lies in a biome
        /// that forbids snow or within that biome's repel distance.
        /// </summary>
        public bool CanSnowAt(Vector2Int cell)
        {
            if (terrain == null || !InBounds(cell)) return false;
            if (snowBlocked == null) BuildSnowMask();
            return !snowBlocked[Index(cell)];
        }

        void BuildSnowMask()
        {
            int n = width * height;
            snowBlocked = new bool[n];
            var dist = new int[n];
            var queue = new Queue<int>();
            for (int b = 0; b < biomes.Count; b++)
            {
                var biome = biomes[b];
                if (biome == null || !biome.ForbidsSnow) continue;
                int radius = biome.Generation != null ? biome.Generation.repelDistance : 0;

                queue.Clear();
                for (int i = 0; i < n; i++)
                {
                    dist[i] = int.MaxValue;
                    if (biomeIndex[i] == b)
                    {
                        dist[i] = 0;
                        snowBlocked[i] = true;
                        queue.Enqueue(i);
                    }
                }
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    int d = dist[i] + 1;
                    if (d > radius) continue;
                    int x = i % width, y = i / width;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + GridAlgorithms.Dx8[k], ny = y + GridAlgorithms.Dy8[k];
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int j = nx + ny * width;
                        if (dist[j] <= d) continue;
                        dist[j] = d;
                        snowBlocked[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ objects

        public void RegisterObject(WorldObject worldObject)
        {
            if (objects == null || worldObject == null) return;
            var area = worldObject.Area;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (InBounds(c)) objects[Index(c)] = worldObject;
            }
        }

        public void UnregisterObject(WorldObject worldObject)
        {
            if (objects == null || worldObject == null) return;
            var area = worldObject.Area;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (InBounds(c) && objects[Index(c)] == worldObject) objects[Index(c)] = null;
            }
        }

        public WorldObject GetObjectAt(Vector2Int cell) => objects != null && InBounds(cell) ? objects[Index(cell)] : null;

        /// <summary>
        /// Can a building / block be placed on these cells? Needs land everywhere and no object that blocks
        /// building (trees, stumps, stones...). A cut tree's stump must be removed first.
        /// </summary>
        public bool CanPlaceStructure(RectInt area)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!IsLand(c)) return false;
                var o = GetObjectAt(c);
                if (o != null && o.BlocksBuilding) return false;
            }
            return true;
        }
    }
}
