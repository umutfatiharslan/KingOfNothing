using System;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// A tree. When cut down it leaves its stump (tree1 -> stump1), which still blocks building. A tree that grew
    /// back thanks to a Forest Keeper (Woodcutting 20) gives more wood when it is chopped the first time.
    /// </summary>
    public class TreeResource : ResourceNode
    {
        /// <summary>(tree, its stump or null, who felled it) right after a tree fell.</summary>
        public static event Action<TreeResource, StumpObject, GameObject> Felled;

        [Serializable]
        class SavedState
        {
            public float bonusWood;
        }

        float bonusWood;

        /// <summary>Extra wood share of the first chop (0 = none).</summary>
        public float BonusWood => bonusWood;

        public void SetBonusWood(float share)
        {
            string before = SaveState();
            bonusWood = Mathf.Max(0f, share);
            RaiseStateChanged(before);
        }

        public override string SaveState() => bonusWood > 0f ? JsonUtility.ToJson(new SavedState { bonusWood = bonusWood }) : null;

        public override void LoadState(string json)
        {
            bonusWood = 0f;
            if (string.IsNullOrEmpty(json)) return;
            try { bonusWood = JsonUtility.FromJson<SavedState>(json).bonusWood; }
            catch (Exception) { bonusWood = 0f; }
        }

        protected override void OnDepleted(GameObject instigator)
        {
            var stumpDefinition = Definition != null ? Definition.Stump : null;
            var map = MapData.Instance;
            var origin = Cell;
            var parent = transform.parent;
            var tree = Definition;
            Remove();

            // The stump knows its tree before it is announced, so the change history records the link
            StumpObject stump = null;
            if (stumpDefinition != null)
                stump = WorldObjectFactory.Create(stumpDefinition, origin, Vector2.zero, parent, map,
                    configure: created => created.SetLinkedDefinition(tree)) as StumpObject;
            Felled?.Invoke(this, stump, instigator);
        }
    }
}
