using System;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// What is left of a tree. Must be removed (it gives a little wood) before anything can be built there.
    /// A skilled woodcutter (Regrowth, Woodcutting 10+) or a priest (Holy Halo, Healing 10) can make it grow back:
    /// healing sparkles play around it and after a while the tree stands again.
    /// </summary>
    public class StumpObject : ResourceNode
    {
        [Tooltip("Tree that grows back from this stump.")]
        [SerializeField] SpawnableDefinition originalTree;

        [Serializable]
        class SavedState
        {
            public float regrowAt;
            public float bonusWood;
        }

        float regrowAt;
        float bonusWood;
        GameObject sparkles;

        public SpawnableDefinition OriginalTree => originalTree;
        /// <summary>Growing back into its tree.</summary>
        public bool IsRegrowing => regrowAt > 0f;
        /// <summary>Game hour when the tree is back.</summary>
        public float RegrowAt => regrowAt;

        public void SetOriginalTree(SpawnableDefinition tree) => originalTree = tree;

        public override SpawnableDefinition LinkedDefinition => originalTree;

        public override void SetLinkedDefinition(SpawnableDefinition linked) => originalTree = linked;

        public override string DisplayName => IsRegrowing ? base.DisplayName + " (growing back)" : base.DisplayName;

        /// <summary>Starts growing back: the tree is back after the given game hours (the new tree may give extra wood).</summary>
        public void BeginRegrow(float hours, float treeBonusWood)
        {
            if (originalTree == null) return;
            string before = SaveState();
            regrowAt = GameClock.Now + Mathf.Max(0.01f, hours);
            bonusWood = treeBonusWood;
            RaiseStateChanged(before);
            UpdateSparkles();
        }

        public override string SaveState() =>
            regrowAt > 0f ? JsonUtility.ToJson(new SavedState { regrowAt = regrowAt, bonusWood = bonusWood }) : null;

        public override void LoadState(string json)
        {
            regrowAt = 0f;
            bonusWood = 0f;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var state = JsonUtility.FromJson<SavedState>(json);
                    regrowAt = state.regrowAt;
                    bonusWood = state.bonusWood;
                }
                catch (Exception) { regrowAt = 0f; }
            }
            UpdateSparkles();
        }

        protected override void Start()
        {
            base.Start();
            UpdateSparkles();
        }

        void Update()
        {
            if (regrowAt <= 0f || GameClock.Instance == null) return;
            if (GameClock.Now >= regrowAt) RegrowTree();
        }

        void UpdateSparkles()
        {
            if (!Application.isPlaying) return;
            if (IsRegrowing && sparkles == null)
            {
                sparkles = new GameObject("Regrow Sparkles");
                sparkles.transform.SetParent(transform, false);
                sparkles.AddComponent<HealSparkles>().Setup(new Rect(GroundPoint.x - 0.5f, GroundPoint.y - 0.1f, 1f, 0.8f), new Color(0.45f, 1f, 0.5f));
            }
            else if (!IsRegrowing && sparkles != null)
            {
                Destroy(sparkles);
                sparkles = null;
            }
        }

        /// <summary>Replaces the stump with its tree again (Regrowth, Holy Halo...).</summary>
        public TreeResource RegrowTree()
        {
            if (originalTree == null) return null;
            var map = MapData.Instance;
            var origin = Cell;
            var parent = transform.parent;
            var tree = originalTree;
            float bonus = bonusWood;
            regrowAt = 0f;
            Remove();
            var created = WorldObjectFactory.Create(tree, origin, Vector2.zero, parent, map,
                configure: o => { if (bonus > 0f && o is TreeResource t) t.LoadState(JsonUtility.ToJson(new TreeState { bonusWood = bonus })); }) as TreeResource;
            return created;
        }

        [Serializable]
        class TreeState
        {
            public float bonusWood;
        }
    }
}
