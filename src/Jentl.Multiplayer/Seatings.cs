namespace Jentl.Multiplayer;

/// <summary>Ways to seat bots at a table so that luck of the seat evens out over several runs.</summary>
public static class Seatings
{
    /// <summary>
    /// One seating per run so that every bot sits in every seat once: run <c>r</c> puts bot <c>(seat + r) % seats</c> in
    /// each seat. Element <c>r</c> is the seating of run <c>r</c>, with <c>seating[seat] = bot</c>; for two seats that is
    /// <c>[[0, 1], [1, 0]]</c>.
    /// </summary>
    public static IReadOnlyList<int[]> Duplicate(int seats)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 1);
        return [.. Enumerable.Range(0, seats).Select(run => Enumerable.Range(0, seats).Select(seat => (seat + run) % seats).ToArray())];
    }
}
