using System;
using System.Collections.Generic;

namespace KingOfNothing.World.Generation
{
    /// <summary>What a single map cell is made of.</summary>
    public enum CellTerrain : byte
    {
        Sea = 0,
        Land = 1,
        Lake = 2,
        River = 3,
        Pond = 4,
        /// <summary>Small pools inside the swamp biome.</summary>
        Marsh = 5,
    }

    /// <summary>Bit mask of terrain types (used by spawn rules to say where an object may appear).</summary>
    [Flags]
    public enum TerrainMask
    {
        None = 0,
        // Bit n corresponds to CellTerrain value n
        Sea = 1,
        Land = 2,
        Lake = 4,
        River = 8,
        Pond = 16,
        Marsh = 32,
        AnyWater = Sea | Lake | River | Pond | Marsh,
    }

    public static class CellTerrainExtensions
    {
        public static bool IsWater(this CellTerrain terrain) => terrain != CellTerrain.Land;

        public static bool Matches(this TerrainMask mask, CellTerrain terrain) => (mask & (TerrainMask)(1 << (int)terrain)) != 0;
    }

    /// <summary>A lake carved by the generator.</summary>
    public sealed class LakeInfo
    {
        public int Id;
        public int CenterX, CenterY;
        public float Radius;
        public int CellCount;
    }

    /// <summary>A traced river: cells from the source to the mouth (sea, lake or another river).</summary>
    public sealed class RiverInfo
    {
        public int Id;
        public bool IsTributary;
        public bool FromLake;
        public readonly List<int> Cells = new List<int>();
    }

    /// <summary>
    /// The result of the map generation: per-cell terrain and biome plus a few helper fields.
    /// Pure data (no UnityEngine objects) so it can be generated on a worker thread and tested outside Unity.
    /// Cells are addressed by index = x + y * Width; y grows upwards like Unity world space.
    /// </summary>
    public sealed class MapLayout
    {
        public const short NoBiome = -1;

        public readonly int Width;
        public readonly int Height;
        public readonly CellTerrain[] Terrain;
        /// <summary>
        /// Biome index per cell (index into the biome list of the settings). NoBiome for sea, lakes and rivers;
        /// ponds and marsh pools keep the biome they lie in.
        /// </summary>
        public readonly short[] Biome;
        /// <summary>Landmass height field; used to grow / shrink coasts naturally.</summary>
        public readonly float[] Elevation;
        /// <summary>Lake id per cell (0 = not a lake).</summary>
        public readonly int[] LakeId;

        public readonly List<LakeInfo> Lakes = new List<LakeInfo>();
        public readonly List<RiverInfo> Rivers = new List<RiverInfo>();

        public MapLayout(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Map size must be positive.");
            Width = width;
            Height = height;
            int count = width * height;
            Terrain = new CellTerrain[count];
            Biome = new short[count];
            Elevation = new float[count];
            LakeId = new int[count];
            for (int i = 0; i < count; i++) Biome[i] = NoBiome;
        }

        public int CellCount => Width * Height;

        public int Index(int x, int y) => x + y * Width;

        public int X(int index) => index % Width;

        public int Y(int index) => index / Width;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsLand(int index) => Terrain[index] == CellTerrain.Land;

        public bool IsLand(int x, int y) => InBounds(x, y) && Terrain[Index(x, y)] == CellTerrain.Land;

        public int Count(CellTerrain terrain)
        {
            int n = 0;
            for (int i = 0; i < Terrain.Length; i++)
                if (Terrain[i] == terrain) n++;
            return n;
        }

        public float LandRatio => Count(CellTerrain.Land) / (float)CellCount;

        /// <summary>Number of land cells per biome.</summary>
        public int[] CountBiomes(int biomeCount)
        {
            var counts = new int[biomeCount];
            for (int i = 0; i < Terrain.Length; i++)
                if (Terrain[i] == CellTerrain.Land && Biome[i] >= 0 && Biome[i] < biomeCount)
                    counts[Biome[i]]++;
            return counts;
        }

        /// <summary>Distance (8-neighbour steps) from every land cell to the nearest non-land cell; 0 on water.</summary>
        public int[] ComputeDistanceToWater() => GridAlgorithms.DistanceField(this, i => Terrain[i] != CellTerrain.Land);

        /// <summary>Distance (8-neighbour steps) from every water cell to the nearest land cell; 0 on land.</summary>
        public int[] ComputeDistanceToLand() => GridAlgorithms.DistanceField(this, i => Terrain[i] == CellTerrain.Land);
    }
}
