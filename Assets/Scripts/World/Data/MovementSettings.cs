using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Movement and interaction rules of characters, in one asset so every value can be tuned in the Inspector
    /// and stays saved (also when it is changed during Play mode). Per-object rules (walk through, speed inside,
    /// hitbox) live in each object's SpawnableDefinition; per-biome speed lives in each BiomeDefinition.
    /// </summary>
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "King of Nothing/World/Movement Settings")]
    public class MovementSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/World/MovementSettings.asset";

        [Header("Water")]
        [Tooltip("Characters can walk this many cells into the water (counted from the shore). Deeper needs a boat.")]
        [Min(0)] public int wadeableWaterDepth = 5;
        [Tooltip("Speed multiplier while wading in shallow water.")]
        [Range(0.05f, 1f)] public float shallowWaterSpeed = 0.5f;
        [Tooltip("Deeper water needs a boat (a craftable item planned for later). Turn on to let characters walk everywhere for tests.")]
        public bool deepWaterWalkable;
        [Tooltip("Speed multiplier in deep water when Deep Water Walkable is on.")]
        [Range(0.05f, 1f)] public float deepWaterSpeed = 0.35f;

        [Header("Proximity (checked separately for every character)")]
        [Tooltip("Hitboxes, collisions and transparency are active only for objects within this distance (in cells) of a character.")]
        [Min(1f)] public float interactionRadius = 5f;
        [Tooltip("How often the objects around the characters are collected (seconds). 0 = every frame.")]
        [Min(0f)] public float proximityUpdateInterval = 0f;

        [Header("Character body")]
        [Tooltip("Radius (cells) of a character's feet; used to collide with object hitboxes.")]
        [Range(0.05f, 0.45f)] public float characterCollisionRadius = 0.2f;
        [Tooltip("Size (cells) of a character's visible body above its feet; used to know when an object hides it.")]
        public Vector2 characterBodySize = new Vector2(0.6f, 1.1f);
        [Tooltip("A character that has made no progress for this long (seconds) looks for a new path.")]
        [Min(0.1f)] public float stuckRepathTime = 0.75f;

        [Header("Transparency")]
        [Tooltip("Opacity of an object while a character walks through it or stands right behind it (0.75 = 25% transparent).")]
        [Range(0f, 1f)] public float occludedAlpha = 0.75f;
        [Tooltip("How fast objects fade (opacity change per second).")]
        [Min(0.1f)] public float fadeSpeed = 6f;

        [Header("Debug")]
        [Tooltip("Draw the active hitboxes (Scene view gizmos, and the Game view when Gizmos is on).")]
        public bool showHitboxes;
        public Color hitboxColor = new Color(1f, 0.3f, 0.2f, 0.9f);

        [SerializeField, HideInInspector] int dataVersion;

        static MovementSettings fallback;

        /// <summary>Default values, used when no asset is assigned (builds without the asset).</summary>
        public static MovementSettings Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<MovementSettings>();
                    fallback.name = "MovementSettings (defaults)";
                    fallback.hideFlags = HideFlags.DontSave;
                }
                return fallback;
            }
        }

#if UNITY_EDITOR
        const int CurrentDataVersion = 1;

        /// <summary>New default values of newer versions, applied once (editor only).</summary>
        public void EditorEnsureDefaults()
        {
            if (dataVersion >= CurrentDataVersion) return;
            if (dataVersion < 1) occludedAlpha = 0.75f; // 25% transparent behind / inside objects
            dataVersion = CurrentDataVersion;
            if (UnityEditor.AssetDatabase.Contains(this)) UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Loads the default asset, creating it (with the default values) if it does not exist yet.</summary>
        public static MovementSettings LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MovementSettings>(DefaultPath);
            if (asset != null)
            {
                asset.EditorEnsureDefaults();
                return asset;
            }
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Data");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/World")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "World");
            asset = CreateInstance<MovementSettings>();
            asset.dataVersion = CurrentDataVersion;
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return asset;
        }
#endif
    }
}
