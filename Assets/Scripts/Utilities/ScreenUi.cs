using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Screen areas covered by on-screen panels (developer panel, action menu...). Panels report their rectangle
    /// every frame; the camera does not zoom and the map ignores clicks while the mouse is over one of them.
    /// </summary>
    public static class ScreenUi
    {
        struct Entry
        {
            public Rect GuiRect;
            public int Frame;
        }

        static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Reports a panel rectangle in GUI coordinates (y down), e.g. from OnGUI.</summary>
        public static void Block(Rect guiRect)
        {
            Entries.Add(new Entry { GuiRect = guiRect, Frame = Time.frameCount });
            if (Entries.Count > 64) Entries.RemoveRange(0, Entries.Count - 64);
        }

        /// <summary>Is the screen point (y up, like Mouse.position) over a panel reported this or last frame?</summary>
        public static bool IsPointerOver(Vector2 screenPosition)
        {
            var gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            int frame = Time.frameCount;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (e.Frame < frame - 1)
                {
                    Entries.RemoveAt(i);
                    continue;
                }
                if (e.GuiRect.Contains(gui)) return true;
            }
            return false;
        }
    }
}
