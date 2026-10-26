using Jentl.Cards;
using Jentl.Core.Cards;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class ChipTests
{
    [Fact]
    public void EveryDenomination_HasItsOwnColour() =>
        Chips.Denominations.Select(ChipRenderer.ColorOf).Distinct().Should().HaveCount(Chips.Denominations.Count);

    [Fact]
    public void AFlight_StartsAtItsOrigin_EndsAtItsTarget()
    {
        var flight = new ChipFlight(50, new Vector2(0, 0), new Vector2(100, 40), TimeSpan.FromSeconds(0.4));
        flight.Position.Should().Be(new Vector2(0, 0));
        flight.Update(TimeSpan.FromSeconds(0.2));
        flight.Position.X.Should().BeInRange(1, 99);
        flight.Update(TimeSpan.FromSeconds(0.3));
        (flight.IsDone, flight.Position).Should().Be((true, new Vector2(100, 40)));
    }

    [Fact]
    public void Width_GrowsWithTheNumberOfStacks() =>
        ChipRenderer.Width(1_637, 10).Should().BeGreaterThan(ChipRenderer.Width(1_000, 10));

    [Fact]
    public void Width_AllocatesNothing()
    {
        ChipRenderer.Width(98_765, 20);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var width = ChipRenderer.Width(98_765, 20);
        GC.GetAllocatedBytesForCurrentThread().Should().Be(before);
        width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AStack_GrowsByOneChipsEdgePerChip()
    {
        var one = ChipRenderer.StackHeight(20, 1);
        var two = ChipRenderer.StackHeight(20, 2);
        one.Should().BeGreaterThan(two - one, "the top chip shows its face as well as its edge");
        (ChipRenderer.StackHeight(20, 10) - ChipRenderer.StackHeight(20, 9)).Should().BeApproximately(two - one, 0.001f);
        ChipRenderer.StackHeight(20, 0).Should().Be(0);
    }

    [Fact]
    public void TheEdgeStripes_StayOnTheChip_AndThinOutTowardsItsSides()
    {
        for (var chip = 0; chip < 5; chip++)
        {
            var stripes = Enumerable.Range(0, ChipRenderer.EdgeStripes)
                .Select(s => ChipRenderer.EdgeStripe(s, chip, 40))
                .OfType<(float Left, float Right)>()
                .ToList();

            stripes.Should().NotBeEmpty();
            stripes.Should().OnlyContain(s => s.Left >= -20 && s.Right <= 20 && s.Left < s.Right);

            // Only the front half shows, and the stripes leave most of the edge in the chip's colour.
            var ordered = stripes.OrderBy(s => s.Left).ToList();
            ordered.Zip(ordered.Skip(1)).Should().OnlyContain(pair => pair.First.Right <= pair.Second.Left);
            stripes.Sum(s => s.Right - s.Left).Should().BeLessThan(40 * 0.5f);

            // Seen from the front the stripe in the middle is the widest: the edge turns away towards the sides.
            var middle = stripes.MinBy(s => MathF.Abs(s.Left + s.Right));
            var side = stripes.MaxBy(s => MathF.Abs(s.Left + s.Right));
            (middle.Right - middle.Left).Should().BeGreaterThan(side.Right - side.Left);
        }
    }

    [Fact]
    public void ChipsInAStack_AreTurnedDifferently()
    {
        var first = ChipRenderer.EdgeStripe(ChipRenderer.EdgeStripes / 2, 0, 40);
        var second = ChipRenderer.EdgeStripe(ChipRenderer.EdgeStripes / 2, 1, 40);
        (first, second).Should().NotBe((null, null));
        second.Should().NotBe(first);
    }

    [Fact]
    public void EveryChip_HasStripesThatStandOutFromIt()
    {
        foreach (var value in Chips.Denominations.Append(7))
        {
            var difference = MathF.Abs(Luma(ChipRenderer.StripeColorOf(value)) - Luma(ChipRenderer.ColorOf(value)));
            difference.Should().BeGreaterThan(0.3f, $"the {value} chip's stripes should show");
        }

        static float Luma(Color c) => ((0.2126f * c.R) + (0.7152f * c.G) + (0.0722f * c.B)) / 255;
    }
}
