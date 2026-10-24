using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>Creates map objects from SpawnableDefinitions (used by the map spawner, trees leaving stumps, dev tools).</summary>
    public static class WorldObjectFactory
    {
        /// <summary>Height of the object's ground contact point inside its bottom cell.</summary>
        public const float BaseOffsetY = 0.15f;

        /// <param name="cell">Bottom-left cell of the footprint.</param>
        /// <param name="offset">Small random shift inside the cell.</param>
        public static WorldObject Create(SpawnableDefinition definition, Vector2Int cell, Vector2 offset, Transform parent, MapData map)
        {
            if (definition == null) return null;
            var footprint = definition.Footprint;
            float baseX = cell.x + footprint.x * 0.5f + offset.x;
            float baseY = cell.y + BaseOffsetY + offset.y;

            GameObject go;
            if (definition.PrefabOverride != null)
            {
                go = Object.Instantiate(definition.PrefabOverride, new Vector3(baseX, baseY, 0f), Quaternion.identity, parent);
                go.name = definition.DisplayName;
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>())
                    r.sortingOrder = MapSorting.Order(baseX, baseY);
            }
            else
            {
                var frames = definition.Frames;
                if (frames == null || frames.Length == 0 || frames[0] == null) return null;
                var sprite = frames[0];

                go = new GameObject(definition.DisplayName);
                go.transform.SetParent(parent, false);
                // Sprite pivots are centred: move the sprite up so its visible base touches the ground point
                float centerY = baseY + sprite.bounds.extents.y - sprite.bounds.center.y - definition.GroundInset;
                go.transform.position = new Vector3(baseX, centerY, 0f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = MapSorting.Order(baseX, baseY);

                if (definition.Animate && frames.Length > 1)
                {
                    var animator = go.AddComponent<SpriteSheetAnimator>();
                    animator.Setup(frames, definition.AnimationFps);
                }
            }

            var worldObject = go.GetComponent<WorldObject>();
            if (worldObject == null) worldObject = AddComponentForKind(go, definition.Kind);
            worldObject.Initialize(definition, cell, map);
            return worldObject;
        }

        static WorldObject AddComponentForKind(GameObject go, WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Tree: return go.AddComponent<TreeResource>();
                case WorldObjectKind.Stump: return go.AddComponent<StumpObject>();
                case WorldObjectKind.GoldMine: return go.AddComponent<GoldMineResource>();
                case WorldObjectKind.Stone:
                case WorldObjectKind.GoldStone: return go.AddComponent<ResourceNode>();
                default: return go.AddComponent<DecorationObject>();
            }
        }
    }
}
