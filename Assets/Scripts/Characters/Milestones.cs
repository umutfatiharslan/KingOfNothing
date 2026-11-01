using System.Collections.Generic;

namespace KingOfNothing.Characters
{
    /// <summary>A milestone ability of a stat (unlocked at 5, 10, 15 and 20).</summary>
    public sealed class Milestone
    {
        public readonly StatType Stat;
        public readonly int Level;
        public readonly string Name;
        public readonly string Description;
        /// <summary>False while the system it needs is missing (fields, fishing, sickness, death).</summary>
        public readonly bool Available;

        public Milestone(StatType stat, int level, string name, string description, bool available = true)
        {
            Stat = stat;
            Level = level;
            Name = name;
            Description = description;
            Available = available;
        }
    }

    /// <summary>Names and descriptions of every milestone (stat table v3; numbers are in StatSettings).</summary>
    public static class Milestones
    {
        static Dictionary<StatType, Milestone[]> table;

        public static IReadOnlyList<Milestone> Of(StatType stat)
        {
            Build();
            return table.TryGetValue(stat, out var list) ? list : new Milestone[0];
        }

        /// <summary>The milestone of this level (5, 10, 15, 20) or null.</summary>
        public static Milestone At(StatType stat, int level)
        {
            foreach (var m in Of(stat)) if (m.Level == level) return m;
            return null;
        }

        static void Add(StatType stat, params Milestone[] list) => table[stat] = list;

        static Milestone M(StatType s, int l, string n, string d, bool available = true) => new Milestone(s, l, n, d, available);

