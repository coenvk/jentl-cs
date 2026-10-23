using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.Items;

/// <summary>
/// A kind of item, from the item catalogue. Items with a <see cref="Slot"/> can be equipped and add their
/// <see cref="Attack"/> and <see cref="Defense"/>; items with <see cref="Use"/> effects can be used, and are
/// used up if <see cref="Consumable"/>.
/// </summary>
public sealed record ItemDefinition(
    string Id,
    string Name,
    string Description,
    string Sprite,
    int MaxStack = 1,
    string? Slot = null,
    int Attack = 0,
    int Defense = 0,
    int Value = 0,
    IReadOnlyList<Effect>? Use = null,
    bool Consumable = true)
{
    public IReadOnlyList<Effect> Use { get; init; } = Use ?? [];

    public bool IsUsable => Use.Count > 0;

    public bool IsEquippable => Slot is not null;

    public override string ToString() => Id;
}

/// <summary>
/// Every item in the game, read from JSON:
/// <code>
/// { "items": [
///   { "id": "potion", "name": "Healing potion", "sprite": "potion", "stack": 5, "use": [{ "heal": 10 }] },
///   { "id": "sword", "name": "Short sword", "sprite": "sword", "slot": "weapon", "attack": 2 } ] }
/// </code>
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<string, ItemDefinition> _items = new(StringComparer.Ordinal);

    public ItemCatalog(IEnumerable<ItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (!_items.TryAdd(item.Id, item))
            {
                throw new ContentException($"The item '{item.Id}' is defined twice.");
            }
        }
    }

    public IEnumerable<ItemDefinition> All => _items.Values;

    public ItemDefinition this[string id] =>
        _items.TryGetValue(id, out var item) ? item : throw new ContentException($"There is no item '{id}'.");

    public bool Contains(string id) => _items.ContainsKey(id);

    public static ItemCatalog FromJson(string json)
    {
        using var document = Json.Parse(json, "items");
        return new ItemCatalog([.. document.RootElement.Array("items").Select(Read)]);
    }

    private static ItemDefinition Read(System.Text.Json.JsonElement e, int index)
    {
        var id = e.String("id", $"items[{index}]");
        var where = $"item '{id}'";
        var use = Effect.List(e, "use", where);
        return new ItemDefinition(
            id,
            e.OptionalString("name") ?? id,
            e.OptionalString("description") ?? "",
            e.OptionalString("sprite") ?? id,
            Math.Max(1, e.Int("stack", 1)),
            e.OptionalString("slot"),
            e.Int("attack", 0),
            e.Int("defense", 0),
            e.Int("value", 0),
            use,
            e.Bool("consumable", true));
    }
}

/// <summary>One inventory slot: an item and how many of it.</summary>
public sealed record ItemSlot(ItemDefinition Item, int Count)
{
    public override string ToString() => Count == 1 ? Item.Name : $"{Item.Name} x{Count}";
}

/// <summary>
/// A bag of a fixed number of slots. Stackable items fill existing stacks before taking a new slot;
/// <see cref="Add"/> says how many did not fit, and <see cref="Remove"/> takes all or nothing.
/// </summary>
public sealed class Inventory
{
    private readonly List<ItemSlot> _stacks = [];

    public Inventory(int capacity = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
    }

    public event Action? Changed;

    public int Capacity { get; }

    public IReadOnlyList<ItemSlot> Stacks => _stacks;

    public bool IsFull => _stacks.Count >= Capacity;

    public int Count(string id) => _stacks.Where(s => s.Item.Id == id).Sum(s => s.Count);

    public bool Has(string id, int count = 1) => Count(id) >= count;

    /// <summary>How many of <paramref name="item"/> would fit.</summary>
    public int Room(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var inStacks = _stacks.Where(s => s.Item == item).Sum(s => item.MaxStack - s.Count);
        return inStacks + (Capacity - _stacks.Count) * item.MaxStack;
    }

    /// <summary>Adds up to <paramref name="count"/> of <paramref name="item"/>; returns how many did not fit.</summary>
    public int Add(ItemDefinition item, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var left = count;
        for (var i = 0; i < _stacks.Count && left > 0; i++)
        {
            var stack = _stacks[i];
            if (stack.Item == item && stack.Count < item.MaxStack)
            {
                var added = Math.Min(left, item.MaxStack - stack.Count);
                _stacks[i] = stack with { Count = stack.Count + added };
                left -= added;
            }
        }

        while (left > 0 && _stacks.Count < Capacity)
        {
            var added = Math.Min(left, item.MaxStack);
            _stacks.Add(new ItemSlot(item, added));
            left -= added;
        }

        if (left != count)
        {
            Changed?.Invoke();
        }

        return left;
    }

    /// <summary>Removes <paramref name="count"/> of the item, from the last stacks first; false (and nothing removed) if there are fewer.</summary>
    public bool Remove(string id, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (Count(id) < count)
        {
            return false;
        }

        var left = count;
        for (var i = _stacks.Count - 1; i >= 0 && left > 0; i--)
        {
            var stack = _stacks[i];
            if (stack.Item.Id != id)
            {
                continue;
            }

            var taken = Math.Min(left, stack.Count);
            left -= taken;
            if (taken == stack.Count)
            {
                _stacks.RemoveAt(i);
            }
            else
            {
                _stacks[i] = stack with { Count = stack.Count - taken };
            }
        }

        if (count > 0)
        {
            Changed?.Invoke();
        }

        return true;
    }

    public void Clear()
    {
        _stacks.Clear();
        Changed?.Invoke();
    }
}

/// <summary>What is worn or wielded, one item per slot ("weapon", "armor", ...).</summary>
public sealed class Equipment
{
    private readonly Dictionary<string, ItemDefinition> _slots = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ItemDefinition> Slots => _slots;

    public ItemDefinition? this[string slot] => _slots.GetValueOrDefault(slot);

    public int Attack => _slots.Values.Sum(i => i.Attack);

    public int Defense => _slots.Values.Sum(i => i.Defense);

    public bool IsEquipped(string id) => _slots.Values.Any(i => i.Id == id);

    /// <summary>Puts <paramref name="item"/> in its slot; returns what was there before.</summary>
    public ItemDefinition? Equip(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var slot = item.Slot ?? throw new ArgumentException($"{item.Name} cannot be equipped.", nameof(item));
        var previous = _slots.GetValueOrDefault(slot);
        _slots[slot] = item;
        return previous;
    }

    public ItemDefinition? Unequip(string slot) => _slots.Remove(slot, out var item) ? item : null;

    public void Clear() => _slots.Clear();
}
