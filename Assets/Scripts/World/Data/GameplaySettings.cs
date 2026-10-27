using System.Collections.Generic;
using KingOfNothing.Units;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.World
{
    /// <summary>
    /// Kingdom rules that are not about single objects: the game day, construction speed and building decay, the
    /// buildings of the build menu, combat values of the soldier classes and their animation sets. An asset, so
    /// every value can be tuned in the Inspector and stays saved, also when it is changed during Play mode.
    /// The values of each building (health, capacity, inventory, tower / church / furnace rules) are in its
    /// BuildingDefinition asset (Assets/Data/World/Buildings).
    /// </summary>
    [CreateAssetMenu(fileName = "GameplaySettings", menuName = "King of Nothing/World/Gameplay Settings")]
    public class GameplaySettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/World/GameplaySettings.asset";

        [Header("Time")]
        [Tooltip("Length of one game day in real minutes.")]
        [Min(0.05f)] public float dayLengthMinutes = 24f;

        [Header("Construction")]
        [Tooltip("Key that opens / closes the build menu.")]
        public Key buildMenuKey = Key.B;
        [Tooltip("Health a villager adds to a building with one hammer swing (building and repairing).")]
        [Min(1)] public int buildPowerPerHit = 50;
        [Tooltip("Seconds between two hammer swings.")]
        [Min(0.05f)] public float buildInterval = 0.8f;
        [Tooltip("Health a new construction site starts with (a template until villagers start building).")]
        [Min(1)] public int constructionStartHealth = 1;
        [Tooltip("Opacity of a template (a placed building nobody has started yet): 0.75 = 25% transparent.")]
        [Range(0.05f, 1f)] public float templateAlpha = 0.75f;
        [Tooltip("A construction site torn down with Destroy leaves this share of its building cost on the ground as loot (0.5 = 50%).")]
        [Range(0f, 1f)] public float constructionSalvageShare = 0.5f;
        [Tooltip("Health every building loses per game day (it needs repairs).")]
        [Min(0)] public int decayPerDay = 1;
        [Tooltip("Buildings offered by the build menu, in this order.")]
        public List<BuildingDefinition> buildMenu = new List<BuildingDefinition>();
        [Tooltip("Tint of a valid building preview.")]
        public Color placementValidTint = new Color(1f, 1f, 1f, 0.6f);
        [Tooltip("Tint of a building preview on a place where it cannot be built.")]
        public Color placementInvalidTint = new Color(1f, 0.35f, 0.35f, 0.6f);
        [Tooltip("Sorting order of objects drawn under the characters (bridges). Ground tiles use about -40.")]
        public int underCharactersSortingOrder = -5;

        [Header("Treasury")]
        [Tooltip("The gold coin item (furnace output, church payments).")]
        public SpawnableDefinition goldCoin;
        [Tooltip("Items always shown in the resource bar at the top left (others appear once they are stored).")]
        public List<SpawnableDefinition> resourceBarItems = new List<SpawnableDefinition>();

        [Header("Classes (animations of each role)")]
        public PawnAnimationSet villagerAnimations;
        public PawnAnimationSet warriorAnimations;
        public PawnAnimationSet lancerAnimations;
        public PawnAnimationSet archerAnimations;
        public PawnAnimationSet priestAnimations;

        [Header("Combat (until stats exist)")]
        [Tooltip("Damage of one sword hit.")]
        [Min(0)] public int warriorDamage = 15;
        [Tooltip("Damage of one spear hit.")]
        [Min(0)] public int lancerDamage = 20;
        [Tooltip("Damage of one arrow.")]
        [Min(0)] public int archerDamage = 10;
        [Tooltip("Seconds between two sword / spear hits.")]
        [Min(0.05f)] public float meleeAttackInterval = 0.8f;
        [Tooltip("Seconds between two arrows.")]
        [Min(0.05f)] public float archerAttackInterval = 1.2f;
        [Tooltip("Range of the archers in cells (later it grows with the archery stat).")]
        [Min(1f)] public float archerRange = 10f;
        [Tooltip("Moment the arrow leaves the bow inside the shoot animation (0 = start, 1 = end).")]
        [Range(0f, 1f)] public float arrowReleasePoint = 0.7f;
        [Tooltip("Arrow flight speed (cells per second).")]
        [Min(0.5f)] public float arrowSpeed = 12f;
        [Tooltip("Height (cells) of the arrow's arc per 10 cells of distance.")]
        [Min(0f)] public float arrowArc = 1.2f;
        [Tooltip("Height (cells) above the feet where the arrow leaves the bow.")]
        public float arrowLaunchHeight = 0.6f;
        public Sprite arrowSprite;
        [Tooltip("Size of the arrow sprite.")]
        [Min(0.05f)] public float arrowScale = 0.55f;
        [Tooltip("Air trail behind a flying arrow: length in seconds (0 = none), width and colour.")]
        [Min(0f)] public float arrowTrailTime = 0.18f;
        [Min(0.005f)] public float arrowTrailWidth = 0.06f;
        public Color arrowTrailColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("Chance to miss with archery 0 and with archery 20 (in between it goes down evenly).")]
        [Range(0f, 1f)] public float missChanceAtArcheryZero = 0.4f;
        [Range(0f, 1f)] public float missChanceAtArcheryMax = 0.05f;
        [Tooltip("How far (cells) a missed arrow lands from the target.")]
        public Vector2 missDistance = new Vector2(0.6f, 1.6f);
        [Tooltip("Arrow without a head stuck in the ground after a miss.")]
        public Sprite stuckArrowSprite;
        [Tooltip("Game hours a missed arrow stays on the ground (1 game hour = 1/24 of a day).")]
        [Min(0f)] public float stuckArrowGameHours = 3f;

        [Header("Game over")]
        [Tooltip("Stop the game (time scale 0) when the castle falls.")]
        public bool pauseOnGameOver = true;

        [SerializeField, HideInInspector] int dataVersion;

        static GameplaySettings fallback;

        /// <summary>Default values, used when no asset is assigned.</summary>
        public static GameplaySettings Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<GameplaySettings>();
                    fallback.name = "GameplaySettings (defaults)";
                    fallback.hideFlags = HideFlags.DontSave;
                }
                return fallback;
            }
        }

        /// <summary>Seconds of one game day.</summary>
        public float DaySeconds => Mathf.Max(3f, dayLengthMinutes * 60f);

        /// <summary>Seconds of one game hour.</summary>
        public float HourSeconds => DaySeconds / 24f;

#if UNITY_EDITOR
        public int EditorDataVersion
        {
            get => dataVersion;
            set => dataVersion = value;
        }

        public static GameplaySettings LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameplaySettings>(DefaultPath);
            if (asset != null) return asset;
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Data");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/World")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "World");
            asset = CreateInstance<GameplaySettings>();
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return asset;
        }
#endif
    }
}
