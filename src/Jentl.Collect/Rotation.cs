namespace Jentl.Collect;

/// <summary>
/// Time cut into equal periods from an epoch, for things that change on a calendar: the events of the week, a
/// daily challenge. Every device works out the same period from the same moment, without a server.
/// </summary>
/// <param name="Epoch">Where period 0 starts.</param>
/// <param name="Period">How long each period lasts.</param>
public sealed record Rotation(DateTimeOffset Epoch, TimeSpan Period)
{
    /// <summary>The period <paramref name="time"/> falls in (negative before the epoch).</summary>
    public long PeriodAt(DateTimeOffset time)
    {
        var ticks = (time - Epoch).Ticks;
        var period = ticks / Period.Ticks;
        return ticks < 0 && ticks % Period.Ticks != 0 ? period - 1 : period;
    }

    /// <summary>When <paramref name="period"/> starts.</summary>
    public DateTimeOffset StartOf(long period) => Epoch + TimeSpan.FromTicks(Period.Ticks * period);

    /// <summary>When <paramref name="period"/> ends (and the next starts).</summary>
    public DateTimeOffset EndOf(long period) => StartOf(period + 1);

    /// <summary>The item for the period at <paramref name="time"/>: items take turns, shifted by <paramref name="offset"/>.</summary>
    public T Pick<T>(IReadOnlyList<T> items, DateTimeOffset time, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        var index = (PeriodAt(time) + offset) % items.Count;
        return items[(int)(index < 0 ? index + items.Count : index)];
    }
}
