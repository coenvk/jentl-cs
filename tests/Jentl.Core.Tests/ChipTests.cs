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
}
