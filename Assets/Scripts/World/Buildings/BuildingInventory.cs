using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>One kind of item in a building's inventory.</summary>
    [Serializable]
    public class InventorySlot
    {
        public SpawnableDefinition item;
        public int count;
    }

    /// <summary>
    /// Items stored in a building: a number of slots (one item kind each) with a capacity per slot (castle: 9 slots
    /// of 100). Only complete buildings accept items (checked by the building).
    /// </summary>
    [Serializable]
    public class BuildingInventory
    {
        [SerializeField] List<InventorySlot> slots = new List<InventorySlot>();
        int slotCount;
        int slotCapacity = 100;

        /// <summary>Finds item assets by name when an inventory is loaded (set by the map generator).</summary>
        public static Func<string, SpawnableDefinition> Resolver;

        public event Action Changed;

        public IReadOnlyList<InventorySlot> Slots => slots;
        public int SlotCount => slotCount;
        public int SlotCapacity => slotCapacity;
        public int UsedSlots => slots.Count;

        public void Configure(int count, int capacity)
        {
            slotCount = Mathf.Max(0, count);
            slotCapacity = Mathf.Max(1, capacity);
        }

        public int Count(SpawnableDefinition item)
        {
            if (item == null) return 0;
            foreach (var s in slots) if (s.item == item) return s.count;
            return 0;
        }

        /// <summary>How many of the item still fit (its slot, or a free slot).</summary>
        public int SpaceFor(SpawnableDefinition item)
        {
            if (item == null || slotCount == 0) return 0;
            foreach (var s in slots) if (s.item == item) return Mathf.Max(0, slotCapacity - s.count);
            return slots.Count < slotCount ? slotCapacity : 0;
        }

        /// <summary>Adds as many as fit. Returns how many were added.</summary>
        public int Add(SpawnableDefinition item, int amount)
        {
            int added = Mathf.Min(Mathf.Max(0, amount), SpaceFor(item));
            if (added <= 0) return 0;
            var slot = slots.Find(s => s.item == item);
            if (slot == null)
            {
                slot = new InventorySlot { item = item };
                slots.Add(slot);
            }
            slot.count += added;
            Changed?.Invoke();
            return added;
        }

        /// <summary>Takes up to the amount. Returns how many were taken.</summary>
        public int Take(SpawnableDefinition item, int amount)
        {
            var slot = slots.Find(s => s.item == item);
            if (slot == null || amount <= 0) return 0;
            int taken = Mathf.Min(amount, slot.count);
            slot.count -= taken;
            if (slot.count <= 0) slots.Remove(slot);
            if (taken > 0) Changed?.Invoke();
            return taken;
        }

        public void Clear()
        {
            if (slots.Count == 0) return;
            slots.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ saving (asset names + counts)

        public void Save(List<string> items, List<int> counts)
        {
            foreach (var s in slots)
            {
                if (s.item == null || s.count <= 0) continue;
                items.Add(s.item.name);
                counts.Add(s.count);
            }
        }

        public void Load(List<string> items, List<int> counts)
        {
            slots.Clear();
            if (items != null && counts != null)
                for (int i = 0; i < items.Count && i < counts.Count; i++)
                {
                    var item = Resolver != null ? Resolver(items[i]) : null;
                    if (item != null && counts[i] > 0) slots.Add(new InventorySlot { item = item, count = counts[i] });
                }
            Changed?.Invoke();
        }
    }
}
