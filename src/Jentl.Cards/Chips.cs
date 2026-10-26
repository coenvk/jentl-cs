namespace Jentl.Cards;

/// <summary>An amount of chips of one value, such as five chips worth 100.</summary>
public readonly record struct ChipCount(int Value, int Count);

/// <summary>One stack of identical chips: <paramref name="Count"/> chips of <paramref name="Value"/> each.</summary>
#pragma warning disable CA1711 // The name ChipStack is part of the chips API; it names a stack of chips.
public readonly record struct ChipStack(int Value, int Count);
#pragma warning restore CA1711

/// <summary>Chip helpers: breaking an amount into chips and arranging chips into stacks for the screen.</summary>
public static class Chips
{
    /// <summary>The chip values of a standard set, smallest first.</summary>
    public static IReadOnlyList<int> Denominations { get; } = [1, 5, 25, 100, 500, 1000, 5000, 25000];

    /// <summary>
    /// Breaks <paramref name="amount"/> into chips with the fewest chips, largest value first. Greedy, so it is
    /// optimal for the standard denominations; a custom set should be one where greedy works too.
    /// </summary>
    /// <param name="amount">The amount to break; never negative.</param>
    /// <param name="denominations">The chip values to use; the standard set when left out.</param>
    public static IReadOnlyList<ChipCount> Break(int amount, IReadOnlyList<int>? denominations = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var values = denominations ?? Denominations;
        if (values.Any(value => value <= 0))
        {
            throw new ArgumentException("Chip values must be positive.", nameof(denominations));
        }

        var chips = new List<ChipCount>();
        var remaining = amount;
        foreach (var value in values.OrderByDescending(value => value))
        {
            var count = remaining / value;
            if (count > 0)
            {
                chips.Add(new ChipCount(value, count));
                remaining -= count * value;
            }
        }

        return chips;
    }

    /// <summary>
    /// Splits chip counts into stacks of at most <paramref name="maxPerStack"/> chips each, largest values first.
    /// A count above the maximum becomes several full stacks and one smaller stack for the rest.
    /// </summary>
    public static IReadOnlyList<ChipStack> Stacks(IReadOnlyList<ChipCount> chips, int maxPerStack)
    {
        ArgumentNullException.ThrowIfNull(chips);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerStack, 1);

        var stacks = new List<ChipStack>();
        foreach (var chip in chips.OrderByDescending(chip => chip.Value))
        {
            var remaining = chip.Count;
            while (remaining > 0)
            {
                var count = Math.Min(remaining, maxPerStack);
                stacks.Add(new ChipStack(chip.Value, count));
                remaining -= count;
            }
        }

        return stacks;
    }

    /// <summary>
    /// The stacks <see cref="Stacks"/> makes of <see cref="Break"/>(<paramref name="amount"/>) with the standard
    /// denominations, worked out one at a time while they are walked with <c>foreach</c>, so nothing is allocated:
    /// for drawing an amount every frame.
    /// </summary>
    public static ChipStacks StacksOf(int amount, int maxPerStack)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerStack, 1);
        return new ChipStacks(amount, maxPerStack);
    }

    /// <summary>How many stacks <see cref="StacksOf"/> gives for <paramref name="amount"/>, without allocating.</summary>
    public static int StackCount(int amount, int maxPerStack)
    {
        var count = 0;
        foreach (var _ in StacksOf(amount, maxPerStack))
        {
            count++;
        }

        return count;
    }
}

/// <summary>The stacks of an amount with the standard denominations, largest first; see <see cref="Chips.StacksOf"/>.</summary>
#pragma warning disable CA1815 // Only ever walked with foreach; comparing two of these means nothing.
public readonly struct ChipStacks
{
    private readonly int _amount;
    private readonly int _maxPerStack;

    internal ChipStacks(int amount, int maxPerStack) => (_amount, _maxPerStack) = (amount, maxPerStack);

    public ChipStackEnumerator GetEnumerator() => new(_amount, _maxPerStack);
}

/// <summary>Walks <see cref="ChipStacks"/>: the largest denomination first, a full stack at a time.</summary>
public struct ChipStackEnumerator
{
    private readonly int _maxPerStack;
    private int _remaining;
    private int _denomination;
    private int _left;

    internal ChipStackEnumerator(int amount, int maxPerStack)
    {
        _remaining = amount;
        _maxPerStack = maxPerStack;
        _denomination = Chips.Denominations.Count;
    }

    public ChipStack Current { get; private set; }

    public bool MoveNext()
    {
        // _left counts the chips of the current denomination not yet stacked.
        while (_left == 0)
        {
            if (--_denomination < 0)
            {
                return false;
            }

            var value = Chips.Denominations[_denomination];
            _left = _remaining / value;
            _remaining -= _left * value;
        }

        var count = Math.Min(_left, _maxPerStack);
        _left -= count;
        Current = new ChipStack(Chips.Denominations[_denomination], count);
        return true;
    }
}
#pragma warning restore CA1815
