using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>Roles a villager can switch between (GDD 6.3; a smoke effect plays on change).</summary>
    public enum VillagerRole
    {
        Villager,
        Archer,
        Warrior,
        Priest,
        King,
        Lancer,
    }

    public static class VillagerRoles
    {
        /// <summary>Warrior, lancer and archer: they attack, they do not work.</summary>
        public static bool IsSoldier(VillagerRole role) => role == VillagerRole.Warrior || role == VillagerRole.Lancer || role == VillagerRole.Archer;

        /// <summary>Villagers (and the king) chop, mine, build and repair.</summary>
        public static bool IsWorker(VillagerRole role) => role == VillagerRole.Villager || role == VillagerRole.King;

        /// <summary>Weapon (attack animation) of a soldier class; Auto for the others.</summary>
        public static KingOfNothing.World.Objects.InteractionTool WeaponFor(VillagerRole role)
        {
            switch (role)
            {
                case VillagerRole.Warrior: return KingOfNothing.World.Objects.InteractionTool.Sword;
                case VillagerRole.Lancer: return KingOfNothing.World.Objects.InteractionTool.Spear;
                case VillagerRole.Archer: return KingOfNothing.World.Objects.InteractionTool.Bow;
                case VillagerRole.Priest: return KingOfNothing.World.Objects.InteractionTool.Heal;
                default: return KingOfNothing.World.Objects.InteractionTool.Auto;
            }
        }
    }

    /// <summary>Who the character is: what the character book shows.</summary>
    [Serializable]
    public class VillagerIdentity
    {
        public string displayName = "Villager";
        [Min(0)] public int age = 20;
        [Tooltip("Characters can die of old age around this age.")]
        [Min(1)] public int lifeExpectancy = 70;
        [TextArea(1, 3)] public string appearance;
        [TextArea(1, 3)] public string hobbies;
        [TextArea(2, 6)] public string story;
    }

    /// <summary>A bar from 0 to max (health, satiety, morale).</summary>
    [Serializable]
    public class NeedValue
    {
        [Min(1f)] public float max = 100f;
        public float current = 100f;
        [Tooltip("Change per second (negative = decays).")]
        public float changePerSecond;

        public float Normalized => max > 0f ? current / max : 0f;
        public bool IsEmpty => current <= 0f;

        public event Action<NeedValue> ValueChanged;

        public NeedValue() { }

        public NeedValue(float max, float changePerSecond)
        {
            this.max = max;
            current = max;
            this.changePerSecond = changePerSecond;
        }

        public void Change(float amount)
        {
            float old = current;
            current = Mathf.Clamp(current + amount, 0f, max);
            if (!Mathf.Approximately(old, current)) ValueChanged?.Invoke(this);
        }

        public void Tick(float deltaTime)
        {
            if (changePerSecond != 0f) Change(changePerSecond * deltaTime);
        }
    }

    /// <summary>GDD 6.4: hunger (meat), morale (house, church, trait events) and health (priest, church).</summary>
    [Serializable]
    public class VillagerNeeds
    {
        public NeedValue Health = new NeedValue(100f, 0f);
        [Tooltip("Satiety: 100 = full, 0 = starving.")]
        public NeedValue Satiety = new NeedValue(100f, -0.05f);
        public NeedValue Morale = new NeedValue(100f, 0f);

        public void Tick(float deltaTime)
        {
            Health.Tick(deltaTime);
            Satiety.Tick(deltaTime);
            Morale.Tick(deltaTime);
        }
    }

    /// <summary>A trait owned by a character and whether the player already knows it.</summary>
    [Serializable]
    public class TraitInstance
    {
        public TraitDefinition definition;
        public bool revealed;

        public TraitInstance() { }

        public TraitInstance(TraitDefinition definition, bool revealed)
        {
            this.definition = definition;
            this.revealed = revealed;
        }
    }
}
