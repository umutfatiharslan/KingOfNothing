using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>What a map object is; decides which component the factory attaches.</summary>
    public enum WorldObjectKind
    {
        Decoration,
        Tree,
        Stump,
        Bush,
        Stone,
        GoldStone,
        GoldMine,
        Mushroom,
        Pumpkin,
        Bone,
        Skull,
        WaterRock,
        Signpost,
        /// <summary>A pile of dropped resources (wood, gold, stones...).</summary>
        Item,
        Scarecrow,
        Grass,
        /// <summary>Built by the player (castle, houses, tower, bridge, furnace...). See BuildingDefinition.</summary>
        Building,
    }

    /// <summary>Tool a pawn works an object with (decides the animation). Auto = chosen from the object kind.</summary>
    public enum InteractionTool
    {
        Auto,
        Axe,
        Pickaxe,
        Knife,
        Hammer,
        /// <summary>Warrior (Warrior_Attack1 / 2).</summary>
        Sword,
        /// <summary>Lancer (Lancer_*_Attack, one sheet per direction).</summary>
        Spear,
        /// <summary>Archer (Archer_Shoot).</summary>
        Bow,
        /// <summary>Monk (Heal).</summary>
        Heal,
    }

    public static class InteractionTools
    {
        /// <summary>Trees / stumps: axe; stones, gold, mines: pickaxe; mushrooms, bushes, grass: knife; the rest: hammer.</summary>
        public static InteractionTool ForKind(WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Tree:
                case WorldObjectKind.Stump:
                    return InteractionTool.Axe;
                case WorldObjectKind.Stone:
                case WorldObjectKind.GoldStone:
                case WorldObjectKind.GoldMine:
                case WorldObjectKind.WaterRock:
                    return InteractionTool.Pickaxe;
                case WorldObjectKind.Mushroom:
                case WorldObjectKind.Bush:
                case WorldObjectKind.Grass:
                    return InteractionTool.Knife;
                default:
                    return InteractionTool.Hammer;
            }
        }
    }

    /// <summary>Items of the economy (GDD chapter 4 and 10).</summary>
    public enum ResourceType
    {
        None,
        Wood,
        SmallStone,
        FlatStone,
        Gold,
        Meat,
        CookedMeat,
        Wheat,
        WheatSeed,
        Bread,
        Weapon,
        Pumpkin,
        Mushroom,
        Bone,
        /// <summary>Made by the furnace from gold and wood (Icon_03).</summary>
        GoldCoin,
    }

    /// <summary>Anything that can be hit by a worker, a soldier or the developer mouse.</summary>
    public interface IDamageable
    {
        bool CanBeHit { get; }

        /// <param name="power">Damage of the hit (usually the worker's stat value).</param>
        /// <param name="instigator">Who hit (null = developer mouse).</param>
        void ApplyHit(int power, GameObject instigator);
    }
}
