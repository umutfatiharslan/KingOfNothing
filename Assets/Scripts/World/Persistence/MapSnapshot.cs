using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using KingOfNothing.World.Generation;

namespace KingOfNothing.World.Persistence
{
    /// <summary>One map object of a snapshot: what it is and where it stands.</summary>
    public struct MapObjectRecord
    {
        /// <summary>Index into MapSnapshot.ObjectTypes (asset name of the SpawnableDefinition).</summary>
        public int Type;
        /// <summary>Index into MapSnapshot.GroupLabels (hierarchy group, e.g. "Forest Trees").</summary>
        public int Group;
        /// <summary>Bottom-left cell of the footprint.</summary>
        public int X, Y;
        /// <summary>Small shift inside the cell.</summary>
        public float OffsetX, OffsetY;
    }

    /// <summary>
    /// A complete map frozen as plain data: terrain and biome of every cell, the start cell and every object.
    /// The original map is stored as one of these (OriginalMapAsset), so later changes to the code or to the
    /// generation settings never change it. Objects are referenced by asset name, biomes by asset name too,
    /// so they are matched again when the snapshot is loaded. No UnityEngine types: can be tested outside Unity.
    /// </summary>
    public sealed class MapSnapshot
    {
        const uint Magic = 0x534E4F4B; // "KONS"
        const int FormatVersion = 1;
        const float OffsetScale = 1000f;

        /// <summary>Unique id of this snapshot; edited map files remember which original they belong to.</summary>
        public string Id = Guid.NewGuid().ToString("N");
        public int Seed;
        public int Width, Height;
        /// <summary>When the snapshot was made (ISO 8601, UTC).</summary>
        public string CreatedAt = DateTime.UtcNow.ToString("o");
        /// <summary>Generation statistics of the moment it was made (shown in the Inspector).</summary>
        public string Report = "";
        public int StartX = -1, StartY = -1;

        public CellTerrain[] Terrain;
        /// <summary>Index into BiomeNames per cell; MapLayout.NoBiome (-1) for water.</summary>
        public short[] Biome;
        public string[] BiomeNames = Array.Empty<string>();

        public string[] ObjectTypes = Array.Empty<string>();
        public string[] GroupLabels = Array.Empty<string>();
        public readonly List<MapObjectRecord> Objects = new List<MapObjectRecord>();

        public int CellCount => Width * Height;

        /// <summary>Snapshot of a generated layout (objects are added by the caller).</summary>
        public static MapSnapshot FromLayout(MapLayout layout, int seed, string[] biomeNames, string report)
        {
            return new MapSnapshot
            {
                Seed = seed,
                Width = layout.Width,
                Height = layout.Height,
                Terrain = (CellTerrain[])layout.Terrain.Clone(),
                Biome = (short[])layout.Biome.Clone(),
                BiomeNames = biomeNames ?? Array.Empty<string>(),
                Report = report ?? "",
            };
        }

        /// <summary>
        /// Rebuilds a MapLayout (terrain + biomes) for painting and MapData. Biomes are matched by name to the
        /// current biome list; unknown names fall back to the first biome.
        /// </summary>
        /// <param name="missingBiomes">Names that were not found (for a warning).</param>
        public MapLayout ToLayout(IList<string> currentBiomeNames, List<string> missingBiomes = null)
        {
            var layout = new MapLayout(Width, Height);
            Array.Copy(Terrain, layout.Terrain, CellCount);

            var remap = new short[BiomeNames.Length];
            for (int b = 0; b < BiomeNames.Length; b++)
            {
                int found = currentBiomeNames != null ? currentBiomeNames.IndexOf(BiomeNames[b]) : -1;
                if (found < 0)
                {
                    found = 0;
                    missingBiomes?.Add(BiomeNames[b]);
                }
                remap[b] = (short)found;
            }
            for (int i = 0; i < CellCount; i++)
            {
                short b = Biome[i];
                layout.Biome[i] = b >= 0 && b < remap.Length ? remap[b] : MapLayout.NoBiome;
            }
            return layout;
        }

