using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingOfNothing.World.Objects
{
    /// <summary>Creates map objects from SpawnableDefinitions (used by the map spawner, trees leaving stumps, dev tools).</summary>
    public static class WorldObjectFactory
    {
        /// <summary>Height of the object's ground contact point inside its bottom cell.</summary>
        public const float BaseOffsetY = 0.15f;

        /// <summary>Ground contact point of an object (sorting and placement use it).</summary>
        public static Vector2 BasePoint(Vector2Int footprint, Vector2Int cell, Vector2 offset) =>
            new Vector2(cell.x + footprint.x * 0.5f + offset.x, cell.y + BaseOffsetY + offset.y);

        /// <summary>Position of an object's sprite so its visible base (minus the ground inset) stands on the ground point.</summary>
        public static Vector3 SpritePosition(SpawnableDefinition definition, Sprite sprite, Vector2Int cell, Vector2 offset)
        {
            var basePoint = BasePoint(definition.Footprint, cell, offset);
            float scale = definition.VisualScale;
            float centerY = basePoint.y + (sprite.bounds.extents.y - sprite.bounds.center.y - definition.GroundInset) * scale;
            return new Vector3(basePoint.x, centerY, 0f);
        }

        /// <param name="cell">Bottom-left cell of the footprint.</param>
        /// <param name="offset">Small random shift inside the cell.</param>
        /// <param name="uid">Id the object must keep (original map / edited map file). 0 = next free id.</param>
        /// <param name="configure">Runs after Initialize and before the object is announced (AnyCreated).</param>
        public static WorldObject Create(SpawnableDefinition definition, Vector2Int cell, Vector2 offset, Transform parent, MapData map,
            int uid = 0, Action<WorldObject> configure = null)
        {
            if (definition == null) return null;
            var footprint = definition.Footprint;
            var basePoint = BasePoint(footprint, cell, offset);
            float baseX = basePoint.x;
            float baseY = basePoint.y;

            GameObject go;
            if (definition is BuildingDefinition && definition.PrefabOverride == null)
            {
                // Buildings draw their own sprites (construction / finished / destroyed looks) as children
                go = new GameObject(definition.DisplayName);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(baseX, baseY, 0f);
            }
            else if (definition.PrefabOverride != null)
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
                go.transform.position = SpritePosition(definition, sprite, cell, offset);
                float scale = definition.VisualScale;
                if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = new Vector3(scale, scale, 1f);

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
            if (worldObject == null)
                worldObject = definition is BuildingDefinition
                    ? go.AddComponent<KingOfNothing.World.Buildings.Building>()
                    : AddComponentForKind(go, definition.Kind);
            worldObject.AssignUid(uid);
            worldObject.Initialize(definition, cell, offset, map);
            configure?.Invoke(worldObject);
            WorldObject.RaiseCreated(worldObject);
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
                case WorldObjectKind.Building: return go.AddComponent<KingOfNothing.World.Buildings.Building>();
                default: return go.AddComponent<DecorationObject>();
            }
        }
    }
}
