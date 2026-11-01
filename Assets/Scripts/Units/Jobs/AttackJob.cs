using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Attack targets (objects, buildings, animals, characters) until they are gone or the player gives another
    /// order. Soldiers use their weapon (warrior: sword, lancer: spear with its reach, archer: arrows within range);
    /// villagers with a level 20 tool milestone fight with that tool (hammer, axe, pickaxe, knife); other villagers
    /// only break gold mines with the pickaxe. Damage, speed, range, accuracy and the milestones come from the
    /// weapon's stat (Combo, Cleave, Impale, Whirlwind, Double Arrow, Eagle Eye...). With several targets each
    /// attacker goes on with the nearest remaining one. Moving targets are chased.
    /// </summary>
    public sealed class AttackJob : UnitJob
    {
        readonly List<ICombatTarget> targets;
        readonly Approach approach = new Approach();
        readonly bool training;
        ICombatTarget current;
        float swingTime;
        bool swinging, impactDone;
        bool warned;
        bool started;
        int hitsOnCurrent;
        bool firstShotDone;
        float nextWhirlwind;
        float nextChase;
        static readonly List<ICombatTarget> Around = new List<ICombatTarget>();

        public AttackJob(List<ResourceNode> objects)
        {
            targets = new List<ICombatTarget>();
            foreach (var o in objects) if (o != null) targets.Add(o);
        }

        public AttackJob(List<ICombatTarget> list, bool scarecrowTraining = false)
        {
            targets = new List<ICombatTarget>(list);
            training = scarecrowTraining;
        }

        public override string Label => training
            ? $"Train at {(current != null ? current.DisplayName : "the scarecrow")}"
            : $"Attack {(current != null ? current.DisplayName : $"{targets.Count} targets")}";

        public ICombatTarget Current => current;
        public override object TargetObject => current != null ? current : targets.Count > 0 ? targets[0] : null;

        VillagerRole Role(Unit unit) => unit.Villager != null ? unit.Villager.Role : VillagerRole.Villager;

        /// <summary>The weapon / tool a unit fights a target with.</summary>
        public static InteractionTool WeaponFor(Unit unit, ICombatTarget target)
        {
            var v = unit != null ? unit.Villager : null;
            var role = v != null ? v.Role : VillagerRole.Villager;
            if (VillagerRoles.IsSoldier(role)) return VillagerRoles.WeaponFor(role);
            var tool = StatEffects.FightingTool(v);
            if (tool != InteractionTool.Auto) return tool;
            // Hunters go after animals with the knife
            if (target != null && target.TargetKind == TargetKind.Animal) return InteractionTool.Knife;
            // Plain villagers only break gold mines, with the object's tool (pickaxe)
            return target is ResourceNode node && node.Definition != null ? node.Definition.Tool : InteractionTool.Pickaxe;
        }

        /// <summary>Can this unit attack at all (soldiers, villagers with a tool fight milestone)?</summary>
        public static bool CanAttack(Unit unit)
        {
            var v = unit != null ? unit.Villager : null;
            if (v == null) return true;
            if (VillagerRoles.IsSoldier(v.Role))
            {
                var weapon = StatRoller.WeaponStat(v.Role);
                return !weapon.HasValue || v.Stats.CanDo(weapon.Value);
            }
            return StatEffects.CanFight(unit);
        }

        public override void Begin(Unit unit)
        {
            approach.Reset();
            swinging = false;
            unit.SetWorkAnimation(WeaponFor(unit, targets.Count > 0 ? targets[0] : null), false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (!CombatTargets.Valid(current))
            {
                bool hadTarget = started;
                started = true;
                current = Nearest(unit);
                approach.Reset(hadTarget); // the first target interrupts the previous walk
                swinging = false;
                hitsOnCurrent = 0;
                firstShotDone = false;
                unit.CurrentTarget = current;
                if (current == null)
                {
                    Approach.Release(unit);
                    unit.SetWorkAnimation(unit.WorkTool, false);
                    unit.CurrentTarget = null;
                    return JobState.Done;
                }
            }

            var villager = unit.Villager;
            var weapon = WeaponFor(unit, current);
            var stat = StatEffects.FightStat(weapon);
            if (villager != null && !villager.Stats.CanDo(stat) && !(weapon == InteractionTool.Pickaxe && villager.Stats.CanDo(StatType.Mining)))
            {
                Warn(unit, $"cannot fight ({StatInfo.Name(stat)} 0)");
                return JobState.Failed;
            }
            var gameplay = MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;
            var interaction = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            var box = current is Building targetBuilding ? targetBuilding.WorkBox : current.Hitbox;
            bool archer = weapon == InteractionTool.Bow;
            bool lancer = weapon == InteractionTool.Spear;

            if (archer)
            {
                var tower = unit.Host as Building;
                if (unit.IsPerched && Building.HasFearOfHeights(unit))
                {
                    Warn(unit, "is afraid of heights and cannot shoot from the tower");
                    return JobState.Failed;
                }
                float range = StatEffects.ArcheryRange(villager) * (tower != null ? tower.RangeMultiplierFor(unit) : 1f) +
                              (tower != null ? tower.RangeBonusFor(unit) : 0f);
                if (training) range = Mathf.Min(range, CharacterSettings.Current.scarecrowArcherDistance);
                if (Approach.DistanceTo(unit, box) > range)
                {
                    if (unit.IsPerched)
                    {
                        Warn(unit, $"is on a tower and {current.DisplayName} is out of range ({range:0.#} cells)");
                        return JobState.Failed;
                    }
                    if (!ApproachTarget(unit, box, range - 0.5f, false, out bool lost))
                    {
                        unit.SetWorkAnimation(weapon, false);
                        swinging = false;
                        return lost ? Skip(unit) : JobState.Running;
                    }
                }
                else if (unit.IsMoving) unit.Stop();
            }
            else
            {
                float reach = lancer ? StatEffects.SpearReach(villager) : Approach.Reach(unit);
                if (!ApproachTarget(unit, box, reach, !lancer && !current.Moves, out bool failed))
                {
                    unit.SetWorkAnimation(weapon, false);
                    swinging = false;
                    return failed ? Skip(unit) : JobState.Running;
                }
            }

            unit.SetWorkAnimation(weapon, true);
            Vector2 aim = box.center - (Vector2)unit.transform.position;
            unit.Face(aim);
            float interval = archer
                ? gameplay.archerAttackInterval * StatEffects.ArcheryIntervalMultiplier(villager)
                : gameplay.meleeAttackInterval * StatEffects.MeleeIntervalMultiplier(villager, stat);
            float impact = archer ? gameplay.arrowReleasePoint : interaction.hitImpactPoint;
            if (!swinging)
            {
                swinging = true;
                impactDone = false;
                swingTime = 0f;
                // Distance Keeper (Spear 5): the first thrust at a new target comes at once
                if (lancer && hitsOnCurrent == 0 && villager != null && villager.Stats.Has(StatType.Spear, 5)) swingTime = interval * impact;
                unit.PlaySwing(interval, aim);
            }
            swingTime += deltaTime;
            if (!impactDone && swingTime >= interval * impact)
            {
                impactDone = true;
                if (archer) Shoot(unit, villager, gameplay);
                else Strike(unit, villager, weapon, stat, aim);
            }
            if (swingTime >= interval) swinging = false;
            if (lancer) TryWhirlwind(unit, villager);
            return JobState.Running;
        }

        /// <summary>Walks to a static target's work spot, or chases a moving one.</summary>
        bool ApproachTarget(Unit unit, Rect box, float reach, bool workSpot, out bool failed)
        {
            failed = false;
            if (workSpot) return approach.TickWork(unit, box, out failed, current as WorldObject);
            if (!current.Moves) return approach.Tick(unit, box, reach, out failed, current as WorldObject);
            float distance = Approach.DistanceTo(unit, box);
            if (distance <= reach)
            {
                if (unit.IsMoving) unit.Stop();
                return true;
            }
            if (distance > 60f)
            {
                failed = true;
                return false;
            }
            if (!unit.IsMoving || Time.time >= nextChase)
            {
                nextChase = Time.time + 0.5f;
                unit.MoveTo(current.GroundPoint);
            }
            return false;
        }

        void Strike(Unit unit, Villager villager, InteractionTool weapon, StatType stat, Vector2 aim)
        {
            var s = StatSettings.Current;
            var interaction = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            if (interaction.hitLunge > 0f) unit.PlayLunge(aim, interaction.hitLunge);
            var hit = CombatRules.MakeHit(unit, current, weapon, false);
            hit.Training = training;
            hitsOnCurrent++;
            // Combo (Sword 10): every second hit on the same target is stronger
            if (villager != null && weapon == InteractionTool.Sword && villager.Stats.Has(StatType.Sword, 10) && hitsOnCurrent % 2 == 0)
                hit.Damage = Mathf.RoundToInt(hit.Damage * (1f + s.comboBonus10));
            var target = current;
            target.ReceiveHit(hit);
            if (villager != null)
            {
                villager.GainXp(stat, stat == StatType.Hunting ? s.huntHitXp : s.meleeHitXp);
                // Scarecrow training also teaches defense
                if (training && hitsOnCurrent % Mathf.Max(1, s.defenseTrainingHits) == 0) villager.GainXp(StatType.Defense, 1f);
            }
            if (training || villager == null) return;

            var half = hit;
            // Cleave (Sword 15): a second enemy next to the target
            if (weapon == InteractionTool.Sword && villager.Stats.Has(StatType.Sword, 15))
            {
                CombatTargets.Around(target.GroundPoint, s.cleaveRadius15, target.TargetKind, target, unit, Around);
                if (Around.Count > 0)
                {
                    half.Damage = Mathf.Max(1, Mathf.RoundToInt(hit.Damage * s.cleaveDamage15));
                    Around[0].ReceiveHit(half);
                }
            }
            // Impale (Spear 15): the next target in line
            if (weapon == InteractionTool.Spear && villager.Stats.Has(StatType.Spear, 15))
            {
                var dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
                CombatTargets.Around(target.GroundPoint + dir * s.pierceCells15 * 0.5f, s.pierceCells15 * 0.6f, target.TargetKind, target, unit, Around);
                foreach (var t in Around)
                {
                    if (Vector2.Dot(t.GroundPoint - target.GroundPoint, dir) <= 0f) continue;
                    t.ReceiveHit(hit);
                    break;
                }
            }
            // Friendly fire of lancers (20 - spear %): an ally next to the target is hit too
            if (weapon == InteractionTool.Spear && Random.value < StatEffects.FriendlyFire(villager, StatType.Spear))
                HitAllyNear(target.GroundPoint, unit, hit);
        }

        /// <summary>Whirlwind (Spear 20): every few seconds every enemy around the lancer is hit.</summary>
        void TryWhirlwind(Unit unit, Villager villager)
        {
            if (training || villager == null || !villager.Stats.Has(StatType.Spear, 20) || current == null) return;
            if (Time.time < nextWhirlwind) return;
            var s = StatSettings.Current;
            if (Approach.DistanceTo(unit, current.Hitbox) > s.whirlwindRadius20) return;
            nextWhirlwind = Time.time + s.whirlwindInterval20;
            CombatTargets.Around(unit.transform.position, s.whirlwindRadius20, current.TargetKind, null, unit, Around);
            var hit = CombatRules.MakeHit(unit, current, InteractionTool.Spear, false);
            foreach (var t in Around) t.ReceiveHit(hit);
            // The attack swing in every direction
            unit.PlaySwing(0.35f, Vector2.up);
            FloatingText.Show(unit.transform, "Whirlwind!", Color.white);
        }

        void Shoot(Unit unit, Villager villager, GameplaySettings gameplay)
        {
            var s = StatSettings.Current;
            var from = (Vector2)unit.transform.position + new Vector2(0f, gameplay.arrowLaunchHeight);
            var hit = CombatRules.MakeHit(unit, current, InteractionTool.Bow, true);
            hit.Training = training;
            // Eagle Eye (Archery 20): the first shot at a new target
            if (!firstShotDone && villager != null && villager.Stats.Has(StatType.Archery, 20) && !training)
                hit.Damage = Mathf.RoundToInt(hit.Damage * s.eagleFirstShotMultiplier20);
            firstShotDone = true;
            Arrow.Launch(from, current, hit, unit, gameplay);
            if (villager != null) villager.GainXp(StatType.Archery, s.arrowXp);
            // Double Arrow (Archery 10)
            if (villager != null && villager.Stats.Has(StatType.Archery, 10) && Random.value < s.doubleShotChance10)
            {
                var second = CombatRules.MakeHit(unit, current, InteractionTool.Bow, true);
                second.Training = training;
                Arrow.Launch(from + new Vector2(0f, 0.12f), current, second, unit, gameplay, 0.08f);
            }
            hitsOnCurrent++;
        }

        /// <summary>Friendly fire: an ally standing within a cell of the point takes the hit.</summary>
        public static void HitAllyNear(Vector2 point, Unit attacker, HitInfo hit)
        {
            foreach (var v in Villager.All)
            {
                if (v == null || v.Movement == attacker || !v.IsAlive) continue;
                if (((Vector2)v.transform.position - point).sqrMagnitude > 1f) continue;
                v.ReceiveHit(hit);
                if (attacker != null && attacker.Villager != null)
                    attacker.Villager.AddJournal($"Hit {v.DisplayName} by accident (friendly fire).");
                return;
            }
        }

        /// <summary>The current target cannot be reached: leave it out and go on with the others.</summary>
        JobState Skip(Unit unit)
        {
            targets.Remove(current);
            current = null;
            Approach.Release(unit);
            return targets.Count > 0 ? JobState.Running : JobState.Failed;
        }

        void Warn(Unit unit, string text)
        {
            if (warned) return;
            warned = true;
            Debug.Log($"[Combat] {unit.name} {text}.", unit);
        }

        ICombatTarget Nearest(Unit unit)
        {
            targets.RemoveAll(t => !CombatTargets.Valid(t) || (t is Villager v && v.Movement == unit));
            ICombatTarget best = null;
            float bestDistance = float.MaxValue;
            Vector2 from = unit.transform.position;
            foreach (var t in targets)
            {
                float d = (t.GroundPoint - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            return best;
        }

        public override void Cancel(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
            unit.CurrentTarget = null;
        }
    }

    /// <summary>
    /// An arrow flying in an arc to its target with a short air trail behind it. It can miss (accuracy = 40 + 3 x
    /// archery %, at most 95%): a missed arrow lands next to the target and stays stuck in the ground (the headless
    /// arrow of Arrow.png) for a few game hours, and may hit an ally standing there (friendly fire 20 - archery %,
    /// none with Wind Reader). A hit damages the target if it still stands; Piercing Shot (Archery 15) goes on to a
    /// target behind when the first one falls.
    /// </summary>
    public class Arrow : MonoBehaviour
    {
        Vector2 start, end;
        float duration, time, arc, delay;
        ICombatTarget target;
        HitInfo hit;
        bool miss;
        bool pierced;
        Unit shooter;
        SpriteRenderer spriteRenderer;
        GameplaySettings settings;
        static readonly List<ICombatTarget> Around = new List<ICombatTarget>();

        public static void Launch(Vector2 from, ICombatTarget target, HitInfo hit, Unit shooter, GameplaySettings settings, float delay = 0f, bool pierced = false, bool neverMiss = false)
        {
            if (!CombatTargets.Valid(target)) return;
            var box = target.Hitbox;
            var to = new Vector2(box.center.x, box.yMin + Mathf.Min(box.height, 0.6f));

            var villager = shooter != null ? shooter.Villager : null;
            float accuracy = StatEffects.ArcheryAccuracy(villager);
            bool miss = !neverMiss && Random.value > accuracy;
            if (miss)
            {
                float distanceOff = Random.Range(settings.missDistance.x, Mathf.Max(settings.missDistance.x, settings.missDistance.y));
                to = new Vector2(box.center.x, box.yMin) + Random.insideUnitCircle.normalized * distanceOff;
            }

            var go = new GameObject("Arrow");
            var arrow = go.AddComponent<Arrow>();
            arrow.settings = settings;
            arrow.spriteRenderer = go.AddComponent<SpriteRenderer>();
            arrow.spriteRenderer.sprite = settings.arrowSprite;
            arrow.spriteRenderer.sortingOrder = short.MaxValue - 60;
            go.transform.localScale = new Vector3(settings.arrowScale, settings.arrowScale, 1f);
            arrow.start = from;
            arrow.end = to;
            float distance = Vector2.Distance(from, to);
            arrow.duration = Mathf.Max(0.05f, distance / Mathf.Max(0.5f, settings.arrowSpeed));
            arrow.arc = settings.arrowArc * distance / 10f;
            arrow.target = target;
            arrow.hit = hit;
            arrow.miss = miss;
            arrow.shooter = shooter;
            arrow.delay = delay;
            arrow.pierced = pierced;
            go.transform.position = from;
            if (arrow.spriteRenderer.sprite == null) arrow.spriteRenderer.enabled = false;
            if (delay > 0f) arrow.spriteRenderer.enabled = false;
            arrow.AddTrail();
        }

        static Material trailMaterial;

        void AddTrail()
        {
            if (settings.arrowTrailTime <= 0f) return;
            if (trailMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) trailMaterial = new Material(shader) { name = "Arrow Trail" };
            }
            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = settings.arrowTrailTime;
            trail.startWidth = settings.arrowTrailWidth;
            trail.endWidth = 0f;
            var c = settings.arrowTrailColor;
            trail.startColor = c;
            trail.endColor = new Color(c.r, c.g, c.b, 0f);
            trail.sortingOrder = short.MaxValue - 61;
            trail.numCapVertices = 2;
        }

        Vector2 PointAt(float t) => Vector2.Lerp(start, end, t) + Vector2.up * (4f * arc * t * (1f - t));

        void Update()
        {
            if (delay > 0f)
            {
                delay -= Time.deltaTime;
                if (delay > 0f) return;
                if (settings.arrowSprite != null) spriteRenderer.enabled = true;
            }
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            var p = PointAt(t);
            var ahead = PointAt(Mathf.Min(1f, t + 0.02f)) - p;
            if (t >= 1f) ahead = p - PointAt(0.98f);
            transform.position = new Vector3(p.x, p.y, 0f);
            if (ahead.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg);
            if (t < 1f) return;
            if (miss) Missed(p);
            else Landed(p);
            Destroy(gameObject);
        }

        void Missed(Vector2 p)
        {
            var villager = shooter != null ? shooter.Villager : null;
            // Friendly fire: an ally right where the arrow comes down
            if (!hit.Training && villager != null && Random.value < StatEffects.FriendlyFire(villager, KingOfNothing.Characters.StatType.Archery))
            {
                foreach (var v in Villager.All)
                {
                    if (v == null || v.Movement == shooter || !v.IsAlive) continue;
                    if (((Vector2)v.transform.position - p).sqrMagnitude > 0.8f) continue;
                    v.ReceiveHit(hit);
                    villager.AddJournal($"Hit {v.DisplayName} by accident (friendly fire).");
                    return;
                }
            }
            StickInGround(p, transform.rotation);
        }

        void Landed(Vector2 p)
        {
            if (!CombatTargets.Valid(target)) return;
            target.ReceiveHit(hit);
            var villager = shooter != null ? shooter.Villager : null;
            if (villager != null) villager.GainXp(KingOfNothing.Characters.StatType.Archery, StatSettings.Current.arrowHitXp);
            // Piercing Shot (Archery 15): the arrow goes on to a target behind when the first one fell
            if (hit.Training || pierced || villager == null || !villager.Stats.Has(KingOfNothing.Characters.StatType.Archery, 15)) return;
            if (CombatTargets.Valid(target)) return;
            var s = StatSettings.Current;
            var dir = (end - start).normalized;
            CombatTargets.Around(p + dir * s.pierceRadius15 * 0.5f, s.pierceRadius15, target.TargetKind, target, shooter, Around);
            foreach (var next in Around)
            {
                if (Vector2.Dot(next.GroundPoint - p, dir) <= 0f) continue;
                var weaker = hit;
                weaker.Damage = Mathf.Max(1, Mathf.RoundToInt(hit.Damage * s.pierceDamage15));
                Launch(p, next, weaker, shooter, settings, 0f, true, true);
                break;
            }
        }

        /// <summary>The missed arrow (without its head, as if it went into the ground) stays for a few game hours.</summary>
        void StickInGround(Vector2 point, Quaternion rotation)
        {
            if (settings.stuckArrowGameHours <= 0f) return;
            var sprite = settings.stuckArrowSprite != null ? settings.stuckArrowSprite : settings.arrowSprite;
            if (sprite == null) return;
            var go = new GameObject("Stuck Arrow");
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = MapSorting.Order(point.x, point.y);
            go.transform.position = new Vector3(point.x, point.y + 0.05f, 0f);
            // Steep, as if it went into the ground
            float z = rotation.eulerAngles.z;
            float steep = (z > 90f && z < 270f) ? 235f : -55f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, steep);
            go.transform.localScale = new Vector3(settings.arrowScale, settings.arrowScale, 1f);
            Destroy(go, settings.stuckArrowGameHours * settings.HourSeconds);
        }
    }
}
