using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Small health bar (SmallBar_Base + SmallBar_Fill) above an object. It appears when the object is hit, stays
    /// for a few seconds after the last hit and then fades out (InteractionSettings: 3 s + 1 s), and it stays while
    /// the object is selected. The fill is green above 60 %, yellow from 25 % to 60 % and red below 25 %.
    /// Added on demand and removed when hidden, so idle objects cost nothing.
    /// </summary>
    public class HealthBar : MonoBehaviour
    {
        // Pixel layout of SmallBar_Base (192 x 64): the fill slot is 74 px wide, starting 37 px left of the centre
        const float SlotLeftPixels = -37f;
        const float SlotWidthPixels = 74f;
        const float SlotHeightPixels = 3f;
        const float SlotCenterPixels = 0.5f; // the slot (rows 30-32 from the top of 64) sits half a pixel above the sprite centre
        const float PixelsPerUnit = 64f;
        const int SortingOrder = short.MaxValue - 30;

        ResourceNode node;
        Transform root;
        SpriteRenderer baseRenderer, fillRenderer;
        float lastHitTime = -999f;
        int pinCount;

        /// <summary>Shows the bar after a hit.</summary>
        public static void Notify(ResourceNode target)
        {
            var bar = Get(target);
            if (bar != null) bar.lastHitTime = Time.time;
        }

        /// <summary>Keeps the bar visible (selected objects) until Unpin.</summary>
        public static void Pin(ResourceNode target)
        {
            var bar = Get(target);
            if (bar != null) bar.pinCount++;
        }

        public static void Unpin(ResourceNode target)
        {
            if (target == null) return;
            var bar = target.GetComponent<HealthBar>();
            if (bar != null) bar.pinCount = Mathf.Max(0, bar.pinCount - 1);
        }

        static HealthBar Get(ResourceNode target)
        {
            if (target == null || target.IsCollectable || !Application.isPlaying) return null;
            var bar = target.GetComponent<HealthBar>();
            if (bar == null)
            {
                bar = target.gameObject.AddComponent<HealthBar>();
                if (!bar.Build(target))
                {
                    Destroy(bar);
                    return null;
                }
            }
            return bar;
        }

        bool Build(ResourceNode target)
        {
            node = target;
            var settings = Settings;
            if (settings.healthBarBase == null) return false;

            _ = target.Renderers; // cache the object's own renderers before adding ours
            root = new GameObject("Health Bar").transform;
            root.SetParent(transform, false);
            baseRenderer = new GameObject("Base", typeof(SpriteEffectMarker)).AddComponent<SpriteRenderer>();
            baseRenderer.transform.SetParent(root, false);
            baseRenderer.sprite = settings.healthBarBase;
            baseRenderer.sortingOrder = SortingOrder;

            // The fill is a plain white pixel coloured by the renderer (SmallBar_Fill itself is red and would not
            // take green / yellow); it is stretched over the slot of SmallBar_Base
            fillRenderer = new GameObject("Fill", typeof(SpriteEffectMarker)).AddComponent<SpriteRenderer>();
            fillRenderer.transform.SetParent(root, false);
            fillRenderer.sprite = WhitePixel;
            fillRenderer.sortingOrder = SortingOrder + 1;
            Refresh(1f);
            return true;
        }

        static Sprite whitePixel;

        /// <summary>1x1 white sprite, 1 unit large (scaled to the slot).</summary>
        static Sprite WhitePixel
        {
            get
            {
                if (whitePixel != null) return whitePixel;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                whitePixel = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                whitePixel.hideFlags = HideFlags.DontSave;
                return whitePixel;
            }
        }

        static InteractionSettings Settings => MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;

        void LateUpdate()
        {
            if (node == null || root == null)
            {
                Destroy(this);
                return;
            }
            var settings = Settings;
            float alpha = 1f;
            if (pinCount <= 0)
            {
                float since = Time.time - lastHitTime;
                if (since > settings.healthBarShowSeconds + settings.healthBarFadeSeconds)
                {
                    Destroy(root.gameObject);
                    Destroy(this);
                    return;
                }
                if (since > settings.healthBarShowSeconds)
                    alpha = 1f - (since - settings.healthBarShowSeconds) / settings.healthBarFadeSeconds;
            }
            Refresh(alpha);
        }

        void Refresh(float alpha)
        {
            var settings = Settings;
            float share = node.MaxHealth > 0 ? Mathf.Clamp01(node.Health / (float)node.MaxHealth) : 0f;

            // Position above the visible top, independent of the object's own scale / shake
            var top = SpriteEffects.VisibleTopCenter(node);
            root.position = new Vector3(top.x, top.y + settings.healthBarOffset, 0f);
            float scale = settings.healthBarScale;
            var parentScale = transform.lossyScale;
            root.localScale = new Vector3(scale / Mathf.Max(0.0001f, parentScale.x), scale / Mathf.Max(0.0001f, parentScale.y), 1f);

            float width = SlotWidthPixels * share;
            fillRenderer.transform.localPosition = new Vector3((SlotLeftPixels + width * 0.5f) / PixelsPerUnit, SlotCenterPixels / PixelsPerUnit, 0f);
            fillRenderer.transform.localScale = new Vector3(width / PixelsPerUnit, SlotHeightPixels / PixelsPerUnit, 1f);
            fillRenderer.enabled = share > 0f;

            var color = share > settings.healthBarGreenAbove ? settings.healthBarGreen
                : share >= settings.healthBarRedBelow ? settings.healthBarYellow : settings.healthBarRed;
            color.a *= alpha;
            fillRenderer.color = color;
            baseRenderer.color = new Color(1f, 1f, 1f, alpha);
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }
    }
}
