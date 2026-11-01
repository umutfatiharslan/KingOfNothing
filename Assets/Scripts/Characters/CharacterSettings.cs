using KingOfNothing.World;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>What a character does in an hour of its day plan.</summary>
    public enum ScheduleBlock
    {
        Work = 0,
        Recreation = 1,
        Sleep = 2,
    }

    /// <summary>
    /// Everything about the characters' life that is not a stat: avatars, starting stats, needs (hunger, sleep,
    /// morale), the default day plan, the routine (settlement, repairs, patrols, recreation), experience popups,
    /// sheep, designation marks and the meat items. An asset (Assets/Data/Characters/CharacterSettings).
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterSettings", menuName = "King of Nothing/Characters/Character Settings")]
    public class CharacterSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/Characters/CharacterSettings.asset";

        [Header("Avatars (character list and bio)")]
        public Sprite warriorAvatar;
        public Sprite lancerAvatar;
        public Sprite archerAvatar;
        public Sprite priestAvatar;
        public Sprite villagerAvatar;

        [Header("Starting stats (every stat rolled on its own)")]
        [Range(0f, 1f)] public float zeroChance = 0.1f;
        [Tooltip("A stat that is not 0 is rolled between these (equal chances).")]
        public int rollMin = 1;
        public int rollMax = 3;
        [Tooltip("Most work / war stats at 0 per character (healing does not count).")]
        [Min(0)] public int maxZeroStats = 3;
        [Tooltip("Life stats (walk speed, hunger) are never 0.")]
        public int lifeMin = 1;
        public int lifeMax = 3;

        [Header("Needs")]
        [Tooltip("Satiety lost per game hour (before the hunger stat).")]
        [Min(0f)] public float satietyLossPerHour = 3f;
        [Tooltip("Below this satiety a character goes to eat.")]
        [Range(0f, 100f)] public float eatThreshold = 50f;
        [Tooltip("Health lost per game hour at 0 satiety (never below 1).")]
        [Min(0f)] public float starvingHealthLossPerHour = 2f;
        [Tooltip("Health regained per game hour of sleep in a house / the castle, and on the ground.")]
        [Min(0f)] public float sleepRegenInBed = 3f;
        [Min(0f)] public float sleepRegenOnGround = 1f;
        [Tooltip("Morale per game hour: recreation (+), sleep (+), work (-).")]
        public float recreationMoralePerHour = 6f;
        public float sleepMoralePerHour = 1f;
        public float workMoraleLossPerHour = 1f;
        [Tooltip("Cells searched for food piles on the ground.")]
        [Min(1)] public int foodSearchRadius = 40;
        [Tooltip("Seconds a meal takes.")]
        [Min(0.1f)] public float mealSeconds = 2f;

        [Header("Day plan")]
        [Tooltip("Hour of the day the game starts at.")]
        [Range(0, 23)] public int startHour = 6;
        [Tooltip("Default plan of new characters, one entry per hour: 0 work, 1 recreation, 2 sleep.")]
        public int[] defaultSchedule = { 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2 };

        [Header("Routine")]
        [Tooltip("Cells around the castle (or the start) that count as the settlement: repairs, hauling, patrols.")]
        [Min(4)] public int settlementRadius = 25;
        [Tooltip("Builders repair settlement buildings below this share of their health on their own.")]
        [Range(0f, 1f)] public float repairBelow = 0.85f;
        [Min(1)] public int maxBuildersPerSite = 4;
        [Tooltip("Patrol ring around the settlement centre (cells) and the pause at every point.")]
        public float patrolRadiusMin = 6f;
        public float patrolRadiusMax = 12f;
        public float patrolPause = 2f;
        [Tooltip("Patrolling soldiers notice enemies within this many cells.")]
        public float detectRadius = 8f;
        [Tooltip("Recreation: wandering radius around the castle / church, chat length.")]
        public float recreationRadius = 6f;
        public float chatSeconds = 5f;
        [Tooltip("Bakers cook when the stores hold fewer cooked meals than this.")]
        [Min(0)] public int foodStockTarget = 10;
        [Tooltip("Archers train this far from a scarecrow (cells).")]
        public float scarecrowArcherDistance = 4.5f;
        [Tooltip("Seconds between two decisions of an idle character.")]
        [Min(0.1f)] public float thinkInterval = 0.5f;

        [Header("Experience popup")]
        public Sprite xpArrow;
        public Color xpColor = new Color(0.55f, 1f, 0.45f, 1f);
        public Color levelUpColor = new Color(1f, 0.85f, 0.25f, 1f);
        [Min(0.1f)] public float popupSeconds = 3f;
        [Min(0.1f)] public float popupFadeSeconds = 2f;
        public float popupRiseSpeed = 0.15f;
        public float popupHeight = 1.6f;
        [Min(0.01f)] public float popupCharacterSize = 0.045f;

        [Header("Designation marks")]
        public Sprite chopMark;
        public Sprite mineMark;
        public Sprite harvestMark;
        public Sprite huntMark;
        [Min(0.05f)] public float markScale = 0.45f;

        [Header("Recreation / sleep icons")]
        public Sprite recreationIcon;

        [Header("Meat and meals")]
        public SpawnableDefinition rawMeat;
        public SpawnableDefinition cookedMeat;
        [Tooltip("Cooked by a baker with Baking 15+ (Feast Meal milestone).")]
        public SpawnableDefinition feastMeat;
        [Tooltip("Cooked by a baker with Baking 20 (Hearth Master milestone, a few per day).")]
        public SpawnableDefinition masterMeat;

        [Header("Sheep")]
        public Sprite[] sheepIdle = new Sprite[0];
        public Sprite[] sheepMove = new Sprite[0];
        public Sprite[] sheepGrass = new Sprite[0];
        [Min(0.05f)] public float sheepScale = 0.5f;
        [Min(1)] public int sheepHealth = 30;
        public float sheepWalkSpeed = 0.8f;
        public float sheepFleeSpeed = 2.4f;
        [Tooltip("Seconds a sheep runs away before it calms down.")]
        public float sheepFleeSeconds = 4f;
        public int sheepMeatMin = 2;
        public int sheepMeatMax = 4;
        [Tooltip("Herds placed when a new game starts, sheep per herd, distance from the start (cells).")]
        [Min(0)] public int herdCount = 6;
        public int herdSizeMin = 3;
        public int herdSizeMax = 5;
        public float herdDistanceMin = 12f;
        public float herdDistanceMax = 45f;
        [Tooltip("A new lamb per herd per day with this chance, up to the maximum number of sheep.")]
        [Range(0f, 1f)] public float lambChancePerDay = 0.3f;
        [Min(0)] public int maxSheep = 40;

        [SerializeField, HideInInspector] int dataVersion;

        static CharacterSettings fallback;

        public static CharacterSettings Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<CharacterSettings>();
                    fallback.name = "CharacterSettings (defaults)";
                    fallback.hideFlags = HideFlags.DontSave;
                }
                return fallback;
            }
        }

        /// <summary>The asset in use (from GameplaySettings), never null.</summary>
        public static CharacterSettings Current
        {
            get
            {
                var map = MapData.Instance;
                var s = map != null ? map.Gameplay.characters : null;
                return s != null ? s : Fallback;
            }
        }

        public Sprite AvatarFor(VillagerRole role)
        {
            switch (role)
            {
                case VillagerRole.Warrior: return warriorAvatar;
                case VillagerRole.Lancer: return lancerAvatar;
                case VillagerRole.Archer: return archerAvatar;
                case VillagerRole.Priest: return priestAvatar;
                default: return villagerAvatar;
            }
        }

        public ScheduleBlock DefaultBlock(int hour)
        {
            if (defaultSchedule == null || defaultSchedule.Length < 24) return hour >= 22 || hour < 6 ? ScheduleBlock.Sleep : hour >= 18 ? ScheduleBlock.Recreation : ScheduleBlock.Work;
            return (ScheduleBlock)Mathf.Clamp(defaultSchedule[Mathf.Clamp(hour, 0, 23)], 0, 2);
        }

#if UNITY_EDITOR
        public int EditorDataVersion
        {
            get => dataVersion;
            set => dataVersion = value;
        }

        public static CharacterSettings LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterSettings>(DefaultPath);
            if (asset != null) return asset;
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Data");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/Characters")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "Characters");
            asset = CreateInstance<CharacterSettings>();
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return asset;
        }
#endif
    }
}