        static void Build()
        {
            if (table != null) return;
            table = new Dictionary<StatType, Milestone[]>();
            var b = StatType.Building;
            Add(b,
                M(b, 5, "Sturdy Foundation", "A finished building gets +100 x level extra health (only the best builder of the site counts)."),
                M(b, 10, "Master Hand", "Builds twice as fast; 20% chance of a critical swing that counts twice."),
                M(b, 15, "Foreman", "60% critical chance; the other builders of the site work 25% faster; a repaired ruin comes back with 10% health."),
                M(b, 20, "War Hammer", "Fights with the hammer (damage = building); x2 against buildings and bridges; 75% salvage when tearing down; stuns living targets for 1.5 s (every 10 s)."));
            var w = StatType.Woodcutting;
            Add(w,
                M(w, 5, "Wood Pile", "30% chance of +1 wood when a tree falls; carries 5 more wood."),
                M(w, 10, "Regrowth", "40% chance that the stump grows back into a tree (healing sparkles around it)."),
                M(w, 15, "Fallen Giant", "60% regrowth; the falling tree hits a tree within 2 cells."),
                M(w, 20, "Forest Keeper", "80% regrowth, regrown trees grow twice as fast and give +50% wood; 5 trees a day = a sapling at night; fights with the axe (bleeding 10 / hour, 50 in total)."));
            var mi = StatType.Mining;
            Add(mi,
                M(mi, 5, "Ruin Digger", "Can dig the ruins of a destroyed building: 25% of its cost as loot, the ruin is gone."),
                M(mi, 10, "Mine Chief", "While inside, the gold mine takes one more miner."),
                M(mi, 15, "Reserve Keeper", "25% of the gold this miner brings out does not lower the mine's reserve."),
                M(mi, 20, "Pickaxe Blow", "Fights with the pickaxe; every hit breaks 1 defense point (max 10, back after an hour); buildings take +1% per hit."));
            var f = StatType.Farming;
            Add(f,
                M(f, 5, "Harvest Calendar", "Needs fields (later).", false),
                M(f, 10, "Soil Feeder", "Needs fields (later).", false),
                M(f, 15, "Rich Harvest", "Needs fields (later).", false),
                M(f, 20, "Mother Earth", "Needs fields (later).", false));
            var h = StatType.Hunting;
            Add(h,
                M(h, 5, "Shepherd", "Can herd sheep to a chosen spot."),
                M(h, 10, "Curious Prey", "Sheep fleeing from this hunter stop after 3 s and look back."),
                M(h, 15, "Hunting Party", "Others within 2 cells deal +50% damage to this hunter's prey."),
                M(h, 20, "Knife Master", "Fights with the knife; every attack is a quick double strike (x2 damage)."));
            var fi = StatType.Fishing;
            Add(fi,
                M(fi, 5, "Patient Rod", "Needs fishing (later).", false),
                M(fi, 10, "Bridge Fisher", "Needs fishing (later).", false),
                M(fi, 15, "Precious Catch", "Needs fishing (later).", false),
                M(fi, 20, "Net Setter", "Needs fishing (later).", false));
            var bk = StatType.Baking;
            Add(bk,
                M(bk, 5, "Fire Keeper", "The furnace burns one wood less per cycle."),
                M(bk, 10, "Perfect Cast", "20% chance of +1 gold coin when melting gold."),
                M(bk, 15, "Feast Meal", "Cooks feast meat: the eater gets +5 morale and +10% work speed for an hour."),
                M(bk, 20, "Hearth Master", "Up to 3 master meals a day: the eater gets experience in a random stat and learns it twice as fast for a day."));
            var e = StatType.Efficiency;
            Add(e,
                M(e, 5, "Full Arms", "Carries 5 more while supplying."),
                M(e, 10, "Thrifty Master", "Templates this character supplies need 10% less."),
                M(e, 15, "Recycling", "Tearing a site down drops 80% of its materials."),
                M(e, 20, "Master's Secret", "Every 5th template supplied gets its most expensive material for free."));
            var a = StatType.Archery;
            Add(a,
                M(a, 5, "Wind Reader", "No friendly fire."),
                M(a, 10, "Double Arrow", "25% chance to shoot two arrows."),
                M(a, 15, "Piercing Shot", "An arrow that kills goes on to a target behind with 50% damage."),
                M(a, 20, "Eagle Eye", "+5 range on a tower; the first shot at every new target deals x3."));
            var sw = StatType.Sword;
            Add(sw,
                M(sw, 5, "Shield Stance", "30% chance to block a hit (guard)."),
                M(sw, 10, "Combo", "Every second hit on the same target +50%."),
                M(sw, 15, "Cleave", "Hits also a second enemy next to the target for 50%."),
                M(sw, 20, "Last Stand", "Below 25% health deals x2 damage; once a day survives a deadly hit with 1 health."));
            var sp = StatType.Spear;
            Add(sp,
                M(sp, 5, "Distance Keeper", "Always strikes first: the first thrust at a new target comes at once."),
                M(sp, 10, "Spear Wall", "While standing, allies next to the lancer take 10% less damage."),
                M(sp, 15, "Impale", "A thrust also hits the next target in line within 2 cells."),
                M(sp, 20, "Whirlwind", "Every 12 s hits every enemy within 2 cells."));
            var d = StatType.Defense;
            Add(d,
                M(d, 5, "Thick Skin", "The first hit of the day does 25% less."),
                M(d, 10, "Cover", "Allies within 2 cells take 10% less damage (does not stack)."),
                M(d, 15, "Push Back", "20% chance to push the attacker back a cell; the first hit of the day does 50% less."),
                M(d, 20, "Unshakable", "No status effect: no stun, bleeding, broken defense or push back."));
            var he = StatType.Health;
            Add(he,
                M(he, 5, "Second Wind", "Below 30% health heals 10% in 5 s (once in the whole game)."),
                M(he, 10, "Fresh Morning", "Every new day heals 5% of the maximum health."),
                M(he, 15, "Iron Body", "The first hit of the day gives a shield of 15% health for 30 s."),
                M(he, 20, "Immortal Will", "Every 5 days a deadly hit leaves 1 health."));
            var hl = StatType.Healing;
            Add(hl,
                M(hl, 5, "Prayer", "Heals the target again over 5 s."),
                M(hl, 10, "Holy Halo", "Heals everybody in the radius; can make stumps grow back (100%)."),
                M(hl, 15, "Purification", "Healing removes status effects (sickness comes later)."),
                M(hl, 20, "Miracle", "Needs death (later).", false));
            var wa = StatType.WalkSpeed;
            Add(wa,
                M(wa, 5, "Nimble", "+50% speed for 2 s after a new order."),
                M(wa, 10, "Messenger", "+40% speed below 30% health."),
                M(wa, 15, "Escape", "Chance to dodge a hit: arrow 15%, sword 10%, spear 8%, tools 10%, collapse 30%."),
                M(wa, 20, "Pathfinder", "A group ordered together walks at this character's speed (+10%), following it."));
            var hu = StatType.Hunger;
            Add(hu,
                M(hu, 5, "Night Fullness", "Gets hungry half as fast inside a building."),
                M(hu, 10, "Iron Stomach", "Can eat raw meat (25% satiety)."),
                M(hu, 15, "Hungry Wolf", "Below 25 satiety work and attacks are 15% stronger."),
                M(hu, 20, "Fine Dine", "Can eat when full: +5 maximum health per extra meal, up to 200."));
        }
    }
}
