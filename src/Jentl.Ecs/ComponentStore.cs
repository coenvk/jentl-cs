namespace Jentl.Ecs;

internal interface IComponentStore
{
    int Count { get; }

    ReadOnlySpan<int> Ids { get; }

    bool Has(int id);

    bool Remove(int id);
}

/// <summary>
/// One component type for every entity that has it, as a sparse set: the components sit packed in an array
/// (fast to run through), and a sparse index finds an entity's slot in constant time. Removing swaps the last
/// slot into the gap.
/// </summary>
internal sealed class ComponentStore<T> : IComponentStore
{
    private int[] _slots = new int[16];
    private int[] _ids = new int[16];
    private T[] _items = new T[16];

    public int Count { get; private set; }

    public ReadOnlySpan<int> Ids => _ids.AsSpan(0, Count);

    // Slots are stored plus one, so the array's default 0 means "absent".
    public bool Has(int id) => id >= 0 && id < _slots.Length && _slots[id] != 0;

    public ref T Get(int id)
    {
        if (!Has(id))
        {
            throw new KeyNotFoundException($"Entity {id} has no {typeof(T).Name}.");
        }

        return ref _items[_slots[id] - 1];
    }

    public void Set(int id, T value)
    {
        if (Has(id))
        {
            _items[_slots[id] - 1] = value;
            return;
        }

        if (id >= _slots.Length)
        {
            Array.Resize(ref _slots, Math.Max(id + 1, _slots.Length * 2));
        }

        if (Count == _items.Length)
        {
            Array.Resize(ref _items, Count * 2);
            Array.Resize(ref _ids, Count * 2);
        }

        _items[Count] = value;
        _ids[Count] = id;
        _slots[id] = ++Count;
    }

    public bool Remove(int id)
    {
        if (!Has(id))
        {
            return false;
        }

        var slot = _slots[id] - 1;
        var last = Count - 1;
        if (slot != last)
        {
            _items[slot] = _items[last];
            _ids[slot] = _ids[last];
            _slots[_ids[slot]] = slot + 1;
        }

        _items[last] = default!;
        _slots[id] = 0;
        Count--;
        return true;
    }
}
