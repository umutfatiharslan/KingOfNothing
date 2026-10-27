using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Default mode moving: walk to an object, lift it, carry it to the target cell and put it down there.
    /// If the target cell got occupied meanwhile the object goes to the nearest free cell (objects never overlap
    /// in Default mode). Cancelling puts the object down next to the character.
    /// </summary>
    public sealed class CarryObjectJob : UnitJob
    {
        enum Phase
        {
            GoToObject,
            Carry,
        }

        readonly WorldObject target;
        readonly Vector2Int destination;
        readonly Approach approach = new Approach();
        Phase phase;

        public CarryObjectJob(WorldObject target, Vector2Int destination)
        {
            this.target = target;
            this.destination = destination;
        }

        public override string Label => $"Carry {(target != null ? target.DisplayName : "?")} to {destination}";
        public WorldObject Target => target;

        public override void Begin(Unit unit)
        {
            approach.Reset();
            phase = Phase.GoToObject;
        }

        public override JobState Tick(Unit unit, float deltaTime)
        {
            if (target == null) return JobState.Failed;
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;

            if (phase == Phase.GoToObject)
            {
                if (target.IsCarried) return JobState.Failed; // someone else took it
                var area = new Rect(target.Area.position, target.Area.size);
                if (!approach.Tick(unit, area, Approach.Reach(unit), out bool failed, target))
                    return failed ? JobState.Failed : JobState.Running;

                target.PickUp();
                // Wood / gold / meat piles: the pawn sprite shows the load, the pile itself is hidden while carried
                var resource = target is ResourceNode node && target.Definition != null && target.Definition.Kind == WorldObjectKind.Item
                    ? node.ResourceType : ResourceType.None;
                hidden = unit.ShowsCarried(resource);
                unit.SetCarrying(hidden ? resource : ResourceType.None);
                SetVisible(!hidden);
                unit.WorkSpeedModifier = settings.carrySpeedMultiplier;
                phase = Phase.Carry;
                approach.Reset(true); // the walk below belongs to this job
                unit.MoveTo(MapData.CellCenter(destination));
            }

            HoldAboveHead(unit, settings);
            var cellArea = new Rect(destination, target.Footprint);
            if (!approach.Tick(unit, cellArea, Approach.Reach(unit) + 0.3f, out bool lost))
            {
                if (!lost) return JobState.Running;
                PutDownNear(unit);
                return JobState.Failed;
            }
            PutDownAt(unit, destination);
            return JobState.Done;
        }

        void HoldAboveHead(Unit unit, InteractionSettings settings)
        {
            Vector2 feet = unit.transform.position;
            target.SetCarriedPosition(feet + new Vector2(0f, settings.carryHeight), unit.SortingOrder + 2);
        }

        bool hidden;

        void SetVisible(bool visible)
        {
            if (target == null) return;
            foreach (var r in target.Renderers) if (r != null) r.enabled = visible;
        }

        void PutDownAt(Unit unit, Vector2Int cell)
        {
            var map = MapData.Instance;
            unit.WorkSpeedModifier = 1f;
            unit.SetCarrying(ResourceType.None);
            if (hidden) SetVisible(true);
            hidden = false;
            if (map == null)
            {
                target.PutDown(cell);
                return;
            }
            var area = new RectInt(cell, target.Footprint);
            if (map.IsAreaBlocked(area))
            {
                var settings = map.Interaction;
                if (!ItemDropper.FindFreeCell(map, area, settings.dropFallbackRadius, out cell))
                    cell = MapData.WorldToCell(unit.transform.position);
            }
            target.PutDown(cell);
        }

        void PutDownNear(Unit unit) => PutDownAt(unit, MapData.WorldToCell(unit.transform.position));

        public override void Cancel(Unit unit)
        {
            if (target != null && target.IsCarried && phase == Phase.Carry) PutDownNear(unit);
            unit.WorkSpeedModifier = 1f;
            unit.SetCarrying(ResourceType.None);
        }
    }
}
