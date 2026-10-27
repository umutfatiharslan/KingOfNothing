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
            var tree = Definition;
            Remove();

            // The stump knows its tree before it is announced, so the change history records the link
            if (stumpDefinition != null)
                WorldObjectFactory.Create(stumpDefinition, origin, Vector2.zero, parent, map,
                    configure: created => created.SetLinkedDefinition(tree));
        }
    }
}
