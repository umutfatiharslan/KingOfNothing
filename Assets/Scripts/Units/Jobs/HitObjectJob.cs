using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Walk to an object (with the right tool in hand) and hit it until it breaks: chop a tree, mine a stone,
    /// cut a bush... Every swing of the pawn's interact animation is one hit; the damage lands at the impact point
    /// of the swing (InteractionSettings: 10 damage every 0.8 s until stats exist) and shakes the object.
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

        InteractionTool targetTool => target != null && target.Definition != null ? target.Definition.Tool : InteractionTool.Hammer;
        InteractionTool tool = InteractionTool.Hammer;
        InteractionTool Tool => tool;

        /// <summary>Villagers use the object's tool; soldiers (cutting bushes) use their own weapon.</summary>
        static InteractionTool ToolFor(Unit unit, InteractionTool objectTool)
        {
            var villager = unit != null ? unit.Villager : null;
            if (villager != null && KingOfNothing.Characters.VillagerRoles.IsSoldier(villager.Role))
                return KingOfNothing.Characters.VillagerRoles.WeaponFor(villager.Role);
            return objectTool;
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
                target.ApplyHit(settings.villagerHitDamage, unit.gameObject);
            }
            if (swingTime >= interval) swinging = false;
            return target == null || !target.CanBeHit ? JobState.Done : JobState.Running;
        }

        /// <summary>Stops working but keeps the tool animations (the next order decides what comes next).</summary>
        public override void Cancel(Unit unit)
        {
            Approach.Release(unit);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }
}
