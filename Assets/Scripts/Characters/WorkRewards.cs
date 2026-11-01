using System.Collections.Generic;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// What a character gets when an object breaks under its hands: experience bonuses, the mining extra resource,
    /// the harvest bonus, and the woodcutting milestones (Wood Pile, Regrowth, Fallen Giant, Forest Keeper).
    /// </summary>
    public static class WoodcuttingRewards
    {
        static bool subscribed;
        static readonly List<WorldObject> Nearby = new List<WorldObject>();

        static StatSettings S => StatSettings.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => subscribed = false;

        public static void Ensure()
        {
            if (subscribed) return;
            subscribed = true;
            TreeResource.Felled += OnFelled;
            ResourceNode.AnyDepletedBy += OnDepleted;
        }

        static Villager VillagerOf(GameObject go) => go != null ? go.GetComponent<Villager>() : null;

        static void OnFelled(TreeResource tree, StumpObject stump, GameObject instigator)
        {
            var v = VillagerOf(instigator);
            if (v == null || tree == null) return;
            var s = S;
            var map = MapData.Instance;
            var definition = tree.Definition;
            var area = tree.Area;
            int level = v.Stats.Base(StatType.Woodcutting);

            v.GainXp(StatType.Woodcutting, s.treeFelledXp);
            var memory = v.Memory;
            int today = GameClock.Today;
            if (memory.felledDay != today)
            {
                memory.felledDay = today;
                memory.felledToday = 0;
            }
            memory.felledToday++;
            if (definition != null) memory.lastFelledTree = definition.name;

            // Wood of the tree (first drop)
            SpawnableDefinition wood = null;
            int woodAmount = 0;
            if (definition != null && definition.Drops != null)
                foreach (var d in definition.Drops)
                    if (d != null && d.item != null)
                    {
                        wood = d.item;
                        woodAmount = d.amount;
                        break;
                    }

            var extra = 0;
            // A regrown tree of a Forest Keeper gives more wood the first time
            if (tree.BonusWood > 0f) extra += Mathf.RoundToInt(woodAmount * tree.BonusWood);
            // Wood Pile (5)
            if (level >= 5 && Random.value < s.extraWoodChance5) extra++;
            if (extra > 0 && wood != null && map != null)
                ItemDropper.Drop(new List<DropEntry> { new DropEntry(wood, extra) }, area, tree.transform.parent, map);

            // Regrowth (10 / 15 / 20)
            float chance = level >= 20 ? s.regrowChance20 : level >= 15 ? s.regrowChance15 : level >= 10 ? s.regrowChance10 : 0f;
            if (stump != null && chance > 0f && Random.value < chance)
            {
                bool keeper = level >= 20;
                stump.BeginRegrow(s.regrowHours / (keeper ? Mathf.Max(0.1f, s.regrowSpeed20) : 1f), keeper ? s.firstChopBonus20 : 0f);
                FloatingText.Show(v.transform, "Regrowth!", new Color(0.5f, 1f, 0.55f));
            }

            // Fallen Giant (15): the falling tree hits one tree close by
            if (level >= 15 && map != null)
            {
                int r = Mathf.CeilToInt(s.fallRadius15);
                Nearby.Clear();
                map.CollectObjects(new RectInt(area.xMin - r, area.yMin - r, area.width + 2 * r, area.height + 2 * r), Nearby);
                TreeResource best = null;
                float bestDistance = s.fallRadius15 + 0.01f;
                foreach (var o in Nearby)
                {
                    if (!(o is TreeResource other) || other == tree || !other.CanBeHit) continue;
                    float d = Vector2.Distance(other.GroundPoint, tree.GroundPoint);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = other;
                    }
                }
                if (best != null) best.ApplyHit(Mathf.Max(1, v.Stats.Get(StatType.Woodcutting)), instigator);
            }
        }

        static void OnDepleted(ResourceNode node, GameObject instigator)
        {
            if (node == null || node is TreeResource || node is Building) return;
            var v = VillagerOf(instigator);
            var definition = node.Definition;
            var map = MapData.Instance;
            if (v == null || definition == null || map == null) return;
            var s = S;
            switch (definition.Kind)
            {
                case WorldObjectKind.Stone:
                case WorldObjectKind.GoldStone:
                {
                    // Mining: chance of one more resource
                    float chance = s.miningExtraChancePerLevel * v.Stats.Get(StatType.Mining);
                    var item = FirstDrop(definition);
                    if (item != null && Random.value < chance)
                        ItemDropper.Drop(new List<DropEntry> { new DropEntry(item, 1) }, node.Area, node.transform.parent, map);
                    break;
                }
                case WorldObjectKind.Mushroom:
                case WorldObjectKind.Pumpkin:
                case WorldObjectKind.Bush:
                {
                    v.GainXp(StatType.Farming, s.harvestDoneXp);
                    float bonus = s.farmingBonusPerLevel * v.Stats.Get(StatType.Farming);
                    var extras = new List<DropEntry>();
                    if (definition.Drops != null)
                        foreach (var d in definition.Drops)
                        {
                            if (d == null || d.item == null || d.amount <= 0) continue;
                            float more = d.amount * bonus;
                            int whole = Mathf.FloorToInt(more);
                            if (Random.value < more - whole) whole++;
                            if (whole > 0) extras.Add(new DropEntry(d.item, whole));
                        }
                    if (extras.Count > 0) ItemDropper.Drop(extras, node.Area, node.transform.parent, map);
                    break;
                }
            }
        }

        static SpawnableDefinition FirstDrop(SpawnableDefinition definition)
        {
            if (definition.Drops == null) return null;
            foreach (var d in definition.Drops) if (d != null && d.item != null) return d.item;
            return null;
        }

        /// <summary>Forest Keeper (Woodcutting 20): after a day of 5+ trees, a sapling grows on free grass near the woodcutter.</summary>
        public static void PlantSapling(Villager v)
        {
            var map = MapData.Instance;
            if (v == null || map == null || !map.IsReady || BuildingInventory.Resolver == null) return;
            var tree = string.IsNullOrEmpty(v.Memory.lastFelledTree) ? null : BuildingInventory.Resolver(v.Memory.lastFelledTree);
            if (tree == null || tree.Stump == null) return;
            var around = MapData.WorldToCell(v.transform.position);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var cell = around + new Vector2Int(Random.Range(-6, 7), Random.Range(-6, 7));
                var rect = new RectInt(cell, tree.Footprint);
                if (!map.IsLand(cell) || map.IsAreaBlocked(rect)) continue;
                var generator = StartingPartySpawner.Instance != null ? StartingPartySpawner.Instance.Generator : null;
                var parent = generator != null ? generator.EditsRoot : v.transform.parent;
                var stump = WorldObjectFactory.Create(tree.Stump, cell, Vector2.zero, parent, map,
                    configure: o => o.SetLinkedDefinition(tree)) as StumpObject;
                if (stump == null) return;
                var s = S;
                stump.BeginRegrow(s.regrowHours / Mathf.Max(0.1f, s.regrowSpeed20), s.firstChopBonus20);
                v.AddJournal($"Planted a sapling ({tree.DisplayName}) after felling {v.Memory.felledToday} trees.");
                return;
            }
        }
    }
}
