using System;
using System.Collections.Generic;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// Roadmap: the game starts with three exiles - the former prince (future king) and his two servants /
    /// guards - thrown out into the generated land. Spawns them at the map's start cell.
    /// </summary>
    public class StartingPartySpawner : MonoBehaviour
    {
        [Serializable]
        public class PartyMember
        {
            public string displayName = "Exile";
            [Tooltip("Leave the name empty to pick one from the Name Pool.")]
            public bool randomName;
            public VillagerRole role = VillagerRole.Villager;
            public IntRange age = new IntRange(18, 40);
            [Tooltip("Each stat is rolled in this range.")]
            public IntRange stats = new IntRange(0, 3);
        }

        [SerializeField] MapGenerator generator;
        [SerializeField] Villager villagerPrefab;
        [SerializeField] NamePool namePool;
        [SerializeField] List<PartyMember> party = new List<PartyMember>
        {
            new PartyMember { displayName = "Prince", role = VillagerRole.King, age = new IntRange(19, 24), stats = new IntRange(1, 3) },
            new PartyMember { randomName = true, role = VillagerRole.Warrior, age = new IntRange(25, 40) },
            new PartyMember { randomName = true, role = VillagerRole.Villager, age = new IntRange(20, 45) },
        };
        [Min(0.2f)] [SerializeField] float spacing = 1.2f;

        [Header("Traits")]
        [Tooltip("Traits the exiles may start with (e.g. Hasty walks faster).")]
        [SerializeField] List<TraitDefinition> traitPool = new List<TraitDefinition>();
        [Tooltip("Chance per party member to get one random trait from the pool.")]
        [Range(0f, 1f)] [SerializeField] float traitChance = 0.5f;

        readonly List<Villager> spawned = new List<Villager>();

        public IReadOnlyList<Villager> Spawned => spawned;

        void Start()
        {
            if (generator == null) generator = FindAnyObjectByType<MapGenerator>();
            if (generator == null)
            {
                Debug.LogWarning("[StartingPartySpawner] No MapGenerator in the scene.", this);
                return;
            }
            generator.MapGenerated += OnMapGenerated;
            if (generator.HasMap) Spawn();
        }

        void OnDestroy()
        {
            if (generator != null) generator.MapGenerated -= OnMapGenerated;
        }

        void OnMapGenerated(MapGenerator _) => Spawn();

        /// <summary>Adds one character with a profession at a position (developer tools).</summary>
        public Villager SpawnVillager(VillagerRole role, Vector2 position)
        {
            if (villagerPrefab == null)
            {
                Debug.LogError("[StartingPartySpawner] Villager Prefab is not assigned.", this);
                return null;
            }
            var villager = Instantiate(villagerPrefab, position, Quaternion.identity, transform);
            string villagerName = namePool != null ? namePool.PickRandom() : $"{role} {spawned.Count + 1}";
            villager.Setup(villagerName, UnityEngine.Random.Range(18, 45), role);
            villager.Stats.Randomize(0, 3);
            spawned.Add(villager);
            return villager;
        }

        public void Spawn()
        {
            foreach (var v in spawned)
                if (v != null) Destroy(v.gameObject);
            spawned.Clear();

            if (villagerPrefab == null)
            {
                Debug.LogError("[StartingPartySpawner] Villager Prefab is not assigned.", this);
                return;
            }
            var start = generator.StartCell;
            if (start.x < 0)
            {
                Debug.LogWarning("[StartingPartySpawner] The map has no valid start cell.", this);
                return;
            }

            Vector2 center = MapData.CellCenter(start);
            for (int i = 0; i < party.Count; i++)
            {
                var member = party[i];
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, party.Count);
                Vector2 position = party.Count == 1 ? center : center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spacing;

                var villager = Instantiate(villagerPrefab, position, Quaternion.identity, transform);
                string memberName = member.randomName || string.IsNullOrEmpty(member.displayName)
                    ? (namePool != null ? namePool.PickRandom() : "Exile " + (i + 1))
                    : member.displayName;
                villager.Setup(memberName, UnityEngine.Random.Range(member.age.min, member.age.max + 1), member.role);
                villager.Stats.Randomize(member.stats.min, member.stats.max);
                if (traitPool.Count > 0 && UnityEngine.Random.value < traitChance)
                {
                    var trait = traitPool[UnityEngine.Random.Range(0, traitPool.Count)];
                    // Traits that trigger at game start are known from the beginning; the others stay hidden
                    if (trait != null) villager.AddTrait(trait, trait.Trigger == TraitTrigger.GameStart);
                }
                spawned.Add(villager);
            }
        }
    }
}
