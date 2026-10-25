namespace Jentl.Collect.Tests;

public class RunningStatsTests
{
    [Fact]
    public void Stats_GiveMeanAndMargin()
    {
        var s = new RunningStats();
        foreach (var v in new[] { 2.0, 4, 4, 4, 5, 5, 7, 9 })
        {
            s.Add(v);
        }

        (s.Count, s.Mean).Should().Be((8, 5.0));
        s.StandardDeviation.Should().BeApproximately(2.138, 0.001);
        s.Margin95.Should().BeApproximately(1.96 * 2.138 / Math.Sqrt(8), 0.001);
    }

    [Fact]
    public void BelowTwoValues_ThereIsNoSpread()
    {
        var s = new RunningStats();
        s.Add(3);

        s.StandardDeviation.Should().Be(0);
        s.Margin95.Should().Be(0);
    }
}
