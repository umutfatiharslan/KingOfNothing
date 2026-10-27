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
        [Tooltip("Water rules, interaction radius, transparency... (asset, so changes made in Play mode are kept). " +
                 "Empty = Assets/Data/World/MovementSettings is used (created automatically in the editor).")]
        [SerializeField] MovementSettings movement;
        [Tooltip("Hitting, carrying, drops, selection colours... Empty = Assets/Data/World/InteractionSettings (created automatically in the editor).")]
        [SerializeField] InteractionSettings interaction;
        [Tooltip("Game day, construction, build menu, combat, class animations... Empty = Assets/Data/World/GameplaySettings (created automatically in the editor).")]
        [SerializeField] GameplaySettings gameplay;

        // Only filled when the generator is told to keep its result in the scene
        [SerializeField, HideInInspector] byte[] savedTerrain;
        [SerializeField, HideInInspector] short[] savedBiomes;

        [NonSerialized] CellTerrain[] terrain;
        [NonSerialized] short[] biomeIndex;
        [NonSerialized] bool[] snowBlocked;
        [NonSerialized] WorldObject[] objects;
        [NonSerialized] Dictionary<int, WorldObject> objectsByUid = new Dictionary<int, WorldObject>();
        /// <summary>Objects hidden under another one on the same cell (developer mode allows overlapping).</summary>
        [NonSerialized] Dictionary<int, List<WorldObject>> buried = new Dictionary<int, List<WorldObject>>();
        [NonSerialized] int nextUid = 1;
        [NonSerialized] bool interactionChecked;
        [NonSerialized] bool movementChecked;
        /// <summary>Distance (cells) of every water cell to the nearest land; 0 on land.</summary>
        [NonSerialized] byte[] waterDepth;
        [NonSerialized] GridPathfinder pathfinder;

        /// <summary>Raised after Initialize (a new map is ready).</summary>
        public event Action<MapData> Initialized;

        public int Width => width;
        public int Height => height;
        public bool IsReady => terrain != null;
        public IReadOnlyList<BiomeDefinition> Biomes => biomes;
        public int WadeableWaterDepth => Movement.wadeableWaterDepth;

        /// <summary>Interaction rules (never null).</summary>
        public InteractionSettings Interaction
        {
            get
            {
#if UNITY_EDITOR
                if (interaction == null) interaction = InteractionSettings.LoadOrCreateDefault();
                if (!interactionChecked && interaction != null)
                {
                    interactionChecked = true;
                    interaction.EditorEnsureDefaults();
                }
#endif
                return interaction != null ? interaction : InteractionSettings.Fallback;
            }
        }

        /// <summary>Kingdom rules: day length, construction, buildings, combat (never null).</summary>
        public GameplaySettings Gameplay
        {
            get
            {
#if UNITY_EDITOR
                if (gameplay == null) gameplay = GameplaySettings.LoadOrCreateDefault();
#endif
                return gameplay != null ? gameplay : GameplaySettings.Fallback;
            }
        }

        /// <summary>Movement rules (never null).</summary>
        public MovementSettings Movement
        {
            get
            {
#if UNITY_EDITOR
                if (movement == null) movement = MovementSettings.LoadOrCreateDefault();
                if (!movementChecked && movement != null)
                {
                    movementChecked = true;
                    movement.EditorEnsureDefaults();
                }
#endif
                return movement != null ? movement : MovementSettings.Fallback;
            }
        }

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
            objectsByUid.Clear();
            buried.Clear();
            nextUid = 1;
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
            objectsByUid.Clear();
            buried.Clear();
            nextUid = 1;
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
            objectsByUid.Clear();
            nextUid = 1;
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

        /// <summary>
        /// Land, or water at most Wadeable Water Depth cells from the shore (deeper water needs a boat), without a
        /// solid object on it (trees, stones... ; bushes, grass and bones can be walked through).
        /// </summary>
        public bool IsWalkable(Vector2Int cell)
        {
            if (!IsTerrainWalkable(cell)) return false;
            var o = objects != null ? objects[Index(cell)] : null;
            return o == null || !o.BlocksMovement;
        }

        /// <summary>Walkable by terrain only (objects ignored).</summary>
        public bool IsTerrainWalkable(Vector2Int cell)
        {
            if (terrain == null || !InBounds(cell)) return false;
            int i = Index(cell);
            if (terrain[i] == CellTerrain.Land) return true;
            if (IsBridge(cell)) return true;
            var m = Movement;
            return m.deepWaterWalkable || waterDepth[i] <= m.wadeableWaterDepth;
        }

        public bool IsWalkable(Vector2 world) => IsWalkable(WorldToCell(world));

        /// <summary>A finished bridge (or another object that makes its cells walkable) stands on the cell.</summary>
        public bool IsBridge(Vector2Int cell)
        {
            var o = objects != null && InBounds(cell) ? objects[Index(cell)] : null;
            return o != null && o.MakesGroundWalkable;
        }

        /// <summary>Walkability of these cells changed (a bridge was finished or destroyed): update the pathfinder.</summary>
        public void RefreshCells(RectInt area)
        {
            if (pathfinder == null) return;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                if (InBounds(new Vector2Int(x, y))) pathfinder.RefreshCell(x, y);
        }

        /// <summary>
        /// Movement speed multiplier of a cell: biome speed on land (swamp 0.25), shallow water speed in water,
        /// multiplied by the speed inside a walk-through object (bush 0.7).
        /// </summary>
        public float SpeedMultiplierAt(Vector2Int cell)
        {
            float speed = TerrainSpeedAt(cell);
            var o = objects != null && InBounds(cell) ? objects[Index(cell)] : null;
            if (o != null && !o.BlocksMovement) speed *= o.WalkThroughSpeed;
            return speed;
        }

        /// <summary>Speed multiplier of the terrain only (objects ignored).</summary>
        public float TerrainSpeedAt(Vector2Int cell)
        {
            if (terrain == null || !InBounds(cell)) return 1f;
            int i = Index(cell);
            var biome = GetBiome(cell);
            float biomeSpeed = biome != null ? biome.MovementSpeedMultiplier : 1f;
            if (terrain[i] == CellTerrain.Land) return biomeSpeed;
            if (IsBridge(cell)) return 1f;
            var m = Movement;
            float water = waterDepth[i] <= m.wadeableWaterDepth ? m.shallowWaterSpeed : m.deepWaterSpeed;
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

        /// <summary>
        /// Forgets every object at once (their GameObjects are destroyed by the caller). Used when the objects
        /// are rebuilt from the original map.
        /// </summary>
        public void ClearObjects()
        {
            if (objects != null) objects = new WorldObject[width * height];
            pathfinder = null;
            objectsByUid.Clear();
            buried.Clear();
            nextUid = 1;
        }

        /// <summary>Next id given to a new object.</summary>
        public int NextUid
        {
            get => nextUid;
            set => nextUid = Mathf.Max(1, value);
        }

        public int ObjectCount => objectsByUid.Count;

        /// <summary>Puts the object on its cells and gives it an id (keeps the requested one if it is free).</summary>
        /// <returns>The id of the object.</returns>
        public int RegisterObject(WorldObject worldObject, int requestedUid = 0)
        {
            if (objects == null || worldObject == null) return requestedUid;
            int uid = requestedUid;
            if (uid <= 0 || (objectsByUid.TryGetValue(uid, out var holder) && holder != null && holder != worldObject))
            {
                if (uid > 0) Debug.LogWarning($"[MapData] Object id {uid} is already used; {worldObject.name} gets a new id.", worldObject);
                uid = nextUid;
            }
            if (uid >= nextUid) nextUid = uid + 1;
            objectsByUid[uid] = worldObject;

            var area = worldObject.Area;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!InBounds(c)) continue;
                int i = Index(c);
                var previous = objects[i];
                if (previous != null && previous != worldObject)
                {
                    // Overlap (developer mode): keep the old object reachable once the new one leaves
                    if (!buried.TryGetValue(i, out var list)) buried[i] = list = new List<WorldObject>();
                    if (!list.Contains(previous)) list.Add(previous);
                }
                objects[i] = worldObject;
                pathfinder?.RefreshCell(x, y);
            }
            return uid;
        }

        public void UnregisterObject(WorldObject worldObject)
        {
            if (objects == null || worldObject == null) return;
            if (objectsByUid.TryGetValue(worldObject.Uid, out var holder) && holder == worldObject) objectsByUid.Remove(worldObject.Uid);
            var area = worldObject.Area;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!InBounds(c)) continue;
                int i = Index(c);
                buried.TryGetValue(i, out var list);
                if (objects[i] == worldObject)
                {
                    WorldObject next = null;
                    while (list != null && list.Count > 0 && next == null)
                    {
                        next = list[list.Count - 1];
                        list.RemoveAt(list.Count - 1);
                        if (next == null || objectsByUid.ContainsKey(next.Uid) == false) next = null;
                    }
                    objects[i] = next;
                    pathfinder?.RefreshCell(x, y);
                }
                else list?.Remove(worldObject);
                if (list != null && list.Count == 0) buried.Remove(i);
            }
        }

        /// <summary>Every object on the map (by id).</summary>
        public IEnumerable<WorldObject> AllObjects
        {
            get
            {
                foreach (var o in objectsByUid.Values)
                    if (o != null) yield return o;
            }
        }

        /// <summary>Object with the given id, or null.</summary>
        public WorldObject FindObject(int uid) =>
            objectsByUid.TryGetValue(uid, out var o) && o != null ? o : null;

        /// <summary>True if another object than the given ones covers any cell of the area (or a cell is not walkable land / shallow water).</summary>
        public bool IsAreaBlocked(RectInt area, ICollection<WorldObject> ignore = null)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!IsTerrainWalkable(c)) return true;
                var o = GetObjectAt(c);
                if (o != null && (ignore == null || !ignore.Contains(o))) return true;
                if (buried.TryGetValue(Index(c), out var list))
                    foreach (var b in list)
                        if (b != null && (ignore == null || !ignore.Contains(b))) return true;
            }
            return false;
        }

        public WorldObject GetObjectAt(Vector2Int cell) => objects != null && InBounds(cell) ? objects[Index(cell)] : null;

        /// <summary>Adds every object covering cells of the area (also those hidden under another one), without duplicates.</summary>
        public void CollectObjects(RectInt area, List<WorldObject> into)
        {
            if (objects == null) return;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (!InBounds(c)) continue;
                int i = Index(c);
                var o = objects[i];
                if (o != null && !into.Contains(o)) into.Add(o);
                if (buried.TryGetValue(i, out var list))
                    foreach (var b in list)
                        if (b != null && !into.Contains(b)) into.Add(b);
            }
        }

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
