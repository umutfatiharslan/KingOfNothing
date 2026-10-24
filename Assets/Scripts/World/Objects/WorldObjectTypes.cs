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
