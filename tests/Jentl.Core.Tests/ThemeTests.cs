using Jentl.Core.Graphics;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void ASharpRectangle_IsOnePiece()
    {
        var bounds = new Rectangle(10, 20, 100, 40);

        RoundedRectangle.Pieces(bounds, 0).Should().Equal(new RoundedRectangle.Piece(bounds, RoundedRectangle.Corner.None));
    }

    [Fact]
    public void ARoundedRectangle_IsABodyTwoSidesAndFourCorners_CoveringItExactly()
    {
        var bounds = new Rectangle(10, 20, 100, 40);

        var pieces = RoundedRectangle.Pieces(bounds, 8);

        pieces.Where(p => p.Corner != RoundedRectangle.Corner.None).Select(p => p.Corner).Should().BeEquivalentTo(
            [RoundedRectangle.Corner.TopLeft, RoundedRectangle.Corner.TopRight, RoundedRectangle.Corner.BottomLeft, RoundedRectangle.Corner.BottomRight]);
        pieces.Where(p => p.Corner != RoundedRectangle.Corner.None).Should().OnlyContain(p => p.Area.Width == 8 && p.Area.Height == 8);
        pieces.Sum(p => p.Area.Width * p.Area.Height).Should().Be(100 * 40, "the pieces tile the rectangle without overlap");
        pieces.Should().OnlyContain(p => bounds.Contains(p.Area));
    }

    [Fact]
    public void TheRadius_IsKeptWithinHalfTheShorterSide()
    {
        var pieces = RoundedRectangle.Pieces(new Rectangle(0, 0, 100, 10), 50);

        pieces.Where(p => p.Corner != RoundedRectangle.Corner.None).Should().OnlyContain(p => p.Area.Width == 5 && p.Area.Height == 5);
        pieces.Sum(p => p.Area.Width * p.Area.Height).Should().Be(100 * 10);
    }

    [Fact]
    public void ACornerIsSolidInside_ClearOutside_AndSoftOnItsEdge()
    {
        // The top-left corner of radius 16: the circle's centre is the tile's bottom-right corner.
        RoundedRectangle.Coverage(15, 15, 16).Should().Be(1);
        RoundedRectangle.Coverage(0, 0, 16).Should().Be(0);
        var edge = RoundedRectangle.Coverage(4, 4, 16);
        edge.Should().BeInRange(0, 1);
    }

    [Fact]
    public void TheDefaultTheme_LooksAsBefore()
    {
        var style = new SurfaceStyle();

        (style.Radius, style.Border, style.Shadow).Should().Be((0, 0, 0));
        style.BorderColor.Should().BeNull();
        new Theme().AccentBar.Should().BeTrue("panels keep their accent bar along the top");
    }

    [Fact]
    public void AScreenshot_IsOpaque_KeepingItsColours()
    {
        // A dimmed overlay leaves the back buffer's alpha below 1; saved as is, viewers show the PNG see-through.
        Color[] pixels = [new(6, 15, 11, 202), new(22, 44, 34, 255)];

        JentlGame.MakeOpaque(pixels);

        pixels.Should().Equal(new Color(6, 15, 11, 255), new Color(22, 44, 34, 255));
    }

    [Fact]
    public void AThemeScalesItsSizes()
    {
        var style = new SurfaceStyle(Radius: 10, Border: 2, Shadow: 4);

        style.Scaled(1.5f).Should().Be(new SurfaceStyle(Radius: 15, Border: 3, Shadow: 6));
        new SurfaceStyle(Border: 1).Scaled(0.6f).Border.Should().Be(1, "a border never vanishes when scaled down");
    }
}
