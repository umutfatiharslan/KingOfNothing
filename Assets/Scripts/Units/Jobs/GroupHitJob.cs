using KingOfNothing.World.Objects;

namespace KingOfNothing.Units
{
    /// <summary>Works through a WorkGroup: hits its current object, then asks the group for the next one.</summary>
    public sealed class GroupHitJob : UnitJob
    {
        readonly WorkGroup group;
        readonly string verb;
        HitObjectJob current;
        ResourceNode currentTarget;

        public GroupHitJob(WorkGroup group, string verb)
        {
            this.group = group;
            this.verb = verb;
        }

        public override string Label => $"{verb} ({group.Remaining} left{(group.Together ? ", together" : "")})";

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (current == null || currentTarget == null || !currentTarget.CanBeHit)
            {
                var finished = currentTarget;
                currentTarget = group.Next(unit, finished);
                if (currentTarget == null)
                {
                    // The last action's animations (tool in hand) stay until the next order
                    unit.SetWorkAnimation(unit.WorkTool, false);
                    return JobState.Done;
                }
                current = new HitObjectJob(currentTarget, verb);
                current.Begin(unit);
            }

            var state = current.Tick(unit, deltaTime);
            if (state == JobState.Failed)
            {
                // Unreachable: let the others have a go at the rest
                group.Release(unit, currentTarget, true);
                current = null;
                currentTarget = null;
            }
            else if (state == JobState.Done) current = null;
            return JobState.Running;
        }

        public override void Cancel(Unit unit)
        {
            current?.Cancel(unit);
            group.Release(unit, currentTarget, false);
            unit.SetWorkAnimation(unit.WorkTool, false);
        }
    }
}
