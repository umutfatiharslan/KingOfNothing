using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>What a building is for (decides its menu entries and special rules).</summary>
    public enum BuildingType
    {
        /// <summary>Main keep (Castle.png): only one; the game is over when it falls. Stores 9 items x 100.</summary>
        Castle,
        /// <summary>School (Barracks.png): only villagers train here.</summary>
        School,
        /// <summary>Army house (Archery.png): soldier training and class changes (warrior / lancer / archer).</summary>
        ArmyHouse,
        /// <summary>Church (Monastery.png): healing for gold coins, unlocks the priest.</summary>
        Church,
        /// <summary>Villager house (House1-3.png): home of two villagers, stores 4 items.</summary>
        House,
        /// <summary>Archer tower (Tower.png): one character on top.</summary>
        Tower,
        /// <summary>Bridge over water (Bridge_All.png).</summary>
        Bridge,
        /// <summary>Furnace: melts gold with wood into gold coins.</summary>
        Furnace,
        /// <summary>Gold mine: villagers go inside and bring gold out (piles at the entrance).</summary>
        GoldMine,
    }

    /// <summary>The three lives of a building.</summary>
    public enum BuildingPhase
    {
        /// <summary>Placed from the build menu (a see-through template at 1 health) and being built.</summary>
        Construction,
        /// <summary>Built: usable; loses its own (full) health when damaged.</summary>
        Complete,
        /// <summary>Its full health is gone: the destroyed look with its own health. Can be repaired; at 0 it is gone.</summary>
        Destroyed,
    }

    /// <summary>
    /// One look of a building while it is built or destroyed. The stage is shown while the health of that phase is
    /// below <see cref="belowHealth"/> (the stage with the lowest matching threshold wins; a destroyed building
    /// always shows one of its destroyed stages).
    /// </summary>
    [Serializable]
    public class BuildingStage
    {
        public Sprite sprite;
        [Tooltip("Size of the sprite (Castle_Destroyed at 0.6 for the smaller buildings, House_Construction at 1.5 for the big ones).")]
        [Min(0.05f)] public float scale = 1f;
        [Tooltip("Transparent space under the visible base of the sprite (units, before scaling).")]
        public float groundInset;
        [Tooltip("Shown while the health is below this value.")]
        [Min(1)] public int belowHealth = 1000;
        [Tooltip("Repeat the sprite on every cell of the footprint (bridges: one pile per cell).")]
        public bool repeatPerCell;
    }

    /// <summary>
    /// A building of the build menu. It is a map object (SpawnableDefinition: the finished sprite, footprint,
    /// hitbox, maximum health) with the building rules on top: construction and destroyed looks, capacity,
    /// inventory, costs, and the special rules of the castle, tower, church, army house, furnace and bridges.
    /// Three phases, each with its own health bar:
    ///  - construction: the site is a see-through template at 1 health until villagers start; every hammer swing adds
    ///    health; once the construction stages are passed the finished sprite is shown, but the building is only
    ///    complete (usable) at full health;
    ///  - complete: full health (Max Health); when it is gone the building collapses into
    ///  - destroyed: the destroyed look with its own health (Destroyed Health, starting full again). It can be
    ///    repaired: when the destroyed health is full again the building comes back with 1 health and the repair
    ///    goes on. At 0 destroyed health it is gone.
    /// </summary>
    [CreateAssetMenu(fileName = "Building", menuName = "King of Nothing/World/Building")]
    public class BuildingDefinition : SpawnableDefinition
    {
        [Header("Building")]
        public BuildingType buildingType = BuildingType.House;
        [TextArea(1, 3)] public string description;
        [Tooltip("Icon of the build menu (empty = the finished sprite).")]
        public Sprite menuIcon;
        [Tooltip("Size of the finished sprite.")]
        [Min(0.05f)] public float finishedScale = 1f;
        [Tooltip("Centre the sprites on the footprint instead of standing them on its front edge (bridges).")]
        public bool centerOnFootprint;
        [Tooltip("Drawn under the characters (bridges).")]
        public bool drawUnderCharacters;

        [Header("Construction and damage looks")]
        [Tooltip("Looks while it is being built (health below the stage value). Order by threshold, lowest first.")]
        public List<BuildingStage> constructionStages = new List<BuildingStage>();
        [Tooltip("Looks of the destroyed building (by its destroyed health; one stage is always shown).")]
        public List<BuildingStage> destroyedStages = new List<BuildingStage>();
        [Tooltip("Health of the destroyed building (a separate bar after the full health is gone). 0 = it disappears at once.")]
        [Min(0)] public int destroyedHealth = 1000;
        [Tooltip("Objects of this kind found on the map are ruins (the destroyed phase) until repaired (gold mines).")]
        public bool spawnDestroyed;
        [Tooltip("Look while characters work inside (the active gold mine). Empty = the finished sprite.")]
        public Sprite activeSprite;

        [Header("Bridge tiles (finished bridge drawn cell by cell, so bridges join end to end)")]
        [Tooltip("First cell (left end / bottom end).")]
        public Sprite tileStart;
        public Sprite tileMiddle;
        [Tooltip("Last cell (right end / top end).")]
        public Sprite tileEnd;

        [Header("Building rules")]
        [Tooltip("How many of it may exist (castle: 1). -1 = no limit.")]
        public int maxCount = -1;
        [Tooltip("Items taken from the stores (castle / houses) when the construction site is placed in Default mode " +
                 "(Developer mode builds for free). Empty = free.")]
        public List<DropEntry> buildCost = new List<DropEntry>();
        [Tooltip("The game is over when this building is destroyed (castle).")]
        public bool gameOverWhenDestroyed;

        [Header("Characters inside")]
        [Tooltip("How many characters fit inside. -1 = unlimited (castle), 0 = nobody.")]
        public int capacity = 2;
        public bool villagersCanEnter = true;
        public bool soldiersCanEnter = true;
        public bool priestsCanEnter = true;
        [Tooltip("Door, relative to the building's ground point (bottom centre). Characters enter and leave here.")]
        public Vector2 doorOffset = new Vector2(0f, -0.35f);

        [Header("Inventory (only complete buildings store items)")]
        [Tooltip("Number of different items. 0 = no inventory.")]
        [Min(0)] public int inventorySlots;
        [Tooltip("Items per slot.")]
        [Min(1)] public int slotCapacity = 100;

        [Header("Tower")]
        [Tooltip("Where the character on top stands, relative to the ground point.")]
        public Vector2 perchOffset = new Vector2(0f, 2.3f);
        [Tooltip("Extra health while an archer is on top.")]
        [Min(0)] public int mannedHealthBonus = 500;
        [Tooltip("Range multiplier of an archer on top.")]
        [Min(1f)] public float archerRangeMultiplier = 2f;
        [Tooltip("Archery bonus gained per interval on top (lost when the archer leaves).")]
        [Min(0)] public int archeryBonusPerStep = 1;
        [Min(0)] public int archeryBonusMax = 5;
        [Tooltip("Seconds on top per archery bonus step.")]
        [Min(1f)] public float archeryBonusInterval = 60f;

        [Header("Church")]
        [Tooltip("Gold coins for healing a character.")]
        [Min(0)] public int healCost = 20;
        [Tooltip("Gold coins for an indulgence (more happiness; waits for the morale mechanic).")]
        [Min(0)] public int indulgenceCost = 10;
        [Tooltip("Gold coins for raising a random skill of a character by one level (waits for stats).")]
        [Min(0)] public int randomSkillCost = 1000;
        [Tooltip("Characters can become priests here.")]
        public bool unlocksPriest;

        [Header("Army house")]
        [Tooltip("Villagers and soldiers can change their class here (warrior, lancer, archer).")]
        public bool allowsClassChange;

        [Header("Furnace")]
        public SpawnableDefinition furnaceInputA;
        [Min(1)] public int furnaceInputAAmount = 1;
        public SpawnableDefinition furnaceInputB;
        [Min(1)] public int furnaceInputBAmount = 3;
        public SpawnableDefinition furnaceOutput;
        [Min(1)] public int furnaceOutputAmount = 1;
        [Tooltip("Seconds to melt one batch.")]
        [Min(0.1f)] public float furnaceSeconds = 8f;
        [Tooltip("Smoke animation (Dust_01) shown while the furnace works.")]
        public Sprite[] smokeFrames = new Sprite[0];
        [Tooltip("How high the smoke rises (cells).")]
        [Min(0.1f)] public float smokeHeight = 3f;
        [Tooltip("Point where the smoke starts, relative to the ground point.")]
        public Vector2 smokeOffset = new Vector2(0.2f, 1.4f);
        [Tooltip("Seconds between two puffs.")]
        [Min(0.05f)] public float smokeInterval = 0.35f;
        [Tooltip("Seconds a puff lives.")]
        [Min(0.1f)] public float smokeLifetime = 2.4f;
        [Min(0.05f)] public float smokeScale = 0.8f;

        [Header("Bridge")]
        [Tooltip("Placed over water; makes its cells walkable once its construction stages are passed.")]
        public bool isBridge;

        [Header("Gold mine")]
        [Tooltip("Item the miners bring out (Gold Resource).")]
        public SpawnableDefinition mineOutput;
        [Tooltip("Seconds one miner needs for one item.")]
        [Min(0.1f)] public float mineInterval = 5f;
        [Tooltip("Items a mine holds; it is exhausted afterwards. 0 = unlimited.")]
        [Min(0)] public int mineReserve = 500;

        public bool HasInventory => inventorySlots > 0;
        public bool IsMine => buildingType == BuildingType.GoldMine && mineOutput != null;
        public bool HasBridgeTiles => tileStart != null && tileMiddle != null && tileEnd != null;
        public bool IsFurnace => buildingType == BuildingType.Furnace && furnaceOutput != null;
        public Sprite MenuIcon => menuIcon != null ? menuIcon : PreviewSprite;

        /// <summary>Health from which the finished sprite is shown while building (the highest construction stage).</summary>
        public int ConstructionLookHealth
        {
            get
            {
                int h = 0;
                foreach (var s in constructionStages) if (s != null && s.sprite != null) h = Mathf.Max(h, s.belowHealth);
                return h;
            }
        }

        /// <summary>Construction stage for the health (null = the finished sprite).</summary>
        public BuildingStage ConstructionStageFor(int health) => Lowest(constructionStages, health, false);

        /// <summary>Destroyed stage for the destroyed health (never null if there is a destroyed sprite).</summary>
        public BuildingStage DestroyedStageFor(int health) => Lowest(destroyedStages, health, true);

        static BuildingStage Lowest(List<BuildingStage> list, int health, bool fallbackToHighest)
        {
            BuildingStage best = null, highest = null;
            foreach (var s in list)
            {
                if (s == null || s.sprite == null) continue;
                if (highest == null || s.belowHealth > highest.belowHealth) highest = s;
                if (health >= s.belowHealth) continue;
                if (best == null || s.belowHealth < best.belowHealth) best = s;
            }
            return best ?? (fallbackToHighest ? highest : null);
        }

        /// <summary>Biggest number of cells along the footprint's long side (bridges repeat their piles on every cell).</summary>
        public int CellsAlongLength => Mathf.Max(Footprint.x, Footprint.y);

#if UNITY_EDITOR
        [SerializeField, HideInInspector] int buildingDataVersion;

        public int EditorBuildingDataVersion
        {
            get => buildingDataVersion;
            set => buildingDataVersion = value;
        }
#endif
    }
}
