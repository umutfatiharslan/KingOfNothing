using System;
using System.Collections.Generic;
using System.IO;
using KingOfNothing.Animals;
using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Persistence;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// The characters of this computer's game: every character (name, class, position, building it is in, stats
    /// with experience and training progress, needs, traits, day plan, work priorities, journal, counters), the
    /// sheep, the marked objects and the game time. A JSON file next to the edited map
    /// (persistentDataPath/Saves/&lt;map&gt;_characters.json), written every 30 seconds and when the game closes.
    /// Only used while the original map is shown (its changes are saved too). Developer panel: Reset characters.
    /// </summary>
    public static class CharacterSave
    {
        const int Version = 1;
        const float AutosaveSeconds = 30f;

        [Serializable]
        public class VillagerSave
        {
            public string id;
            public string name;
            public int age;
            public int role;
            public float x, y;
            public int hostUid;
            public bool perched;
            public List<StatProgress> stats = new List<StatProgress>();
            public float health, satiety, morale;
            public List<string> traits = new List<string>();
            public List<bool> traitsRevealed = new List<bool>();
            public int[] schedule;
            public List<WorkPriority> priorities = new List<WorkPriority>();
            public List<JournalEntry> journal = new List<JournalEntry>();
            public VillagerMemory memory;
            public TrainingAssignment training;
        }

        [Serializable]
        public class SheepSave
        {
            public string id;
            public float x, y, homeX, homeY, health;
            public int herd;
            public bool marked;
        }

        [Serializable]
        public class Data
        {
            public int version = Version;
            public int day = 1;
            public float secondsIntoDay;
            public List<VillagerSave> villagers = new List<VillagerSave>();
            public List<SheepSave> sheep = new List<SheepSave>();
            public List<int> designations = new List<int>();
        }

        static StartingPartySpawner owner;
        static float nextSave;
        static bool active;

        static string PathFor(MapGenerator generator) =>
            System.IO.Path.Combine(EditedMapFile.Folder, (generator != null ? generator.EditedMapFileName : "map") + "_characters.json");

        public static string FilePath => owner != null ? PathFor(owner.Generator) : null;

        static bool Saves(StartingPartySpawner spawner) =>
            spawner != null && spawner.Generator != null && spawner.Generator.ShowsOriginalMap && spawner.Generator.History != null;

        /// <summary>Loads the saved characters (false: no save, a new game has to start).</summary>
        public static bool TryLoad(StartingPartySpawner spawner)
        {
            owner = spawner;
            active = Saves(spawner);
            EnsureRunner(spawner);
            if (!active) return false;
            string path = PathFor(spawner.Generator);
            if (!File.Exists(path)) return false;
            Data data;
            try { data = JsonUtility.FromJson<Data>(File.ReadAllText(path)); }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterSave] Could not read {path}: {e.Message}");
                return false;
            }
            if (data == null || data.villagers == null || data.villagers.Count == 0) return false;
            Apply(spawner, data);
            Debug.Log($"[CharacterSave] {data.villagers.Count} characters and {data.sheep.Count} sheep loaded (day {data.day}).");
            return true;
        }

        static void Apply(StartingPartySpawner spawner, Data data)
        {
            GameClock.Ensure().SetTime(data.day, data.secondsIntoDay);
            var map = MapData.Instance;
            foreach (var s in data.villagers)
            {
                var v = spawner.CreateVillager(new Vector2(s.x, s.y));
                if (v == null) continue;
                v.Stats.Load(s.stats);
                v.LoadFrom(s.id, s.name, s.age, (VillagerRole)s.role);
                v.LoadMemory(s.memory);
                v.LoadTraining(s.training);
                v.SetSchedule(s.schedule);
                v.LoadPriorities(s.priorities);
                v.LoadJournal(s.journal);
                for (int i = 0; i < s.traits.Count; i++)
                {
                    var trait = FindTrait(spawner, s.traits[i]);
                    if (trait != null) v.AddTrait(trait, i < s.traitsRevealed.Count && s.traitsRevealed[i]);
                }
                var needs = v.Needs;
                needs.Health.max = v.MaxHealth;
                needs.Health.current = Mathf.Clamp(s.health, 1f, needs.Health.max);
                needs.Satiety.current = Mathf.Clamp(s.satiety, 0f, needs.Satiety.max);
                needs.Morale.current = Mathf.Clamp(s.morale, 0f, needs.Morale.max);
                if (s.hostUid > 0 && map != null && map.FindObject(s.hostUid) is Building b)
                {
                    var unit = v.Movement;
                    if (s.perched) b.Climb(unit);
                    else b.Enter(unit);
                }
            }
            var herds = SheepHerds.Ensure();
            herds.Clear();
            foreach (var s in data.sheep)
            {
                var sheep = herds.Add(new Vector2(s.x, s.y), s.herd, new Vector2(s.homeX, s.homeY), s.id, s.health);
                if (sheep != null && s.marked) sheep.SetMarked(true);
            }
            Designations.Load(data.designations);
        }

        static TraitDefinition FindTrait(StartingPartySpawner spawner, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in spawner.TraitPool) if (t != null && t.name == name) return t;
            foreach (var t in Resources.FindObjectsOfTypeAll<TraitDefinition>()) if (t != null && t.name == name) return t;
            return null;
        }

        /// <summary>Writes the file now.</summary>
        public static void Save()
        {
            if (!active || owner == null || !Saves(owner)) return;
            var data = new Data();
            var clock = GameClock.Instance;
            if (clock != null)
            {
                data.day = clock.Day;
                data.secondsIntoDay = clock.ElapsedInDay;
            }
            foreach (var v in Villager.All)
            {
                if (v == null) continue;
                var unit = v.Movement;
                var host = unit != null ? unit.Host as Building : null;
                var s = new VillagerSave
                {
                    id = v.Id,
                    name = v.DisplayName,
                    age = v.Identity.age,
                    role = (int)v.Role,
                    x = v.transform.position.x,
                    y = v.transform.position.y,
                    hostUid = host != null ? host.Uid : 0,
                    perched = unit != null && unit.IsPerched,
                    stats = v.Stats.Snapshot(),
                    health = v.Needs.Health.current,
                    satiety = v.Needs.Satiety.current,
                    morale = v.Needs.Morale.current,
                    schedule = v.ScheduleCopy(),
                    priorities = v.PrioritiesCopy(),
                    journal = new List<JournalEntry>(v.Journal),
                    memory = v.Memory,
                    training = v.Training,
                };
                if (host != null && unit != null && !unit.IsPerched)
                {
                    // Standing at the door when the building is gone next time
                    var door = host.DoorPoint;
                    s.x = door.x;
                    s.y = door.y;
                }
                foreach (var t in v.Traits)
                {
                    if (t == null || t.definition == null) continue;
                    s.traits.Add(t.definition.name);
                    s.traitsRevealed.Add(t.revealed);
                }
                data.villagers.Add(s);
            }
            foreach (var sheep in Sheep.All)
            {
                if (sheep == null || !sheep.IsAlive) continue;
                data.sheep.Add(new SheepSave
                {
                    id = sheep.Id,
                    x = sheep.transform.position.x,
                    y = sheep.transform.position.y,
                    homeX = sheep.Home.x,
                    homeY = sheep.Home.y,
                    health = sheep.Health,
                    herd = sheep.Herd,
                    marked = sheep.MarkedForHunt,
                });
            }
            data.designations = Designations.SavedUids();
            string path = PathFor(owner.Generator);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterSave] Could not write {path}: {e.Message}");
            }
        }

        /// <summary>Deletes the save and starts the characters from scratch (developer panel).</summary>
        public static void ResetCharacters()
        {
            if (owner == null) owner = UnityEngine.Object.FindAnyObjectByType<StartingPartySpawner>();
            if (owner == null) return;
            string path = PathFor(owner.Generator);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogError($"[CharacterSave] Could not delete {path}: {e.Message}"); }
            Designations.Load(null);
            owner.SpawnNewGame();
            Debug.Log("[CharacterSave] Characters reset: a new party and new sheep.");
            Save();
        }

        static void EnsureRunner(StartingPartySpawner spawner)
        {
            if (spawner == null || spawner.GetComponent<Runner>() != null) return;
            spawner.gameObject.AddComponent<Runner>();
        }

        /// <summary>Autosave and save on quit.</summary>
        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                if (Time.unscaledTime < nextSave) return;
                nextSave = Time.unscaledTime + AutosaveSeconds;
                if (Villager.All.Count > 0) Save();
            }

            void OnApplicationQuit() => Save();
        }
    }
}
