namespace Jentl.Collect.Tests;

public class Glicko2Tests
{
    [Fact]
    public void GlickmansExample_GivesHisNumbers()
    {
        var player = new GlickoRating(1500, 200, 0.06);
        var after = Glicko2.Update(player, [new(new(1400, 30, 0.06), 1), new(new(1550, 100, 0.06), 0), new(new(1700, 300, 0.06), 0)]);

        after.Rating.Should().BeApproximately(1464.06, 0.01);
        after.Deviation.Should().BeApproximately(151.52, 0.01);
        after.Volatility.Should().BeApproximately(0.05999, 0.00001);
    }

    [Fact]
    public void NoGames_OnlyWidensTheDeviation()
    {
        var after = Glicko2.Update(new GlickoRating(1600, 50, 0.06), []);

        after.Rating.Should().Be(1600);
        after.Deviation.Should().BeGreaterThan(50);
    }

    [Fact]
    public void TheDeviation_NeverPassesItsStart() =>
        Glicko2.Update(GlickoRating.New, []).Deviation.Should().Be(350);

    [Fact]
    public void AWin_RaisesTheRating_AndNarrowsTheDeviation()
    {
        var after = Glicko2.Update(GlickoRating.New, [new(GlickoRating.New, 1)]);

        after.Rating.Should().BeGreaterThan(1500);
        after.Deviation.Should().BeLessThan(350);
    }
}
