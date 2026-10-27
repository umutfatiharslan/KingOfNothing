using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Objects that several characters work through together (one order from the actions menu).
    ///  - Individually: every character claims the nearest object nobody else is working on (one character per object).
    ///  - Together: all characters work on the same object; when it breaks they move on to the remaining object
    ///    nearest to it, until the whole group is done.
    /// "Nearest" is the straight distance from the character (or from the last object) to the object.
    /// </summary>
    public sealed class WorkGroup
    {
        readonly List<ResourceNode> targets;
        readonly Dictionary<ResourceNode, Unit> claims = new Dictionary<ResourceNode, Unit>();
        ResourceNode shared;
        Vector2 lastPosition;
        bool hasLastPosition;

        public bool Together { get; }
        public int Remaining
        {
            get
            {
                Prune();
                return targets.Count;
            }
        }

        public WorkGroup(IEnumerable<ResourceNode> objects, bool together)
        {
            targets = new List<ResourceNode>(objects);
            Together = together;
        }

        void Prune()
        {
            targets.RemoveAll(t => t == null || !t.CanBeHit);
            if (shared != null && !shared.CanBeHit) shared = null;
        }

        /// <summary>The object this character should work on next (null = the group is done for it).</summary>
        public ResourceNode Next(Unit unit, ResourceNode finished)
        {
            Prune();
            if (finished != null)
            {
                claims.Remove(finished);
                lastPosition = finished.GroundPoint;
                hasLastPosition = true;
            }
            if (targets.Count == 0) return null;
            Vector2 from = unit.transform.position;

            if (Together)
            {
                if (shared == null) shared = Nearest(hasLastPosition ? lastPosition : from, null);
                return shared;
            }

            // Already working on something that still stands: keep it
            foreach (var pair in claims)
                if (pair.Value == unit && pair.Key != null && pair.Key.CanBeHit) return pair.Key;

            var free = Nearest(from, claims);
            if (free != null) claims[free] = unit;
            return free;
        }

        /// <summary>The character gives up its object; unreachable objects leave the group.</summary>
        public void Release(Unit unit, ResourceNode target, bool unreachable)
        {
            if (target == null) return;
            if (claims.TryGetValue(target, out var owner) && owner == unit) claims.Remove(target);
            if (!unreachable) return;
            targets.Remove(target);
            if (target == shared) shared = null;
        }

        ResourceNode Nearest(Vector2 from, Dictionary<ResourceNode, Unit> skip)
        {
            ResourceNode best = null;
            float bestDistance = float.MaxValue;
            foreach (var t in targets)
            {
                if (skip != null && skip.ContainsKey(t)) continue;
                float d = (t.GroundPoint - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }
    }
}
