using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// What is left of a tree. Must be removed (it gives a little wood) before anything can be built there.
    /// GDD: a priest or a skilled woodcutter can grow the tree back.
    /// </summary>
    public class StumpObject : ResourceNode
    {
        [Tooltip("Tree that grows back from this stump.")]
        [SerializeField] SpawnableDefinition originalTree;

        public SpawnableDefinition OriginalTree => originalTree;

        public void SetOriginalTree(SpawnableDefinition tree) => originalTree = tree;

        public override SpawnableDefinition LinkedDefinition => originalTree;

        public override void SetLinkedDefinition(SpawnableDefinition linked) => originalTree = linked;

        /// <summary>Replaces the stump with its tree again (priest ability, holy rain...).</summary>
        public TreeResource RegrowTree()
        {
            if (originalTree == null) return null;
            var map = MapData.Instance;
            var origin = Cell;
            var parent = transform.parent;
            var tree = originalTree;
            Remove();
            return WorldObjectFactory.Create(tree, origin, Vector2.zero, parent, map) as TreeResource;
        }
    }
}
