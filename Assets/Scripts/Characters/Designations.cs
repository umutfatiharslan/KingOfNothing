using System.Collections.Generic;
using KingOfNothing.Animals;
using KingOfNothing.World;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>What the player marked an object for (RimWorld style designations).</summary>
    public enum DesignationType
    {
        None,
        /// <summary>Trees and stumps: woodcutters chop them on their own.</summary>
        Chop,
        /// <summary>Stones and gold stones: miners mine them on their own.</summary>
        Mine,
        /// <summary>Pumpkins, mushrooms, bushes: farmers harvest them on their own.</summary>
        Harvest,
    }

    /// <summary>
    /// Objects the player marked for work (right click without characters, or "Mark ..." in the actions menu).
    /// Workers only chop, mine and harvest marked objects on their own; a small tool icon shows the mark. Sheep
    /// are marked for hunting on the sheep itself. Saved with the characters.
    /// </summary>
    public static class Designations
    {
        static readonly Dictionary<WorldObject, DesignationType> Marks = new Dictionary<WorldObject, DesignationType>();
        static readonly Dictionary<WorldObject, SpriteRenderer> Markers = new Dictionary<WorldObject, SpriteRenderer>();
        static readonly List<WorldObject> Stale = new List<WorldObject>();
        static bool subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Marks.Clear();
            Markers.Clear();
            subscribed = false;
        }

        static void Ensure()
        {
            if (subscribed) return;
            subscribed = true;
            WorldObject.AnyRemoving += o => Unmark(o);
        }

        /// <summary>The designation an object can get (None = it cannot be marked).</summary>
        public static DesignationType For(WorldObject o)
        {
            if (!(o is ResourceNode node) || node.IsCollectable || !node.CanBeHit || o.Definition == null) return DesignationType.None;
            switch (o.Definition.Kind)
            {
                case WorldObjectKind.Tree:
                case WorldObjectKind.Stump: return DesignationType.Chop;
                case WorldObjectKind.Stone:
                case WorldObjectKind.GoldStone: return DesignationType.Mine;
                case WorldObjectKind.Mushroom:
                case WorldObjectKind.Pumpkin:
                case WorldObjectKind.Bush: return DesignationType.Harvest;
                default: return DesignationType.None;
            }
        }

        /// <summary>The work stat behind a designation.</summary>
        public static StatType StatOf(DesignationType type) =>
            type == DesignationType.Chop ? StatType.Woodcutting : type == DesignationType.Mine ? StatType.Mining : StatType.Farming;

        public static string Verb(DesignationType type) =>
            type == DesignationType.Chop ? "chopping" : type == DesignationType.Mine ? "mining" : "harvesting";

        public static DesignationType Of(WorldObject o) => o != null && Marks.TryGetValue(o, out var t) ? t : DesignationType.None;

        public static bool IsMarked(WorldObject o) => Of(o) != DesignationType.None;

        public static void Mark(WorldObject o)
        {
            var type = For(o);
            if (type == DesignationType.None) return;
            Ensure();
            Marks[o] = type;
            ShowMarker(o, type);
        }

        public static void Unmark(WorldObject o)
        {
            if (o == null) return;
            Marks.Remove(o);
            if (Markers.TryGetValue(o, out var r))
            {
                if (r != null) Object.Destroy(r.gameObject);
                Markers.Remove(o);
            }
        }

        /// <summary>Marked objects of a kind that still exist.</summary>
        public static List<WorldObject> Marked(DesignationType type)
        {
            var list = new List<WorldObject>();
            Stale.Clear();
            foreach (var pair in Marks)
            {
                if (pair.Key == null || !(pair.Key is ResourceNode n) || !n.CanBeHit) { Stale.Add(pair.Key); continue; }
                if (pair.Value == type) list.Add(pair.Key);
            }
            foreach (var o in Stale) Unmark(o);
            return list;
        }

        static void ShowMarker(WorldObject o, DesignationType type)
        {
            if (!Application.isPlaying || o == null) return;
            if (!Markers.TryGetValue(o, out var r) || r == null)
            {
                var go = new GameObject("Designation Mark");
                go.transform.SetParent(o.transform, true);
                r = go.AddComponent<SpriteRenderer>();
                r.sortingOrder = short.MaxValue - 40;
                Markers[o] = r;
            }
            var c = CharacterSettings.Current;
            var sprite = type == DesignationType.Chop ? c.chopMark : type == DesignationType.Mine ? c.mineMark : c.harvestMark;
            r.sprite = sprite != null ? sprite : KingOfNothing.Utilities.ProceduralSprites.Dot;
            r.color = sprite != null ? Color.white : new Color(1f, 0.85f, 0.3f);
            var top = SpriteEffects.VisibleTopCenter(o);
            r.transform.position = new Vector3(top.x, top.y + 0.15f, 0f);
            var parentScale = o.transform.lossyScale;
            float s = c.markScale;
            r.transform.localScale = new Vector3(s / Mathf.Max(0.0001f, parentScale.x), s / Mathf.Max(0.0001f, parentScale.y), 1f);
        }

        // ------------------------------------------------------------------ saving

        public static List<int> SavedUids()
        {
            var list = new List<int>();
            foreach (var pair in Marks)
                if (pair.Key != null && pair.Key.Uid > 0) list.Add(pair.Key.Uid);
            return list;
        }

        public static void Load(List<int> uids)
        {
            foreach (var o in new List<WorldObject>(Marks.Keys)) Unmark(o);
            var map = MapData.Instance;
            if (uids == null || map == null) return;
            foreach (int uid in uids)
            {
                var o = map.FindObject(uid);
                if (o != null) Mark(o);
            }
        }
    }
}
