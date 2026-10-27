using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Shared look of the on-screen panels (developer tools, actions menu): an opaque dark window, so nothing of the
    /// map or of another panel shines through and the text stays sharp. Use only inside OnGUI.
    /// </summary>
    public static class UiStyle
    {
        static GUIStyle window;
        static Texture2D background;

        public static Color WindowColor = new Color(0.11f, 0.11f, 0.13f, 1f);
        public static Color BorderColor = new Color(0.32f, 0.32f, 0.36f, 1f);

        public static GUIStyle Window
        {
            get
            {
                if (window != null && background != null) return window;
                background = MakeBackground(16, 16, WindowColor, BorderColor);
                window = new GUIStyle(GUI.skin.window)
                {
                    border = new RectOffset(2, 2, 2, 2),
                    padding = new RectOffset(8, 8, 24, 8),
                    contentOffset = new Vector2(0f, -18f),
                    alignment = TextAnchor.UpperCenter,
                    fontStyle = FontStyle.Bold,
                };
                foreach (var state in new[] { window.normal, window.onNormal, window.hover, window.onHover, window.focused, window.onFocused, window.active, window.onActive })
                {
                    state.background = background;
                    state.textColor = Color.white;
                }
                return window;
            }
        }

        static Texture2D MakeBackground(int width, int height, Color fill, Color border)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                pixels[y * width + x] = edge ? border : fill;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
