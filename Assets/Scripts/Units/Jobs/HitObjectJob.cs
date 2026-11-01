using KingOfNothing.Characters;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Walk to an object (with the right tool in hand) and hit it until it breaks: chop a tree, mine a stone,
    /// cut a bush... Every swing of the pawn's interact animation is one hit; the damage lands at the impact point
    /// of the swing and shakes the object. The damage is the character's level in the tool's stat (axe:
    /// woodcutting, pickaxe: mining, knife: farming, hammer: building); every hit gives experience. A character with
    /// 0 in that stat cannot do the job at all.
    /// </summary>
    public sealed class HitObjectJob : UnitJob
    {
        readonly ResourceNode target;
        readonly string verb;
        readonly Approach approach = new Approach();
        float swingTime;
        bool swinging, impactDone;

        public HitObjectJob(ResourceNode target, string verb)
        {
            this.target = target;
            this.verb = verb;
        }

        public override string Label => $"{verb} {(target != null ? target.DisplayName : "?")}";
        public ResourceNode Target => target;
        public override object TargetObject => target;

        InteractionTool targetTool => target != null && target.Definition != null ? target.Definition.Tool : InteractionTool.Hammer;
        InteractionTool tool = InteractionTool.Hammer;
        InteractionTool Tool => tool;

        /// <summary>Villagers use the object's tool; soldiers (cutting bushes) use their own weapon.</summary>
        static InteractionTool ToolFor(Unit unit, InteractionTool objectTool)
        {
            var villager = unit != null ? unit.Villager : null;
            if (villager != null && VillagerRoles.IsSoldier(villager.Role))
                return VillagerRoles.WeaponFor(villager.Role);
            return objectTool;
        }

        /// <summary>The stat a unit uses on an object (soldiers: their weapon).</summary>
        public static StatType StatFor(Unit unit, ResourceNode node)
        {
            var objectTool = node != null && node.Definition != null ? node.Definition.Tool : InteractionTool.Hammer;
            return StatEffects.StatForTool(ToolFor(unit, objectTool));
        }

        /// <summary>Can this unit do this job at all (the stat is not 0)?</summary>
        public static bool CanWork(Unit unit, ResourceNode node)
        {
            var v = unit != null ? unit.Villager : null;
            return v == null || v.Stats.CanDo(StatFor(unit, node));
        }

        public override void Begin(Unit unit)
        {
            tool = ToolFor(unit, targetTool);
            approach.Reset();
            swinging = false;
            unit.SetWorkAnimation(Tool, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null || !target.CanBeHit) return JobState.Done;
            if (target.IsCarried) return JobState.Failed;
            var villager = unit.Villager;
            var stat = StatEffects.StatForTool(Tool);
            if (villager != null && !villager.Stats.CanDo(stat))
            {
                Debug.Log($"[Work] {villager.DisplayName} cannot {verb.ToLowerInvariant()} ({StatInfo.Name(stat)} 0).", unit);
                return JobState.Failed;
            }

            var box = target.Hitbox;
            if (!approach.TickWork(unit, box, out bool failed, target))
            {
                unit.SetWorkAnimation(Tool, false);
                swinging = false;
                return failed ? JobState.Failed : JobState.Running;
            }

            unit.SetWorkAnimation(Tool, true);
            unit.Face(box.center - (Vector2)unit.transform.position);
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            float interval = settings.hitInterval;

            if (!swinging)
            {
                swinging = true;
                impactDone = false;
                swingTime = 0f;
                unit.PlaySwing(interval, box.center - (Vector2)unit.transform.position);
            }
            swingTime += deltaTime;
            if (!impactDone && swingTime >= interval * settings.hitImpactPoint)
            {
                impactDone = true;
                if (settings.hitLunge > 0f) unit.PlayLunge(box.center - (Vector2)unit.transform.position, settings.hitLunge);
                target.ApplyHit(StatEffects.WorkDamage(villager, stat), unit.gameObject);
                if (villager != null) villager.GainXp(stat, XpFor(stat));
            }
            if (swingTime >= interval) swinging = false;
            return target == null || !target.CanBeHit ? JobState.Done : JobState.Running;
        }

        static float XpFor(StatType stat)
        {
            var s = StatSettings.Current;
            switch (stat)
            {
                case StatType.Woodcutting: return s.chopHitXp;
                case StatType.Mining: return s.mineHitXp;
                case StatType.Farming: return s.harvestHitXp;
                case StatType.Building: return s.buildHitXp;
                default: return s.meleeHitXp;
            }
        }

        /// <summary>Stops working but keeps the tool animations (the next order decides what comes next).</summary>
        public override void Cancel(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }

    /// <summary>
    /// Ruin Digger (Mining 5): dig away the ruin of a destroyed building with the pickaxe; when it is gone 25% of
    /// the building cost lies on the ground (the ruin cannot be repaired any more).
    /// </summary>
    public sealed class DigRuinsJob : UnitJob
    {
        readonly Building target;
        readonly Approach approach = new Approach();
        float swingTime;
        bool swinging, impactDone;

        public DigRuinsJob(Building target) => this.target = target;
        public override object TargetObject => target;

        public override string Label => $"Dig the ruins of {(target != null ? target.Definition?.DisplayName : "?")}";

        public override void Begin(Unit unit)
        {
            approach.Reset();
            swinging = false;
            unit.SetWorkAnimation(InteractionTool.Pickaxe, false);
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null || !target.CanBeDug) return JobState.Done;
            var v = unit.Villager;
            if (v == null || !v.Stats.Has(StatType.Mining, 5)) return JobState.Failed;
            var box = target.WorkBox;
            if (!approach.TickWork(unit, box, out bool failed, target))
            {
                unit.SetWorkAnimation(InteractionTool.Pickaxe, false);
                swinging = false;
                return failed ? JobState.Failed : JobState.Running;
            }
            unit.SetWorkAnimation(InteractionTool.Pickaxe, true);
            unit.Face(box.center - (Vector2)unit.transform.position);
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            float interval = settings.hitInterval;
            if (!swinging)
            {
                swinging = true;
                impactDone = false;
                swingTime = 0f;
                unit.PlaySwing(interval, box.center - (Vector2)unit.transform.position);
            }
            swingTime += deltaTime;
            if (!impactDone && swingTime >= interval * settings.hitImpactPoint)
            {
                impactDone = true;
                target.Dig(StatEffects.WorkDamage(v, StatType.Mining), unit.gameObject);
                v.GainXp(StatType.Mining, StatSettings.Current.mineHitXp);
            }
            if (swingTime >= interval) swinging = false;
            return JobState.Running;
        }

        public override void Cancel(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }
}
