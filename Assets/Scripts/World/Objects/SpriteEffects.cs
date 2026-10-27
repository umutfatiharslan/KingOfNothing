using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>Shared materials and helpers for sprite effects (outlines, coloured bars).</summary>
    public static class SpriteEffects
    {
        static Material silhouette;

        /// <summary>Material that draws a sprite as one solid colour (the renderer colour). Null if no shader is available.</summary>
        public static Material Silhouette
        {
            get
            {
                if (silhouette != null) return silhouette;
                var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
                var shader = settings.silhouetteShader;
                if (shader == null) shader = Shader.Find("KingOfNothing/SpriteSilhouette");
                if (shader == null) shader = Shader.Find("GUI/Text Shader");
                if (shader == null) return null;
                silhouette = new Material(shader) { name = "Sprite Silhouette", hideFlags = HideFlags.DontSave };
                return silhouette;
            }
        }

        /// <summary>Top of the visible part of a sprite (its tight mesh), in world space.</summary>
        public static float VisibleTop(SpriteRenderer renderer)
        {
            var sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null) return renderer != null ? renderer.bounds.max.y : 0f;
            float top = float.MinValue;
            foreach (var v in sprite.vertices) top = Mathf.Max(top, v.y);
            if (renderer.flipY) top = -top;
            return renderer.transform.TransformPoint(new Vector3(0f, top, 0f)).y;
        }

        /// <summary>Top centre of the visible part of an object (where its health bar goes).</summary>
        public static Vector2 VisibleTopCenter(WorldObject o)
        {
            float top = float.MinValue;
            float centerX = o.GroundPoint.x;
            foreach (var r in o.Renderers)
                if (r != null) top = Mathf.Max(top, VisibleTop(r));
            if (top == float.MinValue) top = o.GroundPoint.y + 1f;
            return new Vector2(centerX, top);
        }
    }
}
