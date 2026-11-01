using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>What kind of thing a target is (secondary hits only jump to targets of the same kind).</summary>
    public enum TargetKind
    {
        Object,
        Building,
        Animal,
        Character,
    }

    /// <summary>One hit of a weapon or a tool on a target.</summary>
    public struct HitInfo
    {
        public int Damage;
        /// <summary>Who hit (null = developer mouse / the world).</summary>
        public Unit Attacker;
        public InteractionTool Weapon;
        public bool Ranged;
        /// <summary>Seconds the target cannot move or act (War Hammer).</summary>
        public float StunSeconds;
        /// <summary>The target bleeds (axe fights).</summary>
        public bool Bleed;
        /// <summary>The hit breaks some of the target's defense (Pickaxe Blow).</summary>
        public bool ArmorBreak;
        /// <summary>Training on a scarecrow: no damage at all.</summary>
        public bool Training;

        public static HitInfo Simple(int damage, Unit attacker, InteractionTool weapon, bool ranged = false) =>
            new HitInfo { Damage = damage, Attacker = attacker, Weapon = weapon, Ranged = ranged };
    }

    /// <summary>Anything soldiers (and fighting villagers) can attack: map objects, buildings, animals, characters.</summary>
    public interface ICombatTarget
    {
        bool IsAlive { get; }
        string DisplayName { get; }
        /// <summary>Solid area of the target (where weapons hit).</summary>
        Rect Hitbox { get; }
        /// <summary>Where the target stands.</summary>
        Vector2 GroundPoint { get; }
        TargetKind TargetKind { get; }
        /// <summary>True for things that move (they are chased, not approached once).</summary>
        bool Moves { get; }
        GameObject gameObject { get; }
        /// <summary>Applies the hit. Returns the damage really done.</summary>
        int ReceiveHit(HitInfo hit);
    }

    /// <summary>Helpers for combat targets.</summary>
    public static class CombatTargets
    {
        public static bool Valid(ICombatTarget t) => t != null && (t as Object) != null && t.IsAlive;

        /// <summary>Targets of the same kind around a point (secondary hits: cleave, impale, whirlwind, piercing arrows).</summary>
        public static void Around(Vector2 point, float radius, TargetKind kind, ICombatTarget except, Unit attacker, List<ICombatTarget> result)
        {
            result.Clear();
            float r2 = radius * radius;
            switch (kind)
            {
                case TargetKind.Animal:
                    foreach (var s in KingOfNothing.Animals.Sheep.All)
                        if (s != null && s.IsAlive && !ReferenceEquals(s, except) && (s.GroundPoint - point).sqrMagnitude <= r2) result.Add(s);
                    break;
                case TargetKind.Character:
                    foreach (var v in KingOfNothing.Characters.Villager.All)
                        if (v != null && v.IsAlive && !ReferenceEquals(v, except) && (attacker == null || v.Movement != attacker) &&
                            (v.GroundPoint - point).sqrMagnitude <= r2) result.Add(v);
                    break;
                case TargetKind.Building:
                    foreach (var b in KingOfNothing.World.Buildings.Building.All)
                        if (b != null && b.CanBeHit && !ReferenceEquals(b, except) && (b.GroundPoint - point).sqrMagnitude <= r2) result.Add(b);
                    break;
            }
        }
    }
}
