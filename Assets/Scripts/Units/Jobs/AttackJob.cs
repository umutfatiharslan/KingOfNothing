using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Soldiers attack objects (any object with health, buildings included) until they are destroyed or the
    /// player gives another order. Warriors hit with their sword, lancers with their spear (the attack sheet of
    /// the direction), both standing beside the target like workers; archers stop within range (10 cells, x2 on a
    /// tower) and shoot arrows. With several targets each soldier goes on with the nearest remaining one.
    /// </summary>
    public sealed class AttackJob : UnitJob
    {
        readonly List<ResourceNode> targets;
        readonly Approach approach = new Approach();
        ResourceNode current;
        float swingTime;
        bool swinging, impactDone;
        bool warned;
        bool started;

        public AttackJob(List<ResourceNode> targets) => this.targets = targets;

        public override string Label => $"Attack {(current != null ? current.DisplayName : $"{targets.Count} targets")}";

        VillagerRole Role(Unit unit) => unit.Villager != null ? unit.Villager.Role : VillagerRole.Villager;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            swinging = false;
            var role = Role(unit);
            unit.SetWorkAnimation(VillagerRoles.IsSoldier(role) ? VillagerRoles.WeaponFor(role) : InteractionTool.Pickaxe, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (current == null || !current.CanBeHit)
            {
                bool hadTarget = started;
                started = true;
                current = Nearest(unit);
                approach.Reset(hadTarget); // the first target interrupts the previous walk
                swinging = false;
                if (current == null)
                {
                    Approach.Release(unit);
                    unit.SetWorkAnimation(unit.WorkTool, false);
                    return JobState.Done;
                }
            }

            var role = Role(unit);
            // Villagers (attacking a gold mine) swing the object's tool (pickaxe)
            bool soldier = VillagerRoles.IsSoldier(role);
            var weapon = soldier ? VillagerRoles.WeaponFor(role) : current.Definition != null ? current.Definition.Tool : InteractionTool.Pickaxe;
            var gameplay = MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;
            var interaction = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            var box = current is Building targetBuilding ? targetBuilding.WorkBox : current.Hitbox;
            bool archer = role == VillagerRole.Archer;

            if (archer)
            {
                var tower = unit.Host as Building;
                if (unit.IsPerched && Building.HasFearOfHeights(unit))
                {
                    Warn(unit, "is afraid of heights and cannot shoot from the tower");
                    return JobState.Failed;
                }
                float range = gameplay.archerRange * (tower != null ? tower.RangeMultiplierFor(unit) : 1f);
                if (Approach.DistanceTo(unit, box) > range)
                {
                    if (unit.IsPerched)
                    {
                        Warn(unit, $"is on a tower and {current.DisplayName} is out of range ({range:0} cells)");
                        return JobState.Failed;
                    }
                    if (!approach.Tick(unit, box, range - 0.5f, out bool lost, current))
                    {
                        unit.SetWorkAnimation(weapon, false);
                        swinging = false;
                        return lost ? Skip(unit) : JobState.Running;
                    }
                }
                else if (unit.IsMoving) unit.Stop();
            }
            else if (!approach.TickWork(unit, box, out bool failed, current))
            {
                unit.SetWorkAnimation(weapon, false);
                swinging = false;
                return failed ? Skip(unit) : JobState.Running;
            }

            unit.SetWorkAnimation(weapon, true);
            Vector2 aim = box.center - (Vector2)unit.transform.position;
            unit.Face(aim);
            float interval = archer ? gameplay.archerAttackInterval : gameplay.meleeAttackInterval;
            if (!swinging)
            {
                swinging = true;
                impactDone = false;
                swingTime = 0f;
                unit.PlaySwing(interval, aim);
            }
            swingTime += deltaTime;
            float impact = archer ? gameplay.arrowReleasePoint : interaction.hitImpactPoint;
            if (!impactDone && swingTime >= interval * impact)
            {
                impactDone = true;
                if (archer)
                {
                    var from = (Vector2)unit.transform.position + new Vector2(0f, gameplay.arrowLaunchHeight);
                    Arrow.Launch(from, current, gameplay.archerDamage, unit.gameObject, gameplay);
                }
                else
                {
                    int damage = role == VillagerRole.Lancer ? gameplay.lancerDamage
                               : role == VillagerRole.Warrior ? gameplay.warriorDamage
                               : interaction.villagerHitDamage;
                    current.ApplyHit(damage, unit.gameObject);
                }
            }
            if (swingTime >= interval) swinging = false;
            return JobState.Running;
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

        ResourceNode Nearest(Unit unit)
        {
            targets.RemoveAll(t => t == null || !t.CanBeHit);
            ResourceNode best = null;
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
        }
    }

    /// <summary>
    /// An arrow flying in an arc to its target with a short air trail behind it. It can miss (the better the
    /// shooter's archery stat, the rarer): a missed arrow lands next to the target and stays stuck in the ground
    /// (the headless arrow of Arrow.png) for a few game hours. A hit damages the target if it still stands.
    /// </summary>
    public class Arrow : MonoBehaviour
    {
        Vector2 start, end;
        float duration, time, arc;
        ResourceNode target;
        int damage;
        bool miss;
        GameObject shooter;
        SpriteRenderer spriteRenderer;
        GameplaySettings settings;

        public static void Launch(Vector2 from, ResourceNode target, int damage, GameObject shooter, GameplaySettings settings)
        {
            if (target == null) return;
            var box = target.Hitbox;
            var to = new Vector2(box.center.x, box.yMin + Mathf.Min(box.height, 0.6f));

            // Miss chance from the archery stat (0..20)
            var villager = shooter != null ? shooter.GetComponent<Villager>() : null;
            int archery = villager != null ? villager.Stats.Get(StatType.Archery) : 0;
            float missChance = Mathf.Lerp(settings.missChanceAtArcheryZero, settings.missChanceAtArcheryMax, archery / (float)StatBlock.MaxLevel);
            bool miss = Random.value < missChance;
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
            arrow.damage = damage;
            arrow.miss = miss;
            arrow.shooter = shooter;
            go.transform.position = from;
            if (arrow.spriteRenderer.sprite == null) arrow.spriteRenderer.enabled = false;
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
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            var p = PointAt(t);
            var ahead = PointAt(Mathf.Min(1f, t + 0.02f)) - p;
            if (t >= 1f) ahead = p - PointAt(0.98f);
            transform.position = new Vector3(p.x, p.y, 0f);
            if (ahead.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg);
            if (t < 1f) return;
            if (miss) StickInGround(p, transform.rotation);
            else if (target != null && target.CanBeHit) target.ApplyHit(damage, shooter);
            Destroy(gameObject);
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
