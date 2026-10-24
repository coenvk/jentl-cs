namespace Jentl.Collect.Tests;

public class WeightedTableTests
{
    [Fact]
    public void Draw_FollowsWeights()
    {
        var table = new WeightedTable<string>([("common", 9), ("rare", 1)]);
        var random = new Random(1);

        var rare = Enumerable.Range(0, 10_000).Count(_ => table.Draw(random) == "rare");

        rare.Should().BeInRange(850, 1150);
        table.Chance("rare").Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void Draw_SameSeedSameResults()
    {
        var table = new WeightedTable<int>([(1, 1), (2, 2), (3, 3)]);
        var first = new Random(7);
        var second = new Random(7);

        var a = Enumerable.Range(0, 20).Select(_ => table.Draw(first)).ToList();
        var b = Enumerable.Range(0, 20).Select(_ => table.Draw(second)).ToList();

        a.Should().Equal(b);
    }

    [Fact]
    public void Chance_OfMissingItem_IsZero() =>
        new WeightedTable<string>([("a", 1)]).Chance("b").Should().Be(0);

    [Fact]
    public void Constructor_RejectsEmptyAndNonPositiveWeights()
    {
        var empty = () => new WeightedTable<string>([]);
        var zero = () => new WeightedTable<string>([("a", 1), ("b", 0)]);

        empty.Should().Throw<ArgumentException>();
        zero.Should().Throw<ArgumentException>();
    }
}
