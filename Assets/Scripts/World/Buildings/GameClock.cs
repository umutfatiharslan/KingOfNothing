using System;
using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>
    /// Game time: one game day lasts GameplaySettings.dayLengthMinutes real minutes (24 by default, so one game hour
    /// is one real minute). The game starts at CharacterSettings.startHour. Every new day each complete building
    /// loses health (it needs repairs); every new hour the characters' plans move on. The time is saved with the
    /// characters. Created automatically by the selection system.
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        static GameClock instance;

        /// <summary>Raised at the start of every new day (day number).</summary>
        public static event Action<int> DayPassed;
        /// <summary>Raised at the start of every new hour (hour of the day 0-23).</summary>
        public static event Action<int> HourPassed;

        float elapsed;
        bool started;
        int lastHour = -1;

        public static GameClock Instance => instance;
        /// <summary>Current day, starting at 1.</summary>
        public int Day { get; private set; } = 1;
        /// <summary>Part of the current day that has passed (0..1).</summary>
        public float DayProgress => elapsed / Settings.DaySeconds;
        /// <summary>Hour of the day (0-23).</summary>
        public int Hour => Mathf.Clamp(Mathf.FloorToInt(DayProgress * 24f), 0, 23);
        /// <summary>Game hours since the start of day 1 (with fractions).</summary>
        public float TotalHours => (Day - 1) * 24f + DayProgress * 24f;
        /// <summary>Seconds into the current day (saving).</summary>
        public float ElapsedInDay => elapsed;

        static GameplaySettings Settings => MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;

        /// <summary>Game hours since day 1 (0 if there is no clock yet).</summary>
        public static float Now => instance != null ? instance.TotalHours : 0f;
        /// <summary>Current day (1 if there is no clock yet).</summary>
        public static int Today => instance != null ? instance.Day : 1;
        /// <summary>Current hour of the day.</summary>
        public static int CurrentHour => instance != null ? instance.Hour : 12;
        /// <summary>Converts real seconds to game hours.</summary>
        public static float HoursFromSeconds(float seconds) => seconds / Mathf.Max(0.01f, Settings.HourSeconds);

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

        /// <summary>Sets the time (loading a save).</summary>
        public void SetTime(int day, float secondsIntoDay)
        {
            Day = Mathf.Max(1, day);
            elapsed = Mathf.Clamp(secondsIntoDay, 0f, Settings.DaySeconds - 0.01f);
            started = true;
            lastHour = Hour;
        }

        void Update()
        {
            var map = MapData.Instance;
            if (map == null || !map.IsReady) return;
            if (!started)
            {
                started = true;
                var characters = Settings.characters;
                int startHour = characters != null ? characters.startHour : 6;
                elapsed = Settings.HourSeconds * startHour;
                lastHour = Hour;
            }
            elapsed += Time.deltaTime;
            float day = Settings.DaySeconds;
            while (elapsed >= day)
            {
                elapsed -= day;
                Day++;
                OnNewDay();
            }
            int hour = Hour;
            if (hour != lastHour)
            {
                lastHour = hour;
                HourPassed?.Invoke(hour);
            }
        }

        void OnNewDay()
        {
            int decay = Settings.decayPerDay;
            if (decay > 0)
                foreach (var b in Building.All.ToArray())
                    if (b != null && b.IsComplete) b.DailyDecay(decay); // construction sites wait for their builders
            Debug.Log($"[GameClock] Day {Day} begins." + (decay > 0 ? " Buildings decayed." : ""));
            DayPassed?.Invoke(Day);
        }
    }
}
