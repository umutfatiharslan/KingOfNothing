using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingOfNothing.World
{
    /// <summary>
    /// The 16 ground tiles of one biome, chosen by which of the 4 neighbours belong to the same ground
    /// (Tiny Swords flat ground layout: 3x3 corners/edges, vertical strip, horizontal strip, single tile).
    /// Index = N | E &lt;&lt; 1 | S &lt;&lt; 2 | W &lt;&lt; 3 (bit set = neighbour is the same ground).
    /// Created automatically by "King of Nothing > Setup > Create Default Map Assets".
    /// </summary>
    [CreateAssetMenu(fileName = "GroundTileSet", menuName = "King of Nothing/World/Ground Tile Set")]
    public class GroundTileSet : ScriptableObject
    {
        public const int North = 1, East = 2, South = 4, West = 8;
        public const int TileCount = 16;

        [Tooltip("Tile per neighbour mask (N=1, E=2, S=4, W=8). Index 15 = surrounded on all sides.")]
        [SerializeField] TileBase[] tiles = new TileBase[TileCount];

        [Tooltip("Used when the tile for a mask is missing.")]
        [SerializeField] TileBase fallback;

        public static int Mask(bool north, bool east, bool south, bool west) =>
            (north ? North : 0) | (east ? East : 0) | (south ? South : 0) | (west ? West : 0);

        public TileBase GetTile(int mask)
        {
            if (tiles != null && mask >= 0 && mask < tiles.Length && tiles[mask] != null) return tiles[mask];
            if (fallback != null) return fallback;
            return tiles != null && tiles.Length == TileCount ? tiles[TileCount - 1] : null;
        }

        public TileBase GetTile(bool north, bool east, bool south, bool west) => GetTile(Mask(north, east, south, west));

        public bool IsComplete
        {
            get
            {
                if (tiles == null || tiles.Length != TileCount) return false;
                foreach (var t in tiles)
                    if (t == null) return false;
                return true;
            }
        }

        public void SetTile(int mask, TileBase tile)
        {
            if (tiles == null || tiles.Length != TileCount) tiles = new TileBase[TileCount];
            tiles[mask] = tile;
        }

        public void SetFallback(TileBase tile) => fallback = tile;

        void OnValidate()
        {
            if (tiles == null || tiles.Length != TileCount) System.Array.Resize(ref tiles, TileCount);
        }
    }
}
