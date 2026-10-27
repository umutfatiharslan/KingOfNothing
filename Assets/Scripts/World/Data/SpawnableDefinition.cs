using System;
using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>What an object leaves on the ground when it is broken (or gives when it is collected).</summary>
    [Serializable]
    public class DropEntry
    {
        [Tooltip("The item (an object asset, e.g. Wood Resource, Gold Resource, Rock).")]
        public SpawnableDefinition item;
        [Min(0)] public int amount = 1;
        [Tooltip("Chance that this drop happens at all (bones: 0.5 = one bone or nothing).")]
        [Range(0f, 1f)] public float chance = 1f;

        public DropEntry() { }

        public DropEntry(SpawnableDefinition item, int amount, float chance = 1f)
        {
            this.item = item;
            this.amount = amount;
            this.chance = chance;
        }
    }

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
        [Tooltip("Size of the sprite on the map (gold coin: 0.35, so it fits in a pawn's hand).")]
        [Min(0.05f)] [SerializeField] float visualScale = 1f;

        [Header("Placement")]
        [Tooltip("Cells covered on the map grid (e.g. a gold mine is 3x2).")]
        [SerializeField] Vector2Int footprint = Vector2Int.one;
        [Tooltip("Transparent space under the visible base of the sprite, in units (pixels / 64).")]
        [SerializeField] float groundInset = 0.3f;
        [Tooltip("Buildings cannot be placed on this object's cells while it exists.")]
        [SerializeField] bool blocksBuilding = true;

        [Header("Movement & collision")]
        [Tooltip("Characters can walk through this object (bushes, grass, bones, the smallest mushroom / stone...). " +
                 "Off = it blocks characters and paths go around it.")]
        [SerializeField] bool walkThrough;
        [Tooltip("Speed multiplier while a character walks through it (bush = 0.7). Only used when Walk Through is on.")]
        [Range(0.05f, 1f)] [SerializeField] float walkThroughSpeed = 1f;
        [Tooltip("Solid part that characters collide with (cells), e.g. only the trunk of a tree. Kept inside the footprint.")]
        [SerializeField] Vector2 hitboxSize = new Vector2(0.6f, 0.4f);
        [Tooltip("Offset (cells) of the hitbox's bottom centre from the object's ground point.")]
        [SerializeField] Vector2 hitboxOffset = new Vector2(0f, -0.1f);
        [Tooltip("Becomes transparent while a character walks through it or stands right behind it.")]
        [SerializeField] bool fadeWhenHidingCharacter = true;
        [Tooltip("Set by the default asset builder once the movement rules above were filled in.")]
        [SerializeField, HideInInspector] bool movementConfigured;

        [Header("Resource (trees, stones, gold...)")]
        [SerializeField] ResourceType resourceType = ResourceType.None;
        [Tooltip("Hit points (characters hit for 10 until stats exist). -1 = cannot be hit: it is collected directly " +
                 "into the inventory (small mushroom, smallest stone, bones, dropped wood / gold...).")]
        [Min(-1)] [SerializeField] int maxHealth = 20;
        [Tooltip("Resource items dropped when depleted (old single value; the Drops list below is what is spawned).")]
        [Min(0)] [SerializeField] int yieldAmount = 1;
        [Tooltip("What falls on the ground when it is broken. For collectable objects (health -1): what collecting gives.")]
        [SerializeField] List<DropEntry> drops = new List<DropEntry>();
        [Tooltip("As a dropped item: how many fit in one pile on the ground (18 wood = a pile of 10 + a pile of 8).")]
        [Min(1)] [SerializeField] int stackSize = 10;
        [Tooltip("Frames used after the object was hit once (gold stones lose their highlight for good). Empty = no change.")]
        [SerializeField] Sprite[] damagedFrames = new Sprite[0];
        [Tooltip("Characters can carry it to another place in Default mode (trees, bushes and other natural objects cannot).")]
        [SerializeField] bool movableByCharacters;
        [Tooltip("Tool (pawn animation) used to work on it. Auto: axe for trees, pickaxe for stones / gold, knife for mushrooms / bushes, hammer for the rest.")]
        [SerializeField] InteractionTool interactionTool = InteractionTool.Auto;
        [Tooltip("Version of the default values written by the asset builder (newer builders update older assets once).")]
        [SerializeField, HideInInspector] int dataVersion;
        [Tooltip("Trees: what remains after the tree is cut (tree1 -> stump1...).")]
        [SerializeField] SpawnableDefinition stump;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public WorldObjectKind Kind => kind;
        public Sprite[] Frames => frames;
        public bool Animate => animate;
        public float AnimationFps => animationFps;
        public GameObject PrefabOverride => prefabOverride;
        public float VisualScale => Mathf.Max(0.05f, visualScale);
        public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
        public float GroundInset => groundInset;
        public bool BlocksBuilding => blocksBuilding;
        public bool WalkThrough => walkThrough;
        public float WalkThroughSpeed => walkThrough ? Mathf.Clamp(walkThroughSpeed, 0.05f, 1f) : 1f;
        public Vector2 HitboxSize => new Vector2(Mathf.Max(0.05f, hitboxSize.x), Mathf.Max(0.05f, hitboxSize.y));
        public Vector2 HitboxOffset => hitboxOffset;
        public bool FadeWhenHidingCharacter => fadeWhenHidingCharacter;
        public ResourceType ResourceType => resourceType;
        public int MaxHealth => maxHealth;
        /// <summary>Health -1: cannot be hit, collected directly into the inventory.</summary>
        public bool IsCollectable => maxHealth < 0;
        public int YieldAmount => yieldAmount;
        public IReadOnlyList<DropEntry> Drops => drops;
        public int StackSize => Mathf.Max(1, stackSize);
        public Sprite[] DamagedFrames => damagedFrames;
        public bool MovableByCharacters => movableByCharacters;
        public InteractionTool Tool => interactionTool != InteractionTool.Auto ? interactionTool : InteractionTools.ForKind(kind);
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

        public void EditorSetVisualScale(float scale) => visualScale = scale;

        public void EditorSetStackSize(int size) => stackSize = Mathf.Max(1, size);

        public void EditorSetMovable(bool movable) => movableByCharacters = movable;

        public bool EditorMovementConfigured => movementConfigured;
        public int EditorDataVersion => dataVersion;

        /// <summary>Gameplay values from the design tables (health, drops, stacking, carrying).</summary>
        public void EditorSetGameplay(int version, int newHealth, List<DropEntry> newDrops, int newStackSize, bool newMovable,
            Sprite[] newDamagedFrames)
        {
            dataVersion = version;
            maxHealth = newHealth;
            drops = newDrops ?? new List<DropEntry>();
            stackSize = Mathf.Max(1, newStackSize);
            movableByCharacters = newMovable;
            damagedFrames = newDamagedFrames ?? new Sprite[0];
        }

        public void EditorSetIdentity(string newDisplayName, WorldObjectKind newKind)
        {
            displayName = newDisplayName;
            kind = newKind;
        }

        public void EditorSetFrames(Sprite[] newFrames, bool newAnimate)
        {
            frames = newFrames ?? new Sprite[0];
            animate = newAnimate;
        }

        public void EditorSetMovement(bool newWalkThrough, float newSpeed, Vector2 newHitboxSize, Vector2 newHitboxOffset, bool newFade)
        {
            walkThrough = newWalkThrough;
            walkThroughSpeed = newSpeed;
            hitboxSize = newHitboxSize;
            hitboxOffset = newHitboxOffset;
            fadeWhenHidingCharacter = newFade;
            movementConfigured = true;
        }
#endif
    }
}
