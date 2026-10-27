using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Look of a selected object: its sprites are tinted with the selection colour (InteractionSettings,
    /// orange by default; only the colour changes, the transparency stays with the proximity fade) and its health
    /// bar stays visible while it is selected. Added when the object is selected, removed when it is deselected.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionHighlight : MonoBehaviour
    {
        WorldObject target;
        bool pinned;

        public static void Show(WorldObject o)
        {
            if (o == null || !Application.isPlaying) return;
            var highlight = o.GetComponent<SelectionHighlight>();
            if (highlight == null) highlight = o.gameObject.AddComponent<SelectionHighlight>();
            highlight.Setup(o);
        }

        public static void Hide(WorldObject o)
        {
            if (o == null) return;
            var highlight = o.GetComponent<SelectionHighlight>();
            if (highlight != null) Destroy(highlight);
        }

        void Setup(WorldObject o)
        {
            if (target == o) return;
            target = o;
            if (o is ResourceNode node && !node.IsCollectable)
            {
                HealthBar.Pin(node);
                pinned = true;
            }
            Apply(true);
        }

        void LateUpdate() => Apply(true);

        void Apply(bool on)
        {
            if (target == null) return;
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            var tint = on ? settings.selectedTint : Color.white;
            foreach (var r in target.Renderers)
            {
                if (r == null) continue;
                r.color = new Color(tint.r, tint.g, tint.b, r.color.a);
            }
        }

        void OnDestroy()
        {
            Apply(false);
            if (pinned && target is ResourceNode node) HealthBar.Unpin(node);
        }
    }
}
