using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Helper for child objects that a component builds itself at runtime (name tags, selection rings, path lines).
    /// If such a child was ever saved into a prefab or scene, creating it again would draw it twice (two names on
    /// top of each other); this replaces the saved copies instead.
    /// </summary>
    public static class RuntimeChild
    {
        /// <summary>Removes every direct child with this name and returns a new, empty one.</summary>
        public static GameObject Replace(Transform parent, string name)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name != name) continue;
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else Object.DestroyImmediate(child.gameObject);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
