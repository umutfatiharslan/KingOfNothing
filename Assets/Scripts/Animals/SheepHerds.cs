using KingOfNothing.Characters;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.Animals
{
    /// <summary>
    /// The sheep of the map: herds placed around the start when a new game begins (CharacterSettings: herd count,
    /// size, distance), a lamb per herd now and then (up to a maximum). Saved and loaded by CharacterSave.
    /// </summary>
    public class SheepHerds : MonoBehaviour
    {
        static SheepHerds instance;
        Transform root;

        public static SheepHerds Instance => instance;

        public static SheepHerds Ensure()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<SheepHerds>();
            if (instance == null) instance = new GameObject("Sheep Herds").AddComponent<SheepHerds>();
            return instance;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
        }

        void OnEnable() => GameClock.DayPassed += OnDayPassed;
        void OnDisable() => GameClock.DayPassed -= OnDayPassed;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        Transform Root
        {
            get
            {
                if (root == null)
                {
                    root = new GameObject("Sheep").transform;
                    root.SetParent(transform, false);
                }
                return root;
            }
        }

        public Sheep Add(Vector2 position, int herd, Vector2 home, string id = null, float health = -1f) =>
            Sheep.Create(position, herd, home, Root, id, health);

        public void Clear()
        {
            foreach (var s in Sheep.All.ToArray())
                if (s != null) Destroy(s.gameObject);
        }

        /// <summary>Places the herds of a new game around a point.</summary>
        public void SpawnInitial(Vector2 center)
        {
            var map = MapData.Instance;
            var c = CharacterSettings.Current;
            if (map == null || !map.IsReady) return;
            int placed = 0;
            for (int h = 0; h < c.herdCount; h++)
            {
                if (!FindPasture(map, center, c.herdDistanceMin, c.herdDistanceMax, out var home)) continue;
                int size = Random.Range(c.herdSizeMin, c.herdSizeMax + 1);
                for (int i = 0; i < size; i++)
                {
                    var p = home + Random.insideUnitCircle * 1.5f;
                    if (!map.IsWalkable(MapData.WorldToCell(p))) p = home;
                    Add(p, h, home);
                    placed++;
                }
            }
            Debug.Log($"[Sheep] {placed} sheep in {c.herdCount} herds.");
        }

        static bool FindPasture(MapData map, Vector2 center, float min, float max, out Vector2 point)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(min, max);
                var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                var cell = MapData.WorldToCell(p);
                if (!map.IsLand(cell) || !map.IsWalkable(cell)) continue;
                point = MapData.CellCenter(cell);
                return true;
            }
            point = center;
            return false;
        }

        void OnDayPassed(int day)
        {
            var c = CharacterSettings.Current;
            if (Sheep.All.Count >= c.maxSheep) return;
            // A lamb for some herds (a herd needs at least two sheep)
            var herds = new System.Collections.Generic.Dictionary<int, Sheep>();
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var s in Sheep.All)
            {
                if (s == null) continue;
                herds[s.Herd] = s;
                counts.TryGetValue(s.Herd, out int n);
                counts[s.Herd] = n + 1;
            }
            foreach (var pair in herds)
            {
                if (Sheep.All.Count >= c.maxSheep) break;
                if (counts[pair.Key] < 2 || Random.value > c.lambChancePerDay) continue;
                Add((Vector2)pair.Value.transform.position + Random.insideUnitCircle * 0.5f, pair.Key, pair.Value.Home);
            }
        }
    }
}
