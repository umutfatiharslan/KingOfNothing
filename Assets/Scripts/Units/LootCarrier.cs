using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// Loot a character holds in its hands ("Carry loot" on a pile). Unlike carrying an object to a cell (Move),
    /// the loot stays in the hands while the character is moved around, until it is stocked in a building
    /// ("Stock loot"), dropped ("Drop loot") or the character gets other work. Wood and gold use the pawn's carry
    /// sprites; other loot is held above the head. Added automatically to every Unit.
    /// </summary>
    [DisallowMultipleComponent]
    public class LootCarrier : MonoBehaviour
    {
        Unit unit;
        WorldObject held;
        ResourceType resource;
        bool shownByPawn;

        /// <summary>The pile in the hands (null = nothing).</summary>
        public WorldObject Held => held != null && held.IsCarried ? held : null;
        public bool IsHolding => Held != null;

        void Awake() => unit = GetComponent<Unit>();

        /// <summary>Takes a pile in the hands (whatever was held before is put down first).</summary>
        public void Hold(WorldObject pile)
        {
            if (pile == null || unit == null) return;
            if (held != null && held != pile) DropHere();
            if (!pile.IsCarried) pile.PickUp();
            held = pile;
            resource = pile is ResourceNode node ? node.ResourceType : ResourceType.None;
            shownByPawn = unit.ShowsCarried(resource);
            Refresh();
        }

        /// <summary>Gives the pile away (still lifted: the caller puts it down or stores it).</summary>
        public WorldObject Release()
        {
            var pile = Held;
            held = null;
            if (pile != null) SetPileVisible(pile, true);
            if (unit != null)
            {
                unit.SetCarrying(ResourceType.None);
                unit.WorkSpeedModifier = 1f;
            }
            return pile;
        }

        /// <summary>Puts the loot on the ground next to the character.</summary>
        public void DropHere()
        {
            var pile = Release();
            if (pile != null) PutDownNear(pile, transform.position);
        }

        /// <summary>Puts a lifted object on the nearest free cell around a point.</summary>
        public static void PutDownNear(WorldObject pile, Vector2 near)
        {
            if (pile == null || !pile.IsCarried) return;
            var map = MapData.Instance;
            var cell = MapData.WorldToCell(near);
            if (map != null && map.IsReady && map.IsAreaBlocked(new RectInt(cell, pile.Footprint)))
                ItemDropper.FindFreeCell(map, new RectInt(cell, pile.Footprint), Mathf.Max(1, map.Interaction.dropFallbackRadius), out cell);
            pile.PutDown(cell);
        }

        void LateUpdate()
        {
            if (held == null) return;
            if (!held.IsCarried)
            {
                held = null;
                unit.SetCarrying(ResourceType.None);
                unit.WorkSpeedModifier = 1f;
                return;
            }
            Refresh();
        }

        void Refresh()
        {
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            unit.SetCarrying(shownByPawn ? resource : ResourceType.None);
            unit.WorkSpeedModifier = settings.carrySpeedMultiplier;
            SetPileVisible(held, !shownByPawn && !unit.IsHidden);
            Vector2 feet = unit.transform.position;
            held.SetCarriedPosition(feet + new Vector2(0f, settings.carryHeight), unit.SortingOrder + 2);
        }

        static void SetPileVisible(WorldObject pile, bool visible)
        {
            foreach (var r in pile.Renderers) if (r != null) r.enabled = visible;
        }

        void OnDisable()
        {
            if (held != null && held.IsCarried) DropHere();
        }
    }
}
