using Jentl.Cards;
using Jentl.Core.Cards;
using Jentl.Core.UI;

namespace Jentl.Core.Tests;

public sealed class SuitGlyphTests : IDisposable
{
    private readonly SuitGlyphSource _suits = new();

    public void Dispose() => _suits.Dispose();

    [Theory]
    [InlineData('♠')]
    [InlineData('♥')]
    [InlineData('♦')]
    [InlineData('♣')]
    public void TheFourSuits_AreInTheFont(char suit) => _suits.GetGlyphId(suit).Should().NotBeNull();

    [Fact]
    public void Letters_AreNot() => _suits.GetGlyphId('A').Should().BeNull();

    [Fact]
    public void ASuit_SitsOnTheBaseline_AndAdvancesPastItself()
    {
        _suits.GetMetricsForSize(32, out var ascent, out var descent, out var lineHeight);
        _suits.GetGlyphMetrics(_suits.GetGlyphId('♠')!.Value, 32, out var advance, out var x0, out var y0, out var x1, out var y1);

        (ascent, lineHeight).Should().Be((ascent, ascent - descent));
        y1.Should().BeLessThanOrEqualTo(0, "a suit stands on the baseline");
        (-y0).Should().BeLessThanOrEqualTo(ascent);
        x0.Should().BeGreaterThanOrEqualTo(0);
        advance.Should().BeGreaterThan(x1);
    }

    [Fact]
    public void ARasterisedSuit_IsFilledInTheMiddle_AndEmptyInTheCorners()
    {
        var id = _suits.GetGlyphId('♦')!.Value;
        _suits.GetGlyphMetrics(id, 32, out _, out var x0, out var y0, out var x1, out var y1);
        var (width, height) = (x1 - x0, y1 - y0);
        var buffer = new byte[width * height];

        _suits.RasterizeGlyphBitmap(id, 32, buffer, 0, width, height, width);

        buffer[(height / 2 * width) + (width / 2)].Should().Be(255);
        buffer[0].Should().Be(0);
        buffer[^1].Should().Be(0);
    }

    [Fact]
    public void TheCardArtAndTheFont_ShareOneShapePerSuit()
    {
        SuitShape.Covers(Suit.Hearts, 0, 0.2f).Should().BeTrue();
        SuitShape.Covers(Suit.Hearts, 0.95f, 0.95f).Should().BeFalse();
        SuitShape.Covers(Suit.Spades, 0, 0.8f).Should().BeTrue("the spade has a stem");
        SuitShape.Covers(Suit.Hearts, 0, -0.9f).Should().BeFalse("a heart dips between its lobes");
    }
}
