using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// How characters work on map objects (hitting, carrying), how objects react (shaking, dropping items) and the
    /// colours of object selection and placement previews. An asset, so every value can be tuned in the Inspector
    /// and stays saved, also when it is changed during Play mode.
    /// </summary>
    [CreateAssetMenu(fileName = "InteractionSettings", menuName = "King of Nothing/World/Interaction Settings")]
    public class InteractionSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/World/InteractionSettings.asset";

        [Header("Hitting (until character stats exist every villager hits for the same damage)")]
        [Tooltip("Damage of one hit by a villager on any object.")]
        [Min(1)] public int villagerHitDamage = 10;
        [Tooltip("Seconds between two hits.")]
        [Min(0.05f)] public float hitInterval = 0.8f;
        [Tooltip("How close (cells) a character must be to an object's hitbox to hit it.")]
        [Min(0.1f)] public float interactRange = 0.75f;
        [Tooltip("How far the body leans towards the object on a hit (cells). 0 = only the pawn animation.")]
        [Min(0f)] public float hitLunge = 0f;
        [Tooltip("Moment of the hit inside one swing of the interact animation (0 = start, 1 = end).")]
        [Range(0f, 1f)] public float hitImpactPoint = 0.6f;

        [Header("Hit shake (objects wobble when they are hit)")]
        [Min(0f)] public float shakeAmplitude = 0.07f;
        [Min(0.01f)] public float shakeDuration = 0.25f;
        [Min(1f)] public float shakeFrequency = 32f;

        [Header("Drops")]
        [Tooltip("Dropped piles are put on free cells at most this far (cells) from the broken object.")]
        [Min(1)] public int dropSearchRadius = 3;
        [Tooltip("Random shift (cells) of every pile inside its cell, so piles do not look like a grid.")]
        [Range(0f, 0.4f)] public float dropScatter = 0.2f;

        [Header("Carrying (Default mode)")]
        [Tooltip("Height (cells) above the feet where a carried object is held.")]
        [Min(0f)] public float carryHeight = 1.0f;
        [Tooltip("Walking speed multiplier while carrying something.")]
        [Range(0.1f, 1f)] public float carrySpeedMultiplier = 0.85f;
        [Tooltip("When the target cell got occupied meanwhile, the object is put on the nearest free cell within this radius.")]
        [Min(0)] public int dropFallbackRadius = 3;

        [Header("Selection and previews")]
        [Tooltip("Outline of selected objects.")]
        public Color selectionOutline = new Color(1f, 0.55f, 0.1f, 1f);
        [Tooltip("See-through colour laid over selected objects.")]
        public Color selectionOverlay = new Color(1f, 0.55f, 0.1f, 0.3f);
        [Tooltip("Outline thickness in pixels (64 pixels = 1 cell).")]
        [Range(0.5f, 4f)] public float selectionOutlinePixels = 1.5f;
        [Tooltip("Tint of selected objects (multiplies the sprite colour).")]
        public Color selectedTint = new Color(1f, 0.72f, 0.38f, 1f);
        [Tooltip("Tint of a placement preview that overlaps another object (not allowed in Default mode).")]
        public Color overlapTint = new Color(1f, 0.45f, 0.45f, 0.75f);
        [Tooltip("Tint of a valid placement preview.")]
        public Color previewTint = new Color(1f, 1f, 1f, 0.6f);
        [Tooltip("Mouse movement (pixels) before a click becomes a drag (box selection).")]
        [Min(1f)] public float dragThreshold = 6f;

        [Header("Health bars")]
        [Tooltip("SmallBar_Base sprite (frame of the bar).")]
        public Sprite healthBarBase;
        [Tooltip("SmallBar_Fill sprite (inner progress).")]
        public Sprite healthBarFill;
        [Tooltip("Seconds the bar stays after the last hit.")]
        [Min(0f)] public float healthBarShowSeconds = 3f;
        [Tooltip("Seconds of the fade out afterwards.")]
        [Min(0.01f)] public float healthBarFadeSeconds = 1f;
        [Tooltip("Size of the bar (1 = sprite size, about 1.5 cells wide).")]
        [Min(0.1f)] public float healthBarScale = 0.6f;
        [Tooltip("Gap (cells) between the top of the object and the bar.")]
        public float healthBarOffset = 0.15f;
        [Tooltip("Above this health share the bar is green, below it yellow.")]
        [Range(0f, 1f)] public float healthBarGreenAbove = 0.6f;
        [Tooltip("Below this health share the bar is red.")]
        [Range(0f, 1f)] public float healthBarRedBelow = 0.25f;
        public Color healthBarGreen = new Color(0.35f, 0.85f, 0.3f, 1f);
        public Color healthBarYellow = new Color(0.95f, 0.8f, 0.2f, 1f);
        public Color healthBarRed = new Color(0.9f, 0.25f, 0.2f, 1f);

        [Header("Rendering")]
        [Tooltip("Shader that draws a sprite as one solid colour (outlines, bar colours). Empty = built-in fallback.")]
        public Shader silhouetteShader;

        [Header("Formation (right drag)")]
        [Tooltip("Distance (cells) between characters placed along a dragged line.")]
        [Min(0.3f)] public float formationSpacing = 1f;

        [Header("Developer tools")]
        [Tooltip("Damage of the developer Hit tool.")]
        [Min(1)] public int developerHitPower = 10;
        [Tooltip("Brush: minimum distance (cells) between two objects placed while dragging.")]
        [Min(0.5f)] public float brushSpacing = 1f;

        [SerializeField, HideInInspector] int dataVersion;

        static InteractionSettings fallback;

        /// <summary>Default values, used when no asset is assigned.</summary>
        public static InteractionSettings Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<InteractionSettings>();
                    fallback.name = "InteractionSettings (defaults)";
                    fallback.hideFlags = HideFlags.DontSave;
                }
                return fallback;
            }
        }

