using System.Collections.Generic;
using KingOfNothing.World.Generation;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>Turns the planner's placement requests into GameObjects, grouped per rule in the hierarchy.</summary>
    public sealed class MapObjectSpawner
    {
        public int Spawn(IReadOnlyList<PlacementRequest> requests, PlacementRuleSet ruleSet, Transform root, MapData map)
        {
            var groups = new Dictionary<int, Transform>();
            int spawned = 0;
            foreach (var request in requests)
            {
                if (request.VariantId < 0 || request.VariantId >= ruleSet.Variants.Count) continue;
                var definition = ruleSet.Variants[request.VariantId];

                if (!groups.TryGetValue(request.RuleId, out var group))
                {
                    string label = request.RuleId < ruleSet.RuleLabels.Count ? ruleSet.RuleLabels[request.RuleId] : "Objects";
                    group = new GameObject(label).transform;
                    group.SetParent(root, false);
                    groups[request.RuleId] = group;
                }

                var created = WorldObjectFactory.Create(definition, new Vector2Int(request.X, request.Y),
                    new Vector2(request.OffsetX, request.OffsetY), group, map);
                if (created != null) spawned++;
            }
            return spawned;
        }
    }
}
