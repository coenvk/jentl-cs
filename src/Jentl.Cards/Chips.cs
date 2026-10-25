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
}
