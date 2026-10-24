using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>A tree. When cut down it leaves its stump (tree1 -> stump1), which still blocks building.</summary>
    public class TreeResource : ResourceNode
    {
        protected override void OnDepleted(GameObject instigator)
        {
            var stumpDefinition = Definition != null ? Definition.Stump : null;
            var map = MapData.Instance;
            var origin = Cell;
            var parent = transform.parent;
            Remove();

            if (stumpDefinition != null)
            {
                var stump = WorldObjectFactory.Create(stumpDefinition, origin, Vector2.zero, parent, map) as StumpObject;
                if (stump != null) stump.SetOriginalTree(Definition);
            }
        }
    }
}
