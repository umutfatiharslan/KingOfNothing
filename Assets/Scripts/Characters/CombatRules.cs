using System.Collections.Generic;
using KingOfNothing.Units;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// How hits are made and taken by characters: the damage of the attacker's weapon stat with its milestones
    /// (War Hammer, Last Stand, Knife Master, Hunting Party, Hungry Wolf...), status effects of the 20th level tool
    /// fights (stun, bleeding, broken defense) and the defender's rules (Escape, Shield Stance, spear block, defense,
    /// first hit of the day, Cover, Spear Wall, Iron Body, Push Back, Unshakable).
    /// </summary>
    public static class CombatRules
    {
        static StatSettings S => StatSettings.Current;
        static readonly Dictionary<Unit, float> LastStun = new Dictionary<Unit, float>();

        /// <summary>The stat a unit fights with when it uses this tool / weapon.</summary>
        public static StatType WeaponStat(InteractionTool weapon) => StatEffects.FightStat(weapon);

        /// <summary>Base damage of an attack (stat level and attacker milestones; per-hit extras are added by the job).</summary>
        public static HitInfo MakeHit(Unit attacker, ICombatTarget target, InteractionTool weapon, bool ranged)
        {
            var v = StatEffects.Of(attacker);
            var hit = new HitInfo { Attacker = attacker, Weapon = weapon, Ranged = ranged };
            if (v == null)
            {
                var g = KingOfNothing.World.MapData.Instance != null ? KingOfNothing.World.MapData.Instance.Gameplay : KingOfNothing.World.GameplaySettings.Fallback;
                hit.Damage = weapon == InteractionTool.Bow ? g.archerDamage : weapon == InteractionTool.Spear ? g.lancerDamage : g.warriorDamage;
                return hit;
            }
            var stat = WeaponStat(weapon);
            float damage = StatEffects.Level(v, stat) * StatEffects.HungryBonus(v) * v.WorkBuffMultiplier;
            bool living = target != null && (target.TargetKind == TargetKind.Animal || target.TargetKind == TargetKind.Character);

            // War Hammer (Building 20): x2 against buildings and bridges, stuns the living
            if (weapon == InteractionTool.Hammer && v.Stats.Has(StatType.Building, 20))
            {
                if (target != null && target.TargetKind == TargetKind.Building) damage *= S.hammerStructureMultiplier;
                if (living && StunReady(attacker)) hit.StunSeconds = S.stunSeconds;
            }
            // Forest Keeper (Woodcutting 20): the axe makes the living bleed
            if (weapon == InteractionTool.Axe && v.Stats.Has(StatType.Woodcutting, 20) && living) hit.Bleed = true;
            // Pickaxe Blow (Mining 20)
            if (weapon == InteractionTool.Pickaxe && v.Stats.Has(StatType.Mining, 20)) hit.ArmorBreak = true;
            // Knife Master (Hunting 20): a quick double strike
            if (weapon == InteractionTool.Knife && v.Stats.Has(StatType.Hunting, 20)) damage *= S.knifeDoubleStrike;
            // Last Stand (Sword 20)
            if (v.Stats.Has(StatType.Sword, 20) && v.Needs.Health.Normalized < S.lastStandThreshold20) damage *= S.lastStandMultiplier20;
            // Hunting Party (Hunting 15 of another hunter close by whose prey this is)
            if (target != null && HuntingPartyNearby(attacker, target)) damage *= 1f + S.huntingPartyBonus15;

            hit.Damage = Mathf.Max(damage > 0f ? 1 : 0, Mathf.RoundToInt(damage));
            return hit;
        }

        static bool StunReady(Unit attacker)
        {
            if (attacker == null) return false;
            if (LastStun.TryGetValue(attacker, out float t) && Time.time - t < S.stunCooldown) return false;
            return true;
        }

        /// <summary>Remembers that the attacker stunned something (War Hammer cooldown).</summary>
        public static void StunUsed(Unit attacker)
        {
            if (attacker != null) LastStun[attacker] = Time.time;
        }

        static bool HuntingPartyNearby(Unit attacker, ICombatTarget target)
        {
            float r2 = S.huntingPartyRadius * S.huntingPartyRadius;
            foreach (var other in Villager.All)
            {
                if (other == null || other.Movement == attacker || !other.Stats.Has(StatType.Hunting, 15)) continue;
                if (!ReferenceEquals(other.Movement.CurrentTarget, target)) continue;
                if (((Vector2)other.transform.position - (Vector2)attacker.transform.position).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        /// <summary>Applies the status effects of a hit to a living target (the hit already landed).</summary>
        public static void ApplyStatus(Component target, HitInfo hit)
        {
            if (target == null || hit.Training) return;
            var status = StatusEffects.Of(target);
            if (hit.StunSeconds > 0f && status.Stun(hit.StunSeconds)) StunUsed(hit.Attacker);
            if (hit.Bleed) status.Bleed(S.bleedPerHour, S.bleedTotal);
            if (hit.ArmorBreak) status.BreakArmor(S.armorBreakPerHit, S.armorBreakMaxHits * S.armorBreakPerHit, S.armorBreakHours);
        }

        /// <summary>A character is hit: every defender rule, then the damage. Returns the damage done.</summary>
        public static int HitVillager(Villager target, HitInfo hit)
        {
            if (target == null || hit.Training || hit.Damage <= 0) return 0;
            var s = S;
            var stats = target.Stats;

            // Escape (Walk speed 15): dodge completely
            if (stats.Has(StatType.WalkSpeed, 15) && Random.value < DodgeChance(hit.Weapon))
            {
                FloatingText.Show(target.transform, "Dodged!", Color.white);
                return 0;
            }
            // Shield Stance (Sword 5, warriors) and the lancer's block (2% per spear level)
            if (target.Role == VillagerRole.Warrior && stats.Has(StatType.Sword, 5) && Random.value < s.blockChance5)
            {
                target.Movement.PlayGuard(0.5f);
                FloatingText.Show(target.transform, "Blocked!", Color.white);
                return 0;
            }
            if (Random.value < StatEffects.SpearBlock(target))
            {
                target.Movement.PlayGuard(0.5f);
                FloatingText.Show(target.transform, "Blocked!", Color.white);
                return 0;
            }

            float damage = hit.Damage;
            var status = target.Status;
            damage *= 1f - StatEffects.DefenseReduction(target, status != null ? status.ArmorBroken : 0);

            int today = KingOfNothing.World.Buildings.GameClock.Today;
            var memory = target.Memory;
            // Thick Skin (5) / Push Back (15): the first hit of the day
            if (memory.firstHitDay != today && (stats.Has(StatType.Defense, 5)))
            {
                memory.firstHitDay = today;
                damage *= 1f - (stats.Has(StatType.Defense, 15) ? s.firstHitReduction15 : s.firstHitReduction5);
            }
            // Cover (Defense 10 of an ally) and Spear Wall (standing lancer with Spear 10): no stacking
            if (AllyNearby(target, StatType.Defense, 10, s.coverRadius10, false)) damage *= 1f - s.coverReduction10;
            if (AllyNearby(target, StatType.Spear, 10, s.spearWallRadius10, true)) damage *= 1f - s.spearWallReduction10;
            // Iron Body (Health 15): the first hit of the day raises a shield
            if (stats.Has(StatType.Health, 15) && memory.shieldDay != today)
            {
                memory.shieldDay = today;
                memory.shieldAmount = target.MaxHealth * s.ironBodyShield15;
                memory.shieldUntil = Time.time + s.ironBodySeconds15;
            }

            int done = target.ApplyDamage(damage);
            ApplyStatus(target, hit);

            // Push Back (Defense 15)
            var attacker = hit.Attacker;
            if (attacker != null && !hit.Ranged && stats.Has(StatType.Defense, 15) && Random.value < s.knockbackChance15)
            {
                var attackerVillager = attacker.Villager;
                if (attackerVillager == null || !attackerVillager.ImmuneToStatus) Knockback(attacker, target.transform.position, 1f);
            }
            if (done > 0 && !target.Movement.IsHidden) FloatingText.Show(target.transform, $"-{done}", new Color(1f, 0.4f, 0.35f));
            return done;
        }

        static float DodgeChance(InteractionTool weapon)
        {
            var s = S;
            switch (weapon)
            {
                case InteractionTool.Bow: return s.dodgeArrow15;
                case InteractionTool.Sword: return s.dodgeSword15;
                case InteractionTool.Spear: return s.dodgeSpear15;
                default: return s.dodgeTool15;
            }
        }

        /// <summary>Escape (Walk speed 15): chance to get out of a collapsing building unhurt.</summary>
        public static bool DodgesCollapse(Villager v) => v != null && v.Stats.Has(StatType.WalkSpeed, 15) && Random.value < S.dodgeCollapse15;

        static bool AllyNearby(Villager target, StatType stat, int level, float radius, bool mustStand)
        {
            float r2 = radius * radius;
            foreach (var v in Villager.All)
            {
                if (v == null || v == target || v.Movement == null || v.Movement.IsHidden || !v.Stats.Has(stat, level)) continue;
                if (stat == StatType.Spear && v.Role != VillagerRole.Lancer) continue;
                if (mustStand && (v.Movement.IsMoving || (v.Movement.Worker != null && v.Movement.Worker.IsBusy))) continue;
                if (((Vector2)v.transform.position - (Vector2)target.transform.position).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        /// <summary>Pushes a unit away from a point (one cell; never into water or a solid object).</summary>
        public static void Knockback(Unit unit, Vector2 from, float distance)
        {
            if (unit == null || unit.IsHidden || unit.IsPerched) return;
            Vector2 p = unit.transform.position;
            var dir = p - from;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
            var to = p + dir.normalized * distance;
            var map = KingOfNothing.World.MapData.Instance;
            if (map != null && map.IsReady && !map.IsWalkable(KingOfNothing.World.MapData.WorldToCell(to))) return;
            unit.Stop();
            unit.transform.position = new Vector3(to.x, to.y, unit.transform.position.z);
        }
    }
}
