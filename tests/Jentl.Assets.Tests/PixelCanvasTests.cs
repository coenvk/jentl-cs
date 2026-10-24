namespace Jentl.Assets.Tests;

public class PixelCanvasTests
{
    private static readonly Rgba Red = Rgba.Hex(0xff0000);
    private static readonly Rgba Black = Rgba.Hex(0x000000);

    [Fact]
    public void Rect_ClipsAtEdges()
    {
        var canvas = new PixelCanvas(4, 3);

        canvas.Rect(2, 1, 10, 10, Red);

        canvas[3, 2].Should().Be(Red);
        canvas[1, 1].Should().Be(Rgba.Clear);
        canvas[9, 9].Should().Be(Rgba.Clear);
    }

    [Fact]
    public void Line_DrawsBothEnds()
    {
        var canvas = new PixelCanvas(5, 5);

        canvas.Line(0, 0, 4, 2, Red);

        canvas[0, 0].Should().Be(Red);
        canvas[4, 2].Should().Be(Red);
        Enumerable.Range(0, 5).Count(x => Enumerable.Range(0, 5).Any(y => canvas[x, y] == Red)).Should().Be(5);
    }

    [Fact]
    public void Ellipse_Filled_CoversItsCentre_NotItsCorners()
    {
        var canvas = new PixelCanvas(9, 9);

        canvas.Ellipse(4, 4, 3, 2, Red, filled: true);

        canvas[4, 4].Should().Be(Red);
        canvas[7, 4].Should().Be(Red);
        canvas[7, 6].Should().Be(Rgba.Clear);
    }

    [Fact]
    public void Outline_SurroundsShape()
    {
        var canvas = new PixelCanvas(5, 5);
        canvas[2, 2] = Red;

        canvas.Outline(Black);

        canvas[2, 1].Should().Be(Black);
        canvas[1, 2].Should().Be(Black);
        canvas[1, 1].Should().Be(Rgba.Clear); // diagonals stay clear
        canvas[2, 2].Should().Be(Red);
    }

    [Fact]
    public void MirrorX_CopiesLeftHalf()
    {
        var canvas = new PixelCanvas(4, 1);
        canvas[0, 0] = Red;
        canvas[1, 0] = Black;

        canvas.MirrorX();

        canvas[3, 0].Should().Be(Red);
        canvas[2, 0].Should().Be(Black);
    }

    [Fact]
    public void Blit_SkipsClearPixels()
    {
        var target = new PixelCanvas(3, 1);
        target.Fill(Black);
        var source = new PixelCanvas(2, 1);
        source[1, 0] = Red;

        target.Blit(source, 1, 0);

        target[1, 0].Should().Be(Black);
        target[2, 0].Should().Be(Red);
    }

    [Fact]
    public void ToPng_RoundTrips()
    {
        var canvas = new PixelCanvas(3, 2);
        canvas[2, 1] = Red.WithAlpha(128);

        var image = Png.Decode(canvas.ToPng());

        image.Width.Should().Be(3);
        image[2, 1].Should().Be(((byte)255, (byte)0, (byte)0, (byte)128));
        image[0, 0].A.Should().Be(0);
    }

    [Fact]
    public void Colours_MixLighterDarker()
    {
        Rgba.Hex(0x808080).Lighter(0.5).Should().Be(new Rgba(192, 192, 192));
        Rgba.Hex(0x808080).Darker(0.5).Should().Be(new Rgba(64, 64, 64));
        Red.Mix(Black, 0.5).Should().Be(new Rgba(128, 0, 0));
    }
}
