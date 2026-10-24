using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Draw order of map objects (trees, units, buildings...): the lower an object stands, the more in front
    /// it is drawn. The map generator and moving units must use the same formula, otherwise units are drawn
    /// wrongly when walking in front of / behind trees.
    /// </summary>
    public static class MapSorting
    {
        public static int MapHeight { get; private set; } = 800;

        // sortingOrder is limited to 32767, so the multiplier depends on the map height
        static float scale = ComputeScale(800);

        /// <summary>Called by MapData when a map is generated or loaded.</summary>
        public static void Configure(int mapHeight)
        {
            MapHeight = Mathf.Max(1, mapHeight);
            scale = ComputeScale(MapHeight);
        }

        static float ComputeScale(int height) => Mathf.Min(100f, 32000f / (height + 1));

        /// <param name="x">X of the point where the object touches the ground.</param>
        /// <param name="y">Y of the point where the object touches the ground.</param>
        public static int Order(float x, float y) =>
            // Two objects with the same order make Unity flip their draw order every frame (flicker);
            // the small x based term separates them.
            Mathf.RoundToInt((MapHeight - y) * scale) + Mathf.Abs(Mathf.FloorToInt(x * 3f)) % 10;
    }
}
