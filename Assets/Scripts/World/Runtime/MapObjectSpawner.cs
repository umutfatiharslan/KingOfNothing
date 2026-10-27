using System;
using System.Collections.Generic;
using KingOfNothing.World.Objects;
using KingOfNothing.World.Persistence;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Creates the GameObjects of a map snapshot, grouped per rule in the hierarchy. Object n of the snapshot
    /// gets the id n + 1, so the edited map file can refer to original objects by id.
    /// </summary>
    public sealed class MapObjectSpawner
    {
        /// <summary>Asset names of objects that could not be created (removed from the settings?).</summary>
        public readonly HashSet<string> MissingTypes = new HashSet<string>();

        public int Spawn(MapSnapshot snapshot, Func<string, SpawnableDefinition> resolve, Transform root, MapData map)
        {
            MissingTypes.Clear();
            var definitions = new SpawnableDefinition[snapshot.ObjectTypes.Length];
            for (int t = 0; t < definitions.Length; t++)
            {
                definitions[t] = resolve(snapshot.ObjectTypes[t]);
                if (definitions[t] == null) MissingTypes.Add(snapshot.ObjectTypes[t]);
            }

            var groups = new Transform[snapshot.GroupLabels.Length];
            Transform fallbackGroup = null;
            int spawned = 0;
            for (int i = 0; i < snapshot.Objects.Count; i++)
            {
                var record = snapshot.Objects[i];
                var definition = record.Type >= 0 && record.Type < definitions.Length ? definitions[record.Type] : null;
                if (definition == null) continue;

                Transform group;
                if (record.Group >= 0 && record.Group < groups.Length)
                {
                    group = groups[record.Group];
                    if (group == null)
                    {
                        group = new GameObject(snapshot.GroupLabels[record.Group]).transform;
                        group.SetParent(root, false);
                        groups[record.Group] = group;
                    }
                }
                else
                {
                    if (fallbackGroup == null)
                    {
                        fallbackGroup = new GameObject("Objects").transform;
                        fallbackGroup.SetParent(root, false);
                    }
                    group = fallbackGroup;
                }

                var created = WorldObjectFactory.Create(definition, new Vector2Int(record.X, record.Y),
                    new Vector2(record.OffsetX, record.OffsetY), group, map, i + 1);
                if (created != null) spawned++;
            }
            // New objects (stumps, developer placements...) continue after the original ids
            map.NextUid = Mathf.Max(map.NextUid, snapshot.Objects.Count + 1);
            return spawned;
        }
    }
}
