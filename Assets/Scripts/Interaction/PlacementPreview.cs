using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// See-through copies of objects that follow the mouse while choosing where to move them. The group keeps its
    /// layout around the anchor object. In Default mode a copy that would overlap another object (or stand in water)
    /// is tinted red and cannot be placed; developer mode allows overlapping and shows no red.
    /// </summary>
    public sealed class PlacementPreview
    {
        readonly List<WorldObject> objects = new List<WorldObject>();
        readonly List<Vector2Int> relative = new List<Vector2Int>();
        readonly List<SpriteRenderer> ghosts = new List<SpriteRenderer>();
        readonly List<Vector2Int> targets = new List<Vector2Int>();
        readonly List<bool> blocked = new List<bool>();
        readonly HashSet<WorldObject> moving = new HashSet<WorldObject>();
        GameObject root;

        public bool IsActive => root != null;
        public IReadOnlyList<WorldObject> Objects => objects;
        /// <summary>Target cell of each object (same order as Objects) after the last Update.</summary>
        public IReadOnlyList<Vector2Int> Targets => targets;
        public bool AnyBlocked { get; private set; }

        public void Begin(IEnumerable<WorldObject> group, WorldObject anchor)
        {
            End();
            root = new GameObject("Placement Preview");
            foreach (var o in group)
            {
                if (o == null || objects.Contains(o)) continue;
                objects.Add(o);
                moving.Add(o);
            }
            if (anchor == null && objects.Count > 0) anchor = objects[0];
            var anchorCell = anchor != null ? anchor.Cell : Vector2Int.zero;
            foreach (var o in objects)
            {
                relative.Add(o.Cell - anchorCell);
                var ghost = new GameObject(o.DisplayName).AddComponent<SpriteRenderer>();
                ghost.transform.SetParent(root.transform, false);
                var source = o.Renderers.Length > 0 ? o.Renderers[0] : null;
                if (source != null)
                {
                    ghost.sprite = source.sprite;
                    ghost.flipX = source.flipX;
                }
                ghost.sortingOrder = short.MaxValue - 20;
                ghosts.Add(ghost);
                targets.Add(o.Cell);
                blocked.Add(false);
            }
        }

        /// <summary>
        /// Moves the copies so the anchor stands on the cell; recomputes which ones overlap. With allowOverlap
        /// (developer mode) overlapping copies are not tinted red.
        /// </summary>
        public void Update(Vector2Int anchorCell, MapData map, InteractionSettings settings, bool allowOverlap = false)
        {
            if (!IsActive) return;
            AnyBlocked = false;
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (o == null) continue;
                var cell = anchorCell + relative[i];
                targets[i] = cell;
                bool isBlocked = map != null && map.IsAreaBlocked(new RectInt(cell, o.Footprint), moving);
                blocked[i] = isBlocked;
                AnyBlocked |= isBlocked;

                var spriteOffset = o.transform.position - (Vector3)o.GroundPoint;
                var ground = WorldObjectFactory.BasePoint(o.Footprint, cell, o.Offset);
                ghosts[i].transform.position = (Vector3)ground + spriteOffset;
                ghosts[i].color = isBlocked && !allowOverlap ? settings.overlapTint : settings.previewTint;
            }
        }

        public void End()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            objects.Clear();
            relative.Clear();
            ghosts.Clear();
            targets.Clear();
            blocked.Clear();
            moving.Clear();
            AnyBlocked = false;
        }
    }
}
