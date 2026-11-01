using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Small white sprites drawn in code (tinted by the renderer): a star (stun), a drop (bleeding), a cracked shield
    /// (broken defense), a plus (healing sparkles) and an up arrow (experience popups when no icon is assigned).
    /// </summary>
    public static class ProceduralSprites
    {
        static Sprite star, drop, crackedShield, plus, arrow, dot;

        public static Sprite Star => star != null ? star : star = Make(32, StarAlpha);
        public static Sprite Drop => drop != null ? drop : drop = Make(32, DropAlpha);
        public static Sprite CrackedShield => crackedShield != null ? crackedShield : crackedShield = Make(32, ShieldAlpha);
        public static Sprite Plus => plus != null ? plus : plus = Make(16, PlusAlpha);
        public static Sprite UpArrow => arrow != null ? arrow : arrow = Make(32, ArrowAlpha);
        public static Sprite Dot => dot != null ? dot : dot = Make(16, (x, y) => (new Vector2(x, y) - new Vector2(0.5f, 0.5f)).magnitude < 0.45f ? 1f : 0f);

        delegate float AlphaAt(float x, float y);

        static Sprite Make(int size, AlphaAt alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 4x supersampling for smooth edges
                float a = 0f;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    a += alpha((x + 0.25f + sx * 0.5f) / size, (y + 0.25f + sy * 0.5f) / size);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a / 4f);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 2f);
        }

        static float StarAlpha(float x, float y)
        {
            var p = new Vector2(x - 0.5f, y - 0.5f);
            float angle = Mathf.Atan2(p.y, p.x) + Mathf.PI / 2f;
            float r = p.magnitude;
            // 5 points: radius alternates between outer and inner
            float t = Mathf.Repeat(angle / (2f * Mathf.PI) * 5f, 1f);
            float edge = Mathf.Lerp(0.2f, 0.48f, Mathf.Abs(t - 0.5f) * 2f);
            return r < edge ? 1f : 0f;
        }

        static float DropAlpha(float x, float y)
        {
            var p = new Vector2(x - 0.5f, y - 0.38f);
            if (p.magnitude < 0.28f) return 1f;
            // The tip: a triangle above the circle
            if (y > 0.38f && y < 0.95f)
            {
                float half = 0.28f * (0.95f - y) / 0.57f;
                return Mathf.Abs(x - 0.5f) < half ? 1f : 0f;
            }
            return 0f;
        }

        static float ShieldAlpha(float x, float y)
        {
            bool inside = x > 0.15f && x < 0.85f && y > 0.12f && y < 0.9f;
            if (inside && y < 0.45f)
            {
                float half = 0.35f * (y - 0.12f) / 0.33f;
                inside = Mathf.Abs(x - 0.5f) < half + 0.02f;
            }
            if (!inside) return 0f;
            // The crack: a zig-zag line through the middle
            float crackX = 0.5f + (Mathf.Repeat(y * 4f, 1f) < 0.5f ? 0.08f : -0.08f);
            return Mathf.Abs(x - crackX) < 0.04f ? 0f : 1f;
        }

        static float PlusAlpha(float x, float y)
        {
            bool vertical = Mathf.Abs(x - 0.5f) < 0.14f && y > 0.1f && y < 0.9f;
            bool horizontal = Mathf.Abs(y - 0.5f) < 0.14f && x > 0.1f && x < 0.9f;
            return vertical || horizontal ? 1f : 0f;
        }

        static float ArrowAlpha(float x, float y)
        {
            if (y > 0.5f)
            {
                float half = 0.45f * (0.95f - y) / 0.45f;
                return Mathf.Abs(x - 0.5f) < half ? 1f : 0f;
            }
            return Mathf.Abs(x - 0.5f) < 0.15f && y > 0.08f ? 1f : 0f;
        }
    }
}