        // ------------------------------------------------------------------ binary format

        public byte[] Serialize()
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
                using (var w = new BinaryWriter(gzip, Encoding.UTF8))
                {
                    w.Write(Magic);
                    w.Write(FormatVersion);
                    w.Write(Id ?? "");
                    w.Write(Seed);
                    w.Write(Width);
                    w.Write(Height);
                    w.Write(CreatedAt ?? "");
                    w.Write(Report ?? "");
                    w.Write(StartX);
                    w.Write(StartY);
                    WriteStrings(w, BiomeNames);

                    var terrainBytes = new byte[CellCount];
                    for (int i = 0; i < terrainBytes.Length; i++) terrainBytes[i] = (byte)Terrain[i];
                    w.Write(terrainBytes);
                    for (int i = 0; i < CellCount; i++) w.Write(Biome[i]);

                    WriteStrings(w, ObjectTypes);
                    WriteStrings(w, GroupLabels);
                    w.Write(Objects.Count);
                    foreach (var o in Objects)
                    {
                        w.Write((ushort)o.Type);
                        w.Write((ushort)o.Group);
                        w.Write((ushort)o.X);
                        w.Write((ushort)o.Y);
                        w.Write((short)Math.Round(o.OffsetX * OffsetScale));
                        w.Write((short)Math.Round(o.OffsetY * OffsetScale));
                    }
                }
                return memory.ToArray();
            }
        }

        public static MapSnapshot Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0) throw new InvalidDataException("Empty map snapshot.");
            using (var memory = new MemoryStream(data))
            using (var gzip = new GZipStream(memory, CompressionMode.Decompress))
            using (var r = new BinaryReader(gzip, Encoding.UTF8))
            {
                if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a King of Nothing map snapshot.");
                int version = r.ReadInt32();
                if (version != FormatVersion) throw new InvalidDataException($"Unsupported map snapshot version {version}.");

                var s = new MapSnapshot
                {
                    Id = r.ReadString(),
                    Seed = r.ReadInt32(),
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    CreatedAt = r.ReadString(),
                    Report = r.ReadString(),
                    StartX = r.ReadInt32(),
                    StartY = r.ReadInt32(),
                    BiomeNames = ReadStrings(r),
                };
                int n = s.CellCount;
                var terrainBytes = ReadExactly(r, n);
                s.Terrain = new CellTerrain[n];
                for (int i = 0; i < n; i++) s.Terrain[i] = (CellTerrain)terrainBytes[i];
                s.Biome = new short[n];
                for (int i = 0; i < n; i++) s.Biome[i] = r.ReadInt16();

                s.ObjectTypes = ReadStrings(r);
                s.GroupLabels = ReadStrings(r);
                int count = r.ReadInt32();
                s.Objects.Capacity = count;
                for (int i = 0; i < count; i++)
                {
                    s.Objects.Add(new MapObjectRecord
                    {
                        Type = r.ReadUInt16(),
                        Group = r.ReadUInt16(),
                        X = r.ReadUInt16(),
                        Y = r.ReadUInt16(),
                        OffsetX = r.ReadInt16() / OffsetScale,
                        OffsetY = r.ReadInt16() / OffsetScale,
                    });
                }
                return s;
            }
        }

        static void WriteStrings(BinaryWriter w, string[] values)
        {
            values = values ?? Array.Empty<string>();
            w.Write(values.Length);
            foreach (var v in values) w.Write(v ?? "");
        }

        static string[] ReadStrings(BinaryReader r)
        {
            int count = r.ReadInt32();
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = r.ReadString();
            return result;
        }

        static byte[] ReadExactly(BinaryReader r, int count)
        {
            var bytes = r.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException("Map snapshot is truncated.");
            return bytes;
        }
    }
}
