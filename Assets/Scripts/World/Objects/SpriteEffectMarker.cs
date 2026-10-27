using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Marks renderers that belong to an effect (outline, health bar) and not to the object itself, so fading,
    /// tinting and mouse picking ignore them.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteEffectMarker : MonoBehaviour
    {
    }
}
