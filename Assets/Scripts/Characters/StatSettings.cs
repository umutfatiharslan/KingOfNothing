using System;
using System.Collections.Generic;
using KingOfNothing.World;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>One material of a training (amount = base + per level x current level).</summary>
    [Serializable]
    public class TrainingMaterial
    {
        public SpawnableDefinition item;
        [Min(0)] public int baseAmount = 5;
        [Min(0)] public int perLevel = 5;

        public TrainingMaterial() { }

        public TrainingMaterial(SpawnableDefinition item, int baseAmount, int perLevel)
        {
            this.item = item;
            this.baseAmount = baseAmount;
            this.perLevel = perLevel;
        }
    }

    /// <summary>Where a stat is trained and what one level of training costs (taken from that building's storage).</summary>
    [Serializable]
    public class StatTraining
    {
        public StatType stat;
        [Tooltip("School: work stats. Army House: war stats (health loss risk). Church: healing (priests).")]
        public BuildingType building = BuildingType.School;
        [Tooltip("Gold coins of the training L -> L+1 = base + per level x L.")]
        [Min(0)] public int coinBase = 2;
        [Min(0)] public int coinPerLevel = 2;
        public List<TrainingMaterial> materials = new List<TrainingMaterial>();

        public StatTraining() { }

        public StatTraining(StatType stat, BuildingType building)
        {
            this.stat = stat;
            this.building = building;
        }
    }

    /// <summary>
    /// Everything about stats: experience curve, experience per action, the level effects (stat table v3), the
    /// milestone abilities and the school / army / church trainings. An asset (Assets/Data/Characters/StatSettings),
    /// so every number can be changed in the Inspector, also during Play mode.
    /// </summary>
    [CreateAssetMenu(fileName = "StatSettings", menuName = "King of Nothing/Characters/Stat Settings")]
    public class StatSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/Data/Characters/StatSettings.asset";

        [Header("Experience (level L -> L+1 needs first + (L-1) x step: 1->2 = 200 ... 19->20 = 2000)")]
        [Min(1f)] public float xpFirstLevel = 200f;
        [Min(0f)] public float xpPerLevel = 100f;
        [Tooltip("XP multiplier after the training of a level: 1 + this x level (level 1: x1.1, level 2: x1.2 ...).")]
        [Min(0f)] public float trainingMultiplierPerLevel = 0.1f;

        [Header("Experience per action")]
        public float buildHitXp = 2f;
        public float buildCompleteXp = 20f;
        public float chopHitXp = 2f;
        public float treeFelledXp = 10f;
        public float mineHitXp = 2f;
        public float mineGoldXp = 5f;
        public float harvestHitXp = 2f;
        public float harvestDoneXp = 5f;
        public float huntHitXp = 3f;
        public float huntKillXp = 10f;
        public float furnaceCycleXp = 10f;
        [Tooltip("Efficiency: per item delivered to a template, a store or a training storage.")]
        public float deliveredItemXp = 1f;
        public float arrowXp = 2f;
        public float arrowHitXp = 2f;
        [Tooltip("Sword / spear (and tool fights): per hit.")]
        public float meleeHitXp = 2f;
        [Tooltip("Defense: XP per point of damage taken (0.2 = 1 XP per 5 damage).")]
        public float defenseXpPerDamage = 0.2f;
        [Tooltip("Defense: scarecrow hits for 1 XP.")]
        [Min(1)] public int defenseTrainingHits = 4;
        [Tooltip("Health: XP per point of health lost or regained (0.2 = 1 XP per 5).")]
        public float healthXpPerPoint = 0.2f;
        [Tooltip("Healing: XP per point healed.")]
        public float healingXpPerPoint = 1f;
        [Tooltip("Walk speed: cells walked for 1 XP.")]
        [Min(0.1f)] public float walkCellsPerXp = 10f;
        public float mealXp = 5f;

        [Header("Level effects: work")]
        [Tooltip("Health a hammer swing adds = this x building level.")]
        [Min(0)] public int buildPowerPerLevel = 1;
        [Tooltip("Daily decay x (1 - this x level of the best builder of the building).")]
        public float decayReductionPerLevel = 0.025f;
        [Tooltip("Mining: chance of +1 resource when a stone breaks = this x level.")]
        public float miningExtraChancePerLevel = 0.05f;
        [Tooltip("Gold mine: seconds per gold of one miner = base - per level x level (stat table: 120 s at 1, 25 s at 20).")]
        public float mineSecondsBase = 125f;
        public float mineSecondsPerLevel = 5f;
        [Min(0.5f)] public float mineSecondsMin = 5f;
        [Tooltip("Farming: harvest bonus per level (+1.5%).")]
        public float farmingBonusPerLevel = 0.015f;
        [Tooltip("Hunting: meat bonus per level (+1.5%).")]
        public float huntingBonusPerLevel = 0.015f;
        [Tooltip("Hunting: an animal flees when the hunter is closer than base - per level x level cells.")]
        public float fleeDistanceBase = 5f;
        public float fleeDistancePerLevel = 0.2f;
        [Tooltip("Baking: furnace speed x (base + per level x level).")]
        public float bakingSpeedBase = 0.7f;
        public float bakingSpeedPerLevel = 0.03f;
        [Tooltip("Baking: chance per wood to save it.")]
        public float bakingFuelSavingPerLevel = 0.015f;
        [Tooltip("Baking: chance of one extra portion when cooking (stands for the +1% satiety per level).")]
        public float bakingSatietyPerLevel = 0.01f;
        [Tooltip("Efficiency: material saving of the templates the character supplies (best supplier counts).")]
        public float efficiencySavingPerLevel = 0.015f;

        [Header("Level effects: war")]
        public float archeryAccuracyBase = 40f;
        public float archeryAccuracyPerLevel = 3f;
        public float archeryAccuracyMax = 95f;
        public float archerySpeedBase = 0.7f;
        public float archerySpeedPerLevel = 0.03f;
        public float archeryRangeBase = 4f;
        public float archeryRangePerLevel = 0.4f;
        [Tooltip("Friendly fire % = base - level (archers and lancers).")]
        public float friendlyFireBase = 20f;
        public float swordSpeedBase = 0.7f;
        public float swordSpeedPerLevel = 0.03f;
        public float spearReachBase = 1f;
        public float spearReachPerLevel = 0.1f;
        public float spearBlockPerLevel = 0.02f;
        [Tooltip("Damage taken - this x defense level.")]
        public float defenseReductionPerLevel = 0.01f;
        public float healthBase = 100f;
        public float healthPerLevel = 5f;

        [Header("Level effects: support and life")]
        public float healingRadiusBase = 1f;
        public float healingRadiusPerLevel = 0.1f;
        [Tooltip("Seconds between two healing waves of a priest.")]
        [Min(0.2f)] public float healingInterval = 2f;
        public float walkSpeedBase = 0.7f;
        public float walkSpeedPerLevel = 0.03f;
        public float hungerRateBase = 1.5f;
        public float hungerRatePerLevel = 0.05f;

        [Header("Milestones: Building")]
        [Tooltip("Sturdy Foundation (5): bonus health of a finished building = this x level of the best builder.")]
        public int foundationHealthPerLevel = 100;
        [Tooltip("Master Hand (10): build speed multiplier and critical chance (a critical swing counts twice).")]
        public float masterSpeedMultiplier = 2f;
        public float critChance10 = 0.2f;
        [Tooltip("Foreman (15): critical chance, speed bonus of the other builders of the site, health of a restored ruin.")]
        public float critChance15 = 0.6f;
        public float foremanSpeedBonus = 0.25f;
        public float foremanRestoreShare = 0.1f;
        [Tooltip("War Hammer (20): damage multiplier against buildings / bridges, salvage share of his Destroy, stun.")]
        public float hammerStructureMultiplier = 2f;
        public float hammerSalvageShare = 0.75f;
        public float stunSeconds = 1.5f;
        public float stunCooldown = 10f;

        [Header("Milestones: Woodcutting")]
        public float extraWoodChance5 = 0.3f;
        [Tooltip("Wood Pile (5) / Full Arms (Efficiency 5): extra pile size while carrying.")]
        public int carryBonus5 = 5;
        public float regrowChance10 = 0.4f;
        public float regrowChance15 = 0.6f;
        public float regrowChance20 = 0.8f;
        [Tooltip("Game hours until a stump grows back into a tree.")]
        public float regrowHours = 12f;
        [Tooltip("Fallen Giant (15): a falling tree hits one tree within this many cells.")]
        public float fallRadius15 = 2f;
        [Tooltip("Forest Keeper (20): regrown trees grow this many times faster and give this much more wood on the first chop.")]
        public float regrowSpeed20 = 2f;
        public float firstChopBonus20 = 0.5f;
        [Tooltip("Forest Keeper (20): trees cut in a day to plant a sapling at the end of the day.")]
        [Min(1)] public int saplingTreesPerDay = 5;
        [Tooltip("Axe fights: bleeding damage per game hour and in total.")]
        public int bleedPerHour = 10;
        public int bleedTotal = 50;

        [Header("Milestones: Mining")]
        public float ruinSalvageShare5 = 0.25f;
        public int mineCapacityBonus10 = 1;
        public float reserveKeepChance15 = 0.25f;
        [Tooltip("Pickaxe Blow (20): defense points broken per hit, maximum hits, game hours until it comes back.")]
        public int armorBreakPerHit = 1;
        public int armorBreakMaxHits = 10;
        public float armorBreakHours = 1f;
        [Tooltip("Pickaxe Blow (20) on buildings: extra damage per hit (+1% x hits, up to the maximum).")]
        public float armorBreakBuildingBonus = 0.01f;

        [Header("Milestones: Hunting")]
        public float curiousStopSeconds10 = 3f;
        public float huntingPartyBonus15 = 0.5f;
        public float huntingPartyRadius = 2f;
        [Tooltip("Knife Master (20): damage multiplier of the double strike.")]
        public float knifeDoubleStrike = 2f;

        [Header("Milestones: Baking")]
        [Tooltip("Fire Keeper (5): wood saved per furnace cycle.")]
        public int woodSaving5 = 1;
        public float extraCoinChance10 = 0.2f;
        public float feastMorale15 = 5f;
        public float feastWorkBonus15 = 0.1f;
        public float feastHours15 = 1f;
        [Tooltip("Hearth Master (20): special meals per day; the eater gets XP (share of the next level) and an XP multiplier for a while.")]
        [Min(0)] public int masterMealsPerDay = 3;
        public float masterXpShare = 0.5f;
        public float masterXpMultiplier = 2f;
        public float masterHours = 24f;

        [Header("Milestones: Efficiency")]
        public float discount10 = 0.1f;
        public float salvageShare15 = 0.8f;
        [Tooltip("Master's Secret (20): every n-th template supplied gets its most expensive material for free.")]
        [Min(1)] public int freeMaterialEvery20 = 5;

        [Header("Milestones: Archery")]
        public float doubleShotChance10 = 0.25f;
        public float pierceDamage15 = 0.5f;
        public float pierceRadius15 = 3f;
        public float eagleRangeBonus20 = 5f;
        public float eagleFirstShotMultiplier20 = 3f;

        [Header("Milestones: Sword")]
        public float blockChance5 = 0.3f;
        public float comboBonus10 = 0.5f;
        public float cleaveDamage15 = 0.5f;
        public float cleaveRadius15 = 1.2f;
        public float lastStandMultiplier20 = 2f;
        public float lastStandThreshold20 = 0.25f;

        [Header("Milestones: Spear")]
        public float spearWallReduction10 = 0.1f;
        public float spearWallRadius10 = 1.2f;
        public float pierceCells15 = 2f;
        public float whirlwindInterval20 = 12f;
        public float whirlwindRadius20 = 2f;

        [Header("Milestones: Defense")]
        public float firstHitReduction5 = 0.25f;
        public float firstHitReduction15 = 0.5f;
        public float coverReduction10 = 0.1f;
        public float coverRadius10 = 2f;
        public float knockbackChance15 = 0.2f;

        [Header("Milestones: Health")]
        public float secondWindThreshold = 0.3f;
        public float secondWindHeal = 0.1f;
        public float secondWindSeconds = 5f;
        public float morningHeal10 = 0.05f;
        public float ironBodyShield15 = 0.15f;
        public float ironBodySeconds15 = 30f;
        [Min(1)] public int immortalWillDays20 = 5;

        [Header("Milestones: Healing")]
        [Tooltip("Prayer (5): extra healing over this many seconds (same amount as the wave).")]
        public float prayerSeconds = 5f;

        [Header("Milestones: Walk speed")]
        public float commandBoost5 = 0.5f;
        public float commandBoostSeconds5 = 2f;
        public float lowHealthBoost10 = 0.4f;
        public float lowHealthThreshold10 = 0.3f;
        [Tooltip("Escape (15): chance to dodge a hit completely.")]
        public float dodgeArrow15 = 0.15f;
        public float dodgeSword15 = 0.1f;
        public float dodgeSpear15 = 0.08f;
        public float dodgeTool15 = 0.1f;
        public float dodgeCollapse15 = 0.3f;
        public float leaderBonus20 = 0.1f;

        [Header("Milestones: Hunger")]
        public float insideHungerRate5 = 0.5f;
        public float rawFoodShare10 = 0.25f;
        public float hungryThreshold15 = 25f;
        public float hungryBonus15 = 0.15f;
        public int overeatHealth20 = 5;
        public int overeatMaxHealth20 = 200;

        [Header("Training (school, army house, church)")]
        [Tooltip("Game hours of the training L -> L+1 (index = L; 1 game hour = 1 real minute with a 24 minute day).")]
        public float[] trainingHours = { 0f, 2f, 3f, 3f, 4f, 5f, 7f, 9f, 11f, 14f, 18f, 24f, 30f, 38f, 49f, 63f, 80f, 103f, 131f, 168f };
        [Tooltip("A character needs at least this share of its health to start / go on with an army house training.")]
        [Range(0f, 1f)] public float trainingMinHealth = 0.25f;
        [Tooltip("Army house: chance per game hour of training to lose (target level) health.")]
        [Range(0f, 1f)] public float armyHealthLossChance = 0.2f;
        public List<StatTraining> trainings = new List<StatTraining>();

        [SerializeField, HideInInspector] int dataVersion;

        static StatSettings fallback;

        public static StatSettings Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<StatSettings>();
                    fallback.name = "StatSettings (defaults)";
                    fallback.hideFlags = HideFlags.DontSave;
                    fallback.trainings = DefaultTrainings(null, null, null, null, null, null, null, null);
                }
                return fallback;
            }
        }

        /// <summary>The asset in use (from GameplaySettings), never null.</summary>
        public static StatSettings Current
        {
            get
            {
                var map = MapData.Instance;
                var s = map != null ? map.Gameplay.stats : null;
                return s != null ? s : Fallback;
            }
        }

        public float XpToNext(int level) => xpFirstLevel + Mathf.Max(0, level - 1) * xpPerLevel;

        public float TrainingMultiplier(int level) => 1f + trainingMultiplierPerLevel * Mathf.Max(0, level);

        public float TrainingHoursFor(int level)
        {
            if (trainingHours == null || trainingHours.Length == 0) return 1f;
            return Mathf.Max(0.05f, trainingHours[Mathf.Clamp(level, 0, trainingHours.Length - 1)]);
        }

        public StatTraining TrainingFor(StatType stat)
        {
            foreach (var t in trainings) if (t != null && t.stat == stat) return t;
            return null;
        }

        /// <summary>What the training L -> L+1 costs (coins + materials).</summary>
        public List<DropEntry> TrainingCost(StatType stat, int level, SpawnableDefinition coin)
        {
            var list = new List<DropEntry>();
            var t = TrainingFor(stat);
            if (t == null) return list;
            int coins = t.coinBase + t.coinPerLevel * level;
            if (coin != null && coins > 0) list.Add(new DropEntry(coin, coins));
            foreach (var m in t.materials)
            {
                if (m == null || m.item == null) continue;
                int amount = m.baseAmount + m.perLevel * level;
                if (amount > 0) list.Add(new DropEntry(m.item, amount));
            }
            return list;
        }

        /// <summary>Default trainings (stat table + the agreed materials).</summary>
        public static List<StatTraining> DefaultTrainings(SpawnableDefinition wood, SpawnableDefinition stone, SpawnableDefinition gold,
            SpawnableDefinition bush, SpawnableDefinition mushroom, SpawnableDefinition meat, SpawnableDefinition pumpkin, SpawnableDefinition unused)
        {
            StatTraining T(StatType stat, BuildingType building, params SpawnableDefinition[] items)
            {
                var t = new StatTraining(stat, building);
                foreach (var i in items) t.materials.Add(new TrainingMaterial(i, 5, 5));
                return t;
            }
            return new List<StatTraining>
            {
                T(StatType.Building, BuildingType.School, wood, stone),
                T(StatType.Woodcutting, BuildingType.School, wood),
                T(StatType.Mining, BuildingType.School, stone),
                T(StatType.Farming, BuildingType.School, bush, mushroom),
                T(StatType.Hunting, BuildingType.School, meat),
                T(StatType.Fishing, BuildingType.School, wood),
                T(StatType.Baking, BuildingType.School, gold),
                T(StatType.Efficiency, BuildingType.School, stone, wood),
                T(StatType.Archery, BuildingType.ArmyHouse, wood),
                T(StatType.Sword, BuildingType.ArmyHouse, gold),
                T(StatType.Spear, BuildingType.ArmyHouse, wood),
                T(StatType.Defense, BuildingType.ArmyHouse, stone),
                T(StatType.Health, BuildingType.ArmyHouse, pumpkin),
                T(StatType.Healing, BuildingType.Church, mushroom),
            };
        }

#if UNITY_EDITOR
        public int EditorDataVersion
        {
            get => dataVersion;
            set => dataVersion = value;
        }

        public static StatSettings LoadOrCreateDefault()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<StatSettings>(DefaultPath);
            if (asset != null) return asset;
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Data");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/Characters")) UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "Characters");
            asset = CreateInstance<StatSettings>();
            // The default trainings with the items of the default map assets (the builder fills them again)
            SpawnableDefinition Item(string file) =>
                UnityEditor.AssetDatabase.LoadAssetAtPath<SpawnableDefinition>($"Assets/Data/World/Spawnables/{file}.asset");
            asset.trainings = DefaultTrainings(Item("WoodResource"), Item("Rock"), Item("GoldResource"), Item("BushBranch"), Item("Mushroom1"),
                Item("RawMeat"), Item("PumpkinItem"), null);
            UnityEditor.AssetDatabase.CreateAsset(asset, DefaultPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return asset;
        }
#endif
    }
}
