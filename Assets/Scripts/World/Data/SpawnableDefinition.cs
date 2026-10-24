using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Everything about one kind of map object: its sprites, footprint and gameplay values.
    /// Biomes refer to these from their spawn rules. Either give it sprite frames (the object is built in code)
    /// or a prefab (the prefab is instantiated).
    /// </summary>
    [CreateAssetMenu(fileName = "Spawnable", menuName = "King of Nothing/World/Spawnable Object")]
    public class SpawnableDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] string displayName = "Object";
        [SerializeField] WorldObjectKind kind = WorldObjectKind.Decoration;

        [Header("Visuals")]
        [Tooltip("Sprite or animation frames (left to right, top to bottom).")]
        [SerializeField] Sprite[] frames = new Sprite[0];
        [SerializeField] bool animate = true;
        [Min(0f)] [SerializeField] float animationFps = 10f;
        [Tooltip("Optional prefab. When set it is instantiated instead of building the object from the frames.")]
        [SerializeField] GameObject prefabOverride;

        [Header("Placement")]
        [Tooltip("Cells covered on the map grid (e.g. a gold mine is 3x2).")]
        [SerializeField] Vector2Int footprint = Vector2Int.one;
        [Tooltip("Transparent space under the visible base of the sprite, in units (pixels / 64).")]
        [SerializeField] float groundInset = 0.3f;
        [Tooltip("Buildings cannot be placed on this object's cells while it exists.")]
        [SerializeField] bool blocksBuilding = true;

        [Header("Resource (trees, stones, gold...)")]
        [SerializeField] ResourceType resourceType = ResourceType.None;
        [Tooltip("Hit points. A worker with stat S removes S per hit (GDD: tree with 20 HP = 20 hits at Woodcutting 1).")]
        [Min(0)] [SerializeField] int maxHealth = 20;
        [Tooltip("Resource items dropped when depleted.")]
        [Min(0)] [SerializeField] int yieldAmount = 1;
        [Tooltip("Trees: what remains after the tree is cut (tree1 -> stump1...).")]
        [SerializeField] SpawnableDefinition stump;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public WorldObjectKind Kind => kind;
        public Sprite[] Frames => frames;
        public bool Animate => animate;
        public float AnimationFps => animationFps;
        public GameObject PrefabOverride => prefabOverride;
        public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
        public float GroundInset => groundInset;
        public bool BlocksBuilding => blocksBuilding;
        public ResourceType ResourceType => resourceType;
        public int MaxHealth => maxHealth;
        public int YieldAmount => yieldAmount;
        public SpawnableDefinition Stump => stump;
        public Sprite PreviewSprite => frames != null && frames.Length > 0 ? frames[0] : null;

#if UNITY_EDITOR
        /// <summary>Editor-only setup used by the default asset builder.</summary>
        public void EditorSetup(string newDisplayName, WorldObjectKind newKind, Sprite[] newFrames, Vector2Int newFootprint,
            float newGroundInset, bool newAnimate, bool newBlocksBuilding, ResourceType newResource, int newHealth, int newYield)
        {
            displayName = newDisplayName;
            kind = newKind;
            frames = newFrames ?? new Sprite[0];
            footprint = newFootprint;
            groundInset = newGroundInset;
            animate = newAnimate;
            blocksBuilding = newBlocksBuilding;
            resourceType = newResource;
            maxHealth = newHealth;
            yieldAmount = newYield;
        }

        public void EditorSetStump(SpawnableDefinition newStump) => stump = newStump;
#endif
    }
}
