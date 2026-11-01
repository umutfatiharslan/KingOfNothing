using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// The level effects of the stat table v3 in one place (numbers from StatSettings). Every method takes the
    /// character (null = a plain unit without stats: it gets the old fixed values).
    /// </summary>
    public static class StatEffects
    {
        static StatSettings S => StatSettings.Current;

        public static Villager Of(Unit unit) => unit != null ? unit.Villager : null;
        public static Villager Of(GameObject go) => go != null ? go.GetComponent<Villager>() : null;

        /// <summary>Level used for effects (temporary bonuses included).</summary>
        public static int Level(Villager v, StatType stat) => v != null ? v.Stats.Get(stat) : 0;

        public static bool Has(Villager v, StatType stat, int milestone) => v != null && v.Stats.Has(stat, milestone);

        // ------------------------------------------------------------------ tools and work stats

        /// <summary>The work stat behind a tool used on an object (axe: woodcutting, pickaxe: mining, knife: farming...).</summary>
        public static StatType StatForTool(InteractionTool tool, bool onAnimal = false)
        {
            switch (tool)
            {
                case InteractionTool.Axe: return StatType.Woodcutting;
                case InteractionTool.Pickaxe: return StatType.Mining;
                case InteractionTool.Knife: return onAnimal ? StatType.Hunting : StatType.Farming;
                case InteractionTool.Sword: return StatType.Sword;
                case InteractionTool.Spear: return StatType.Spear;
                case InteractionTool.Bow: return StatType.Archery;
                case InteractionTool.Heal: return StatType.Healing;
                default: return StatType.Building;
            }
        }

        /// <summary>Hungry Wolf (Hunger 15): stronger while hungry.</summary>
        public static float HungryBonus(Villager v)
        {
            if (v == null || !Has(v, StatType.Hunger, 15)) return 1f;
            return v.Needs.Satiety.current < S.hungryThreshold15 ? 1f + S.hungryBonus15 : 1f;
        }

        /// <summary>Damage of one work hit with this stat (= the level, stat table). Plain units: the old 10.</summary>
        public static int WorkDamage(Villager v, StatType stat)
        {
            if (v == null) return MapData.Instance != null ? MapData.Instance.Interaction.villagerHitDamage : 10;
            int level = Level(v, stat);
            if (level <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(level * HungryBonus(v) * v.WorkBuffMultiplier));
        }

        /// <summary>Health a hammer swing adds.</summary>
        public static int BuildPower(Villager v)
        {
            if (v == null) return MapData.Instance != null ? MapData.Instance.Gameplay.buildPowerPerHit : 50;
            int level = Level(v, StatType.Building);
            if (level <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(level * S.buildPowerPerLevel * HungryBonus(v) * v.WorkBuffMultiplier));
        }

        /// <summary>Speed of the hammer (Master Hand x2).</summary>
        public static float BuildSpeed(Villager v) => Has(v, StatType.Building, 10) ? S.masterSpeedMultiplier : 1f;

        /// <summary>Critical swing chance of a builder (Master Hand 20%, Foreman 60%).</summary>
        public static float BuildCritChance(Villager v) => Has(v, StatType.Building, 15) ? S.critChance15 : Has(v, StatType.Building, 10) ? S.critChance10 : 0f;

        public static float DecayMultiplier(int builderLevel) => Mathf.Clamp01(1f - S.decayReductionPerLevel * builderLevel);

        public static float MineSeconds(Villager v)
        {
            int level = v != null ? Mathf.Max(1, Level(v, StatType.Mining)) : 10;
            return Mathf.Max(S.mineSecondsMin, S.mineSecondsBase - S.mineSecondsPerLevel * level);
        }

        public static float BakingSpeed(Villager v) => S.bakingSpeedBase + S.bakingSpeedPerLevel * (v != null ? Level(v, StatType.Baking) : 10);

        public static float FleeDistance(int huntingLevel) => Mathf.Max(0.5f, S.fleeDistanceBase - S.fleeDistancePerLevel * huntingLevel);

        /// <summary>Extra pile size while carrying (Wood Pile: wood; Full Arms: anything while supplying).</summary>
        public static int CarryBonus(Villager v, SpawnableDefinition item, bool supplying)
        {
            if (v == null || item == null) return 0;
            int bonus = 0;
            if (Has(v, StatType.Woodcutting, 5) && item.ResourceType == ResourceType.Wood) bonus += S.carryBonus5;
            if (supplying && Has(v, StatType.Efficiency, 5)) bonus += S.carryBonus5;
            return bonus;
        }

        // ------------------------------------------------------------------ war

        public static float ArcheryAccuracy(Villager v)
        {
            int level = v != null ? Level(v, StatType.Archery) : 10;
            return Mathf.Clamp01(Mathf.Min(S.archeryAccuracyMax, S.archeryAccuracyBase + S.archeryAccuracyPerLevel * level) / 100f);
        }

        public static float ArcheryIntervalMultiplier(Villager v) =>
            1f / Mathf.Max(0.1f, S.archerySpeedBase + S.archerySpeedPerLevel * (v != null ? Level(v, StatType.Archery) : 10));

        public static float ArcheryRange(Villager v) =>
            v == null ? (MapData.Instance != null ? MapData.Instance.Gameplay.archerRange : 10f) : S.archeryRangeBase + S.archeryRangePerLevel * Level(v, StatType.Archery);

        public static float MeleeIntervalMultiplier(Villager v, StatType weaponStat) =>
            1f / Mathf.Max(0.1f, S.swordSpeedBase + S.swordSpeedPerLevel * (v != null ? Level(v, weaponStat) : 10));

        /// <summary>Friendly fire chance of archers and lancers (0 with Wind Reader).</summary>
        public static float FriendlyFire(Villager v, StatType weaponStat)
        {
            if (v == null) return 0f;
            if (weaponStat == StatType.Archery && Has(v, StatType.Archery, 5)) return 0f;
            return Mathf.Clamp01((S.friendlyFireBase - Level(v, weaponStat)) / 100f);
        }

        public static float SpearReach(Villager v) => S.spearReachBase + S.spearReachPerLevel * (v != null ? Level(v, StatType.Spear) : 0);

        public static float SpearBlock(Villager v) => v != null && v.Role == VillagerRole.Lancer ? Mathf.Clamp01(S.spearBlockPerLevel * Level(v, StatType.Spear)) : 0f;

        /// <summary>Share of the damage taken away by defense (broken defense points subtracted).</summary>
        public static float DefenseReduction(Villager v, int brokenPoints)
        {
            if (v == null) return 0f;
            int level = Mathf.Max(0, Level(v, StatType.Defense) - brokenPoints);
            return Mathf.Clamp(S.defenseReductionPerLevel * level, 0f, 0.95f);
        }

        public static int MaxHealth(Villager v, int bonus) =>
            Mathf.RoundToInt(S.healthBase + S.healthPerLevel * (v != null ? Level(v, StatType.Health) : 0)) + bonus;

        // ------------------------------------------------------------------ support and life

        public static float HealingRadius(Villager v) => S.healingRadiusBase + S.healingRadiusPerLevel * Level(v, StatType.Healing);

        public static float WalkMultiplier(Villager v) => v == null ? 1f : S.walkSpeedBase + S.walkSpeedPerLevel * Level(v, StatType.WalkSpeed);

        public static float HungerRate(Villager v) => v == null ? 1f : Mathf.Max(0.05f, S.hungerRateBase - S.hungerRatePerLevel * Level(v, StatType.Hunger));

        // ------------------------------------------------------------------ fighting villagers

        /// <summary>
        /// The tool a villager fights with (milestone 20 of building, woodcutting, mining or hunting), or Auto when it
        /// cannot fight. Soldiers use their weapon.
        /// </summary>
        public static InteractionTool FightingTool(Villager v)
        {
            if (v == null) return InteractionTool.Auto;
            if (VillagerRoles.IsSoldier(v.Role)) return VillagerRoles.WeaponFor(v.Role);
            if (!VillagerRoles.IsWorker(v.Role)) return InteractionTool.Auto;
            if (Has(v, StatType.Building, 20)) return InteractionTool.Hammer;
            if (Has(v, StatType.Woodcutting, 20)) return InteractionTool.Axe;
            if (Has(v, StatType.Mining, 20)) return InteractionTool.Pickaxe;
            if (Has(v, StatType.Hunting, 20)) return InteractionTool.Knife;
            return InteractionTool.Auto;
        }

        public static bool CanFight(Unit unit)
        {
            var v = Of(unit);
            if (v == null) return false;
            var tool = FightingTool(v);
            return tool != InteractionTool.Auto && v.Stats.CanDo(StatForTool(tool, true));
        }

        /// <summary>The stat behind a fighting tool.</summary>
        public static StatType FightStat(InteractionTool tool) => tool == InteractionTool.Knife ? StatType.Hunting : StatForTool(tool);
    }
}
