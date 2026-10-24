namespace Jentl.Collect;

/// <summary>
/// A saved count that refills one at a time on the wall clock: tickets, energy, lives. Keep the
/// <see cref="RechargeState"/> in the save file; the counter works out the count at any moment, also after the game
/// was closed for a while.
/// </summary>
/// <param name="Max">The most there can be; refilling stops here.</param>
/// <param name="Interval">How long one takes to come back.</param>
public sealed record RechargingCounter(int Max, TimeSpan Interval)
{
    /// <summary>The most there can be; at least one.</summary>
    public int Max { get; } = Max > 0 ? Max : throw new ArgumentOutOfRangeException(nameof(Max), "A counter needs room for at least one.");

    /// <summary>How long one takes to come back; more than zero.</summary>
    public TimeSpan Interval { get; } = Interval > TimeSpan.Zero ? Interval : throw new ArgumentOutOfRangeException(nameof(Interval), "A counter needs an interval longer than zero.");

    /// <summary>A full counter as of <paramref name="now"/>.</summary>
    public RechargeState Full(DateTimeOffset now) => new(Max, now);

    /// <summary>The count at <paramref name="now"/>. A clock set back before the state's time gives nothing back.</summary>
    public int Count(RechargeState state, DateTimeOffset now) => Current(state, now).Count;

    /// <summary>How long until the next one comes back, or null when full.</summary>
    public TimeSpan? NextIn(RechargeState state, DateTimeOffset now)
    {
        var current = Current(state, now);
        if (current.Count >= Max)
        {
            return null;
        }

        var elapsed = now > current.Since ? now - current.Since : TimeSpan.Zero;
        return Interval - elapsed;
    }

    /// <summary>Takes one if there is one; returns whether it did.</summary>
    public bool TrySpend(ref RechargeState state, DateTimeOffset now)
    {
        var current = Current(state, now);
        if (current.Count <= 0)
        {
            state = current;
            return false;
        }

        // From full the clock starts now; otherwise the progress towards the next one is kept.
        // A clock set back never moves the time back, or restoring it would hand out tickets.
        state = current.Count >= Max ? new RechargeState(Max - 1, now > current.Since ? now : current.Since) : current with { Count = current.Count - 1 };
        return true;
    }

    /// <summary>
    /// The state as of <paramref name="now"/>: whole elapsed intervals folded into the count, its time moved forward by
    /// those intervals only (never back), so it can be saved without losing progress towards the next one.
    /// </summary>
    public RechargeState Current(RechargeState state, DateTimeOffset now)
    {
        var count = Math.Clamp(state.Count, 0, Max);
        if (count >= Max || now <= state.Since)
        {
            return state with { Count = count };
        }

        var gained = (now - state.Since).Ticks / Interval.Ticks;
        if (count + gained >= Max)
        {
            return new RechargeState(Max, now);
        }

        return new RechargeState(count + (int)gained, state.Since + (Interval * gained));
    }
}

/// <summary>A <see cref="RechargingCounter"/>'s saved state.</summary>
/// <param name="Count">The count at <paramref name="Since"/>.</param>
/// <param name="Since">When the count was last exact; refilling counts from here.</param>
public readonly record struct RechargeState(int Count, DateTimeOffset Since);
