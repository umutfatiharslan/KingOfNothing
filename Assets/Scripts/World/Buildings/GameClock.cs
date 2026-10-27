using System;
using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>
    /// Simple day counter: one game day lasts GameplaySettings.dayLengthMinutes real minutes (24 by default).
    /// Every new day each complete building loses GameplaySettings.decayPerDay health and needs repairs.
    /// Created automatically by the selection system.
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        static GameClock instance;

        /// <summary>Raised at the start of every new day (day number).</summary>
        public static event Action<int> DayPassed;

        float elapsed;

        public static GameClock Instance => instance;
        /// <summary>Current day, starting at 1.</summary>
        public int Day { get; private set; } = 1;
        /// <summary>Part of the current day that has passed (0..1).</summary>
        public float DayProgress => elapsed / Settings.DaySeconds;

        static GameplaySettings Settings => MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;

        public static GameClock Ensure()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<GameClock>();
            if (instance == null) instance = new GameObject("Game Clock").AddComponent<GameClock>();
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

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return;
            elapsed += Time.deltaTime;
            float day = Settings.DaySeconds;
            while (elapsed >= day)
            {
                elapsed -= day;
                Day++;
                OnNewDay();
            }
        }

        void OnNewDay()
        {
            int decay = Settings.decayPerDay;
            if (decay > 0)
                foreach (var b in Building.All.ToArray())
                    if (b != null && b.IsComplete) b.Decay(decay); // construction sites wait for their builders
            Debug.Log($"[GameClock] Day {Day} begins." + (decay > 0 ? $" Buildings lost {decay} health." : ""));
            DayPassed?.Invoke(Day);
        }
    }
}
