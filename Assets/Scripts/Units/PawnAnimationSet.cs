using System;
using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// All animations of one pawn colour (Tiny Swords "Pawn_*" sheets): idle / run, idle / run with each tool,
    /// the interact (work) animation of each tool, and idle / run while carrying wood, gold or meat.
    /// The default asset builder fills it from the Blue Pawn sheets; another colour can be assigned per prefab.
    /// </summary>
    [CreateAssetMenu(fileName = "PawnAnimations", menuName = "King of Nothing/Characters/Pawn Animation Set")]
    public class PawnAnimationSet : ScriptableObject
    {
        [Serializable]
        public class ToolAnimations
        {
            public InteractionTool tool;
            public Sprite[] idle = new Sprite[0];
            public Sprite[] run = new Sprite[0];
            [Tooltip("One swing (Pawn_Interact <tool>); played once per hit. For weapons: the attack towards the side.")]
            public Sprite[] interact = new Sprite[0];
            [Tooltip("Optional second swing, used every other hit (Warrior_Attack2). Empty = always the first one.")]
            public Sprite[] interactAlternate = new Sprite[0];
            [Tooltip("Optional swings by direction (Lancer). Empty = the side swing is used.")]
            public Sprite[] interactUp = new Sprite[0];
            public Sprite[] interactUpSide = new Sprite[0];
            public Sprite[] interactDown = new Sprite[0];
            public Sprite[] interactDownSide = new Sprite[0];

            /// <summary>Swing frames for a direction (x sign is handled by flipping the sprite).</summary>
            public Sprite[] InteractFor(Vector2 direction, bool alternate)
            {
                if (direction.sqrMagnitude > 0.0001f)
                {
                    float angle = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg; // -90 (down) .. 90 (up)
                    Sprite[] directional = null;
                    if (angle > 67.5f) directional = interactUp;
                    else if (angle > 22.5f) directional = interactUpSide;
                    else if (angle < -67.5f) directional = interactDown;
                    else if (angle < -22.5f) directional = interactDownSide;
                    if (directional != null && directional.Length > 0) return directional;
                }
                if (alternate && interactAlternate != null && interactAlternate.Length > 0) return interactAlternate;
                return interact;
            }
        }

        [Header("Basic")]
        public Sprite[] idle = new Sprite[0];
        public Sprite[] run = new Sprite[0];
        [Min(0.1f)] public float framesPerSecond = 10f;

        [Header("Tools (walking to / working on an object)")]
        public List<ToolAnimations> tools = new List<ToolAnimations>();

        [Header("Carrying")]
        public Sprite[] idleWood = new Sprite[0];
        public Sprite[] runWood = new Sprite[0];
        public Sprite[] idleGold = new Sprite[0];
        public Sprite[] runGold = new Sprite[0];
        public Sprite[] idleMeat = new Sprite[0];
        public Sprite[] runMeat = new Sprite[0];

        public ToolAnimations ForTool(InteractionTool tool)
        {
            if (tool == InteractionTool.Auto) return null;
            foreach (var t in tools)
                if (t != null && t.tool == tool) return t;
            return null;
        }

        /// <summary>Idle / run frames while carrying a resource (null if the pawn has no sprite for it).</summary>
        public Sprite[] Carrying(ResourceType resource, bool moving)
        {
            Sprite[] frames;
            switch (resource)
            {
                case ResourceType.Wood: frames = moving ? runWood : idleWood; break;
                case ResourceType.Gold: frames = moving ? runGold : idleGold; break;
                case ResourceType.Meat:
                case ResourceType.CookedMeat: frames = moving ? runMeat : idleMeat; break;
                default: return null;
            }
            return frames != null && frames.Length > 0 ? frames : null;
        }

#if UNITY_EDITOR
        public ToolAnimations EditorSetTool(InteractionTool tool, Sprite[] idleFrames, Sprite[] runFrames, Sprite[] interactFrames)
        {
            var entry = ForTool(tool);
            if (entry == null)
            {
                entry = new ToolAnimations { tool = tool };
                tools.Add(entry);
            }
            entry.idle = idleFrames;
            entry.run = runFrames;
            entry.interact = interactFrames;
            return entry;
        }
#endif
    }
}
