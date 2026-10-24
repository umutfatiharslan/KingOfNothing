namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Binary min-heap of (priority, item, tag) entries. Used by the Dijkstra-style flood fills of the
    /// map generator. Duplicate items are allowed ("lazy deletion"): callers skip stale entries.
    /// (System.Collections.Generic.PriorityQueue does not exist in Unity's .NET profile.)
    /// </summary>
    public sealed class MinHeap
    {
        float[] priorities;
        int[] items;
        int[] tags;

        public int Count { get; private set; }

        public MinHeap(int capacity = 1024)
        {
            if (capacity < 16) capacity = 16;
            priorities = new float[capacity];
            items = new int[capacity];
            tags = new int[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(float priority, int item, int tag = 0)
        {
            if (Count == priorities.Length) Grow();

            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (priorities[parent] <= priority) break;
                priorities[i] = priorities[parent];
                items[i] = items[parent];
                tags[i] = tags[parent];
                i = parent;
            }
            priorities[i] = priority;
            items[i] = item;
            tags[i] = tag;
        }

        public bool TryPop(out float priority, out int item, out int tag)
        {
            if (Count == 0)
            {
                priority = 0f;
                item = -1;
                tag = 0;
                return false;
            }

            priority = priorities[0];
            item = items[0];
            tag = tags[0];

            Count--;
            if (Count > 0)
            {
                float lastPriority = priorities[Count];
                int lastItem = items[Count];
                int lastTag = tags[Count];

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    if (left >= Count) break;
                    int right = left + 1;
                    int smallest = right < Count && priorities[right] < priorities[left] ? right : left;
                    if (priorities[smallest] >= lastPriority) break;
                    priorities[i] = priorities[smallest];
                    items[i] = items[smallest];
                    tags[i] = tags[smallest];
                    i = smallest;
                }
                priorities[i] = lastPriority;
                items[i] = lastItem;
                tags[i] = lastTag;
            }
            return true;
        }

        void Grow()
        {
            int size = priorities.Length * 2;
            System.Array.Resize(ref priorities, size);
            System.Array.Resize(ref items, size);
            System.Array.Resize(ref tags, size);
        }
    }
}
