using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Puts the items of a broken object on the ground, only inside the ring of cells around it (a 3x3 area for
    /// a one-cell object, centre included):
    ///  1. the items are split into piles of the item's stack size (10) plus the rest (18 wood = 10 + 8), and each
    ///     pile goes on a free cell of the area;
    ///  2. when every free cell of the area holds a pile, the remaining items are added at random on top of the piles
    ///     of that item already lying there (e.g. 10, 11, 14, 16, 18, 23, 31, 21, 12).
    /// Each pile knows its amount, so taking it later gives everything. Only if the whole area is blocked (water,
    /// other objects) the piles go to the nearest free cells further away.
    /// </summary>
    public static class ItemDropper
    {
        static readonly List<Vector2Int> Candidates = new List<Vector2Int>();
        static readonly List<WorldObject> Nearby = new List<WorldObject>();

        /// <summary>Spawns the drops around the area. Returns the piles that were created.</summary>
        public static List<WorldObject> Drop(IReadOnlyList<DropEntry> drops, RectInt area, Transform parent, MapData map)
        {
            var created = new List<WorldObject>();
            if (drops == null || drops.Count == 0 || map == null || !map.IsReady) return created;
            var settings = map.Interaction;
            var zone = new RectInt(area.xMin - 1, area.yMin - 1, area.width + 2, area.height + 2);

            foreach (var drop in drops)
            {
                if (drop == null || drop.item == null || drop.amount <= 0) continue;
                if (drop.chance < 1f && Random.value > drop.chance) continue;

                // Piles of this item already lying in the area (from earlier drops)
                var piles = new List<WorldObject>();
                Nearby.Clear();
                map.CollectObjects(zone, Nearby);
                foreach (var o in Nearby)
                    if (o != null && o.Definition == drop.item && !o.IsCarried) piles.Add(o);

                var free = FreeCells(map, zone);
                int stack = drop.item.StackSize;
                int left = drop.amount;

                // 1. Full stacks first, then the rest, each on a free cell of the area
                while (left > 0 && free.Count > 0)
                {
                    int pile = Mathf.Min(stack, left);
                    var cell = free[free.Count - 1];
                    free.RemoveAt(free.Count - 1);
                    var item = CreatePile(drop.item, cell, pile, parent, map, settings);
                    if (item == null) break;
                    piles.Add(item);
                    created.Add(item);
                    left -= pile;
                }

                // Nothing of this item in the area and no free cell: use the nearest free cells further away
                while (left > 0 && piles.Count == 0)
                {
                    if (!FindFreeCell(map, area, settings.dropSearchRadius, out var cell)) cell = area.position;
                    int pile = Mathf.Min(stack, left);
                    var item = CreatePile(drop.item, cell, pile, parent, map, settings);
                    if (item == null) break;
                    piles.Add(item);
                    created.Add(item);
                    left -= pile;
                }

                // 2. The area is full: the rest lands on the piles at random
                if (left > 0 && piles.Count > 0)
                {
                    var extra = new int[piles.Count];
                    for (int i = 0; i < left; i++) extra[Random.Range(0, piles.Count)]++;
                    for (int i = 0; i < piles.Count; i++)
                        if (extra[i] > 0) piles[i].AddAmount(extra[i]);
                }
            }
            return created;
        }

        static WorldObject CreatePile(SpawnableDefinition item, Vector2Int cell, int amount, Transform parent, MapData map, InteractionSettings settings)
        {
            var offset = new Vector2(Random.Range(-settings.dropScatter, settings.dropScatter),
                                     Random.Range(-settings.dropScatter * 0.5f, settings.dropScatter * 0.5f));
            return WorldObjectFactory.Create(item, cell, offset, parent, map, configure: o => o.Amount = amount);
        }

        /// <summary>Free cells (walkable land, no object) of the zone in random order.</summary>
        static List<Vector2Int> FreeCells(MapData map, RectInt zone)
        {
            var cells = new List<Vector2Int>();
            for (int y = zone.yMin; y < zone.yMax; y++)
            for (int x = zone.xMin; x < zone.xMax; x++)
            {
                var c = new Vector2Int(x, y);
                if (map.IsLand(c) && !map.IsAreaBlocked(new RectInt(c, Vector2Int.one))) cells.Add(c);
            }
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (cells[i], cells[j]) = (cells[j], cells[i]);
            }
            return cells;
        }

        /// <summary>Random free cell (no object, walkable land) close to the area: nearest rings first.</summary>
        public static bool FindFreeCell(MapData map, RectInt area, int radius, out Vector2Int cell)
        {
            for (int r = 0; r <= radius; r++)
            {
                Candidates.Clear();
                for (int y = area.yMin - r; y < area.yMax + r; y++)
                for (int x = area.xMin - r; x < area.xMax + r; x++)
                {
                    bool onRing = x == area.xMin - r || x == area.xMax + r - 1 || y == area.yMin - r || y == area.yMax + r - 1;
                    if (r > 0 && !onRing) continue;
                    var c = new Vector2Int(x, y);
                    if (map.IsLand(c) && !map.IsAreaBlocked(new RectInt(c, Vector2Int.one))) Candidates.Add(c);
                }
                if (Candidates.Count > 0)
                {
                    cell = Candidates[Random.Range(0, Candidates.Count)];
                    return true;
                }
            }
            cell = area.position;
            return false;
        }
    }
}
