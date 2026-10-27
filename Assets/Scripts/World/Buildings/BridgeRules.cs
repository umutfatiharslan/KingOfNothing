using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>
    /// How bridges fit together. A bridge runs along its long side (horizontal 3x1, vertical 1x3); its first cell
    /// (left / bottom) and last cell (right / top) are its ends. An end is joined when the cell right after it
    /// belongs to another bridge (the same direction, or a bridge running the other way met from the side). A
    /// joined end is drawn without its posts; meeting a bridge of the other direction it also lays a short piece
    /// over that bridge's edge, so there is no gap. Free ends (and ends only touched from the side) always keep
    /// their support posts. Placement itself is free (see CanPlace): to cross water the bridges simply have to be
    /// put next to each other.
    /// </summary>
    public static class BridgeRules
    {
        static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        public static bool IsHorizontal(RectInt area) => area.width >= area.height;
        public static Vector2Int Axis(RectInt area) => IsHorizontal(area) ? Vector2Int.right : Vector2Int.up;
        public static Vector2Int StartCell(RectInt area) => area.position;
        public static Vector2Int EndCell(RectInt area) => IsHorizontal(area)
            ? new Vector2Int(area.xMax - 1, area.yMin)
            : new Vector2Int(area.xMin, area.yMax - 1);
        public static Vector2Int StartOutward(RectInt area) => -Axis(area);
        public static Vector2Int EndOutward(RectInt area) => Axis(area);

        /// <summary>The bridge standing on the cell (null if none).</summary>
        public static Building BridgeAt(MapData map, Vector2Int cell)
        {
            var o = map.GetObjectAt(cell);
            return o is Building b && b.Info != null && b.Info.isBridge && !b.IsDepleted ? b : null;
        }

        static bool TryOutward(RectInt area, Vector2Int endCell, out Vector2Int outward)
        {
            if (endCell == StartCell(area)) { outward = StartOutward(area); return true; }
            if (endCell == EndCell(area)) { outward = EndOutward(area); return true; }
            outward = Vector2Int.zero;
            return false;
        }

        /// <summary>
        /// The bridge straight ahead of this end (null if none): a bridge continuing in the same direction, or one
        /// running the other way that this end meets from the side. Such an end is drawn without its posts. An end
        /// that is only touched from the side by another bridge keeps its posts.
        /// </summary>
        public static Building AheadOf(MapData map, Vector2Int endCell, Vector2Int outward, Building self)
        {
            var ahead = BridgeAt(map, endCell + outward);
            return ahead != null && ahead != self ? ahead : null;
        }

        /// <summary>Is another bridge straight ahead of this end?</summary>
        public static bool JoinedAt(MapData map, RectInt area, Vector2Int endCell, Vector2Int outward, Building self) =>
            AheadOf(map, endCell, outward, self) != null;

        /// <summary>
        /// Placement rules of a new bridge on these cells. A bridge may go anywhere (land or water) where it does
        /// not overlap another structure - that is checked by the caller, developer mode skips it. Over water the
        /// characters only get across when the bridges touch each other (and the shore): a bridge is only walkable
        /// itself, so a gap of water between two bridges stays a gap. Nothing else is enforced here.
        /// </summary>
        public static bool CanPlace(MapData map, RectInt area, out string problem)
        {
            problem = null;
            return true;
        }
    }
}
