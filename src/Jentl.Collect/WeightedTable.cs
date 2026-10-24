namespace Jentl.Collect;

/// <summary>
/// Items drawn at random in proportion to their weights: loot, card packs, rarities. The same <see cref="Random"/>
/// seed draws the same items.
/// </summary>
/// <typeparam name="T">What is drawn.</typeparam>
public sealed class WeightedTable<T>
{
    private readonly (T Item, double Weight)[] _entries;
    private readonly double _total;

    /// <param name="entries">The items and their weights; at least one, every weight above zero.</param>
    public WeightedTable(IEnumerable<(T Item, double Weight)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = [.. entries];
        if (_entries.Length == 0)
        {
            throw new ArgumentException("A weighted table needs at least one entry.", nameof(entries));
        }

        if (_entries.Any(e => !(e.Weight > 0)))
        {
            throw new ArgumentException("Every weight must be above zero.", nameof(entries));
        }

        _total = _entries.Sum(e => e.Weight);
    }

    /// <summary>Draws one item.</summary>
    public T Draw(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
#pragma warning disable CA5394 // Game randomness, not security relevant.
        var roll = random.NextDouble() * _total;
#pragma warning restore CA5394
        foreach (var (item, weight) in _entries)
        {
            roll -= weight;
            if (roll < 0)
            {
                return item;
            }
        }

        return _entries[^1].Item;
    }

    /// <summary>The chance (0 to 1) that a draw gives <paramref name="item"/>.</summary>
    public double Chance(T item) =>
        _entries.Where(e => EqualityComparer<T>.Default.Equals(e.Item, item)).Sum(e => e.Weight) / _total;
}