#if UNITY_EDITOR
        public const string HealthBarBasePath = "Assets/Tiny Swords/UI Elements/Bars/SmallBar_Base.png";
        public const string HealthBarFillPath = "Assets/Tiny Swords/UI Elements/Bars/SmallBar_Fill.png";
        public const string SilhouetteShaderPath = "Assets/Shaders/SpriteSilhouette.shader";
        const int CurrentDataVersion = 2;

        /// <summary>Fills references that were added in newer versions (bar sprites, shader) and new defaults (editor only).</summary>
        public void EditorEnsureDefaults()
        {
            bool changed = false;
            if (healthBarBase == null) { healthBarBase = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HealthBarBasePath); changed |= healthBarBase != null; }
            if (healthBarFill == null) { healthBarFill = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HealthBarFillPath); changed |= healthBarFill != null; }
            if (silhouetteShader == null) { silhouetteShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(SilhouetteShaderPath); changed |= silhouetteShader != null; }
            if (dataVersion < 1) hitLunge = 0f; // the pawn interact animations show the hit; no body lean by default
            if (dataVersion < 2) selectedTint = new Color(1f, 0.72f, 0.38f, 1f); // orange selection tint
            if (dataVersion < CurrentDataVersion)
            {
                dataVersion = CurrentDataVersion;
                changed = true;
            }
            if (changed && UnityEditor.AssetDatabase.Contains(this)) UnityEditor.EditorUtility.SetDirty(this);
        }

        public static InteractionSettings LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractionSettings>(DefaultPath);
            if (asset != null)
            {
                asset.EditorEnsureDefaults();
                return asset;
            }
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Data");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/World")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "World");
            asset = CreateInstance<InteractionSettings>();
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            asset.EditorEnsureDefaults();
            UnityEditor.AssetDatabase.SaveAssets();
            return asset;
        }
#endif
    }
}
