namespace Jentl.Collect.Tests;

public class RechargingCounterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly RechargingCounter Tickets = new(5, TimeSpan.FromMinutes(30));

    [Fact]
    public void Spend_ThenRecharge_OnePerInterval()
    {
        var s = Tickets.Full(T0);
        for (var i = 0; i < 5; i++)
        {
            Tickets.TrySpend(ref s, T0).Should().BeTrue();
        }

        Tickets.TrySpend(ref s, T0).Should().BeFalse();
        Tickets.Count(s, T0.AddMinutes(29)).Should().Be(0);
        Tickets.Count(s, T0.AddMinutes(30)).Should().Be(1);
        Tickets.NextIn(s, T0.AddMinutes(45)).Should().Be(TimeSpan.FromMinutes(15));
        Tickets.Count(s, T0.AddDays(30)).Should().Be(5);
        Tickets.NextIn(s, T0.AddDays(30)).Should().BeNull();
    }

    [Fact]
    public void SpendWhilePartlyRecharged_KeepsProgressTowardsNext()
    {
        // Empty since T0: at +45 minutes one ticket is back and the next is 15 minutes away; spending it keeps that.
        var s = new RechargeState(0, T0);

        Tickets.TrySpend(ref s, T0.AddMinutes(45)).Should().BeTrue();

        Tickets.Count(s, T0.AddMinutes(45)).Should().Be(0);
        Tickets.NextIn(s, T0.AddMinutes(45)).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void SpendFromFull_StartsTheClockAtSpending()
    {
        var s = Tickets.Full(T0);

        Tickets.TrySpend(ref s, T0.AddHours(3)).Should().BeTrue();

        Tickets.Count(s, T0.AddHours(3)).Should().Be(4);
        Tickets.NextIn(s, T0.AddHours(3)).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ClockSetBack_GrantsNothing_AndNeverNegative()
    {
        var s = new RechargeState(2, T0);

        Tickets.Count(s, T0.AddHours(-5)).Should().Be(2);
        Tickets.NextIn(s, T0.AddHours(-5)).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Count_NeverExceedsMax_EvenFromABadState() =>
        Tickets.Count(new RechargeState(9, T0), T0).Should().Be(5);
}
