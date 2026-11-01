using System;
using System.Collections.Generic;
using KingOfNothing.Animals;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// Starts the game: three exiles thrown out into the generated land (two villagers and a warrior; there is no
    /// prince / king any more, everybody starts as a normal character), and the sheep herds around them. When a
    /// character save exists (CharacterSave), the saved characters and sheep come back instead.
    /// </summary>
    public class StartingPartySpawner : MonoBehaviour
    {
        [Serializable]
        public class PartyMember
        {
            public string displayName = "Exile";
            [Tooltip("Leave the name empty to pick one from the Name Pool.")]
            public bool randomName = true;
            public VillagerRole role = VillagerRole.Villager;
            public IntRange age = new IntRange(18, 40);
        }

        [SerializeField] MapGenerator generator;
        [SerializeField] Villager villagerPrefab;
        [SerializeField] NamePool namePool;
        [SerializeField] List<PartyMember> party = new List<PartyMember>
        {
            new PartyMember { randomName = true, role = VillagerRole.Villager, age = new IntRange(19, 30) },
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

        public static StartingPartySpawner Instance { get; private set; }
        public IReadOnlyList<Villager> Spawned => spawned;
        public IReadOnlyList<TraitDefinition> TraitPool => traitPool;
        public MapGenerator Generator => generator;

        void Awake() => Instance = this;

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
            if (Instance == this) Instance = null;
        }

        void OnMapGenerated(MapGenerator _) => Spawn();

        /// <summary>Creates a character (stats are not rolled: the caller sets them).</summary>
        public Villager CreateVillager(Vector2 position)
        {
            if (villagerPrefab == null)
            {
                Debug.LogError("[StartingPartySpawner] Villager Prefab is not assigned.", this);
                return null;
            }
            var villager = Instantiate(villagerPrefab, position, Quaternion.identity, transform);
            spawned.Add(villager);
            return villager;
        }

        /// <summary>Adds one character with a profession at a position (developer tools).</summary>
        public Villager SpawnVillager(VillagerRole role, Vector2 position)
        {
            var villager = CreateVillager(position);
            if (villager == null) return null;
            string villagerName = namePool != null ? namePool.PickRandom() : $"{role} {spawned.Count}";
            StatRoller.Roll(villager.Stats, role);
            villager.Setup(villagerName, UnityEngine.Random.Range(18, 45), role);
            return villager;
        }

        /// <summary>Removes every character (Reset characters).</summary>
        public void ClearAll()
        {
            foreach (var v in spawned)
                if (v != null) Destroy(v.gameObject);
            spawned.Clear();
            foreach (var v in Villager.All.ToArray())
                if (v != null) Destroy(v.gameObject);
        }

        public void Spawn()
        {
            ClearAll();
            if (CharacterSave.TryLoad(this)) return;
            SpawnNewGame();
        }

        /// <summary>A new game: the exiles and the sheep herds.</summary>
        public void SpawnNewGame()
        {
            ClearAll();
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

                var villager = CreateVillager(position);
                if (villager == null) continue;
                // The old prince (king) of earlier scenes starts as a normal villager with a normal name
                bool oldPrince = member.role == VillagerRole.King;
                string memberName = member.randomName || oldPrince || string.IsNullOrEmpty(member.displayName)
                    ? (namePool != null ? namePool.PickRandom() : "Exile " + (i + 1))
                    : member.displayName;
                var role = member.role == VillagerRole.King ? VillagerRole.Villager : member.role;
                StatRoller.Roll(villager.Stats, role);
                villager.Setup(memberName, UnityEngine.Random.Range(member.age.min, member.age.max + 1), role);
                if (traitPool.Count > 0 && UnityEngine.Random.value < traitChance)
                {
                    var trait = traitPool[UnityEngine.Random.Range(0, traitPool.Count)];
                    // Traits that trigger at game start are known from the beginning; the others stay hidden
                    if (trait != null) villager.AddTrait(trait, trait.Trigger == TraitTrigger.GameStart);
                }
            }
            var herds = SheepHerds.Ensure();
            herds.Clear();
            herds.SpawnInitial(center);
        }
    }

    /// <summary>
    /// Starting stats: every work / war stat is rolled on its own (10% 0 = can never do it, otherwise 1, 2 or 3 with
    /// equal chances), at most 3 of them at 0; life stats (walk speed, hunger) 1-3 (never 0); healing starts at 0
    /// for everybody (priests get 1). Soldiers can always use their own weapon (at least 1). Numbers in
    /// CharacterSettings.
    /// </summary>
    public static class StatRoller
    {
        public static void Roll(StatBlock stats, VillagerRole role)
        {
            var c = CharacterSettings.Current;
            var rolled = new List<StatType>();
            foreach (var s in StatInfo.Work) rolled.Add(s);
            foreach (var s in StatInfo.War) rolled.Add(s);
            var zeros = new List<StatType>();
            foreach (var s in rolled)
            {
                int level = UnityEngine.Random.value < c.zeroChance ? 0 : UnityEngine.Random.Range(c.rollMin, c.rollMax + 1);
                stats.Set(s, level);
                if (level == 0) zeros.Add(s);
            }
            // At most a few zeros: the extra ones get a normal roll
            while (zeros.Count > c.maxZeroStats)
            {
                int i = UnityEngine.Random.Range(0, zeros.Count);
                stats.Set(zeros[i], UnityEngine.Random.Range(c.rollMin, c.rollMax + 1));
                zeros.RemoveAt(i);
            }
            foreach (var s in StatInfo.Life) stats.Set(s, UnityEngine.Random.Range(Mathf.Max(1, c.lifeMin), Mathf.Max(1, c.lifeMax) + 1));
            stats.Set(StatType.Healing, role == VillagerRole.Priest ? 1 : 0);
            // A soldier can always use the weapon of its class
            var weapon = WeaponStat(role);
            if (weapon.HasValue && !stats.CanDo(weapon.Value)) stats.Set(weapon.Value, UnityEngine.Random.Range(c.rollMin, c.rollMax + 1));
        }

        /// <summary>The stat a class fights with (null for workers and priests).</summary>
        public static StatType? WeaponStat(VillagerRole role)
        {
            switch (role)
            {
                case VillagerRole.Warrior: return StatType.Sword;
                case VillagerRole.Lancer: return StatType.Spear;
                case VillagerRole.Archer: return StatType.Archery;
                default: return null;
            }
        }
    }
}
