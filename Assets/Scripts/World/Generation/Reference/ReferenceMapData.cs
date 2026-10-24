using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace KingOfNothing.World.Generation
{
    /// <summary>Biome numbers of the Azgaar Fantasy Map Generator.</summary>
    public enum ReferenceBiome : byte
    {
        Marine = 0,
        HotDesert = 1,
        ColdDesert = 2,
        Savanna = 3,
        Grassland = 4,
        TropicalSeasonalForest = 5,
        TemperateDeciduousForest = 6,
        TropicalRainforest = 7,
        TemperateRainforest = 8,
        Taiga = 9,
        Tundra = 10,
        Glacier = 11,
        Wetland = 12,
    }

    /// <summary>
    /// A hand-made reference map (Azgaar .map file) rasterised to the game grid: land, lakes, the largest rivers,
    /// height and biome of every cell. Produced once in the editor (see AzgaarMapParser) and stored compressed in
    /// a ReferenceMapAsset, so the game does not have to parse the big .map file at every start.
    /// Index = x + y * Width, y = 0 is the bottom of the map.
    /// </summary>
    public sealed class ReferenceMapData
    {
        public const int BiomeCount = 13;
        const int FormatVersion = 1;

        public int Width;
        public int Height;
        /// <summary>Land without lakes and rivers.</summary>
        public bool[] Land;
        /// <summary>Land masses of the reference before lakes / rivers were cut out.</summary>
        public bool[] OriginalLand;
        public bool[] Lake;
        public bool[] River;
        /// <summary>Azgaar height (0-100, sea level 20), interpolated.</summary>
        public float[] Elevation;
        public ReferenceBiome[] Biome;
        /// <summary>Cells where rivers reach the original sea.</summary>
        public List<int> RiverMouths = new List<int>();

        public int CellCount => Width * Height;

        public ReferenceMapData(int width, int height)
        {
            Width = width;
            Height = height;
            int n = width * height;
            Land = new bool[n];
            OriginalLand = new bool[n];
            Lake = new bool[n];
            River = new bool[n];
            Elevation = new float[n];
            Biome = new ReferenceBiome[n];
        }

        // ------------------------------------------------------------------ serialization

        public byte[] Serialize()
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                using (var writer = new BinaryWriter(gzip))
                {
                    writer.Write(FormatVersion);
                    writer.Write(Width);
                    writer.Write(Height);
                    int n = CellCount;
                    for (int i = 0; i < n; i++)
                    {
                        byte flags = (byte)((Land[i] ? 1 : 0) | (OriginalLand[i] ? 2 : 0) | (Lake[i] ? 4 : 0) | (River[i] ? 8 : 0));
                        writer.Write(flags);
                    }
                    for (int i = 0; i < n; i++) writer.Write((byte)Biome[i]);
                    // Height with 0.5 precision is plenty
                    for (int i = 0; i < n; i++) writer.Write((byte)Math.Max(0, Math.Min(255, (int)Math.Round(Elevation[i] * 2f))));
                    writer.Write(RiverMouths.Count);
                    foreach (int m in RiverMouths) writer.Write(m);
                }
                return output.ToArray();
            }
        }

        public static ReferenceMapData Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            using (var input = new MemoryStream(bytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
            {
                int version = reader.ReadInt32();
                if (version != FormatVersion) return null;
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                var data = new ReferenceMapData(width, height);
                int n = data.CellCount;
                for (int i = 0; i < n; i++)
                {
                    byte flags = reader.ReadByte();
                    data.Land[i] = (flags & 1) != 0;
                    data.OriginalLand[i] = (flags & 2) != 0;
                    data.Lake[i] = (flags & 4) != 0;
                    data.River[i] = (flags & 8) != 0;
                }
                for (int i = 0; i < n; i++) data.Biome[i] = (ReferenceBiome)reader.ReadByte();
                for (int i = 0; i < n; i++) data.Elevation[i] = reader.ReadByte() * 0.5f;
                int mouths = reader.ReadInt32();
                for (int k = 0; k < mouths; k++) data.RiverMouths.Add(reader.ReadInt32());
                return data;
            }
        }
    }
}
