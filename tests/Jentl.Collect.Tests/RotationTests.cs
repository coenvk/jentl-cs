namespace Jentl.Collect.Tests;

public class RotationTests
{
    private static readonly Rotation Every3Days = new(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(3));

    [Fact]
    public void Rotation_PicksByPeriod()
    {
        var t = new DateTimeOffset(2026, 1, 11, 23, 59, 0, TimeSpan.Zero);

        Every3Days.PeriodAt(t).Should().Be(2);
        Every3Days.StartOf(2).Should().Be(new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero));
        Every3Days.EndOf(2).Should().Be(new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero));
        Every3Days.Pick(["a", "b", "c"], t, offset: 2).Should().Be("b");
    }

    [Fact]
    public void PeriodAt_BeforeTheEpoch_Floors()
    {
        Every3Days.PeriodAt(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)).Should().Be(-1);
        Every3Days.Pick(["a", "b", "c"], new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)).Should().Be("c");
    }

    [Fact]
    public void PeriodAt_IgnoresTheOffsetOfTheTime() =>
        Every3Days.PeriodAt(new DateTimeOffset(2026, 1, 8, 1, 0, 0, TimeSpan.FromHours(2))).Should().Be(0);
}
