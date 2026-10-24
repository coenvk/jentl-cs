using Jentl.Core.Tracks;
using Jentl.Tracks;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class RoadPainterTests
{
    private static readonly RoadStyle Style = new(
        Ground: Color.Green, Verge: Color.Tan, Surface: Color.Gray, EdgeA: Color.Red, EdgeB: Color.White,
        CentreLine: Color.White, Width: 20, VergeWidth: 4, KerbLength: 6);

    private static readonly TrackPath Oval = TrackPath.Build([new Straight(200), new Arc(40, 180), new Straight(200), new Arc(40, 180)]);

    [Fact]
    public void Paint_RoadPixelsUnderPath_GroundFarAway()
    {
        var pixels = RoadPainter.Paint(Oval, Style, padding: 30, out var bounds);

        pixels.Should().HaveCount(bounds.Width * bounds.Height);
        foreach (var point in Oval.Polyline(4))
        {
            var (x, y) = ((int)MathF.Round(point.X) - bounds.X, (int)MathF.Round(point.Y) - bounds.Y);
            pixels[(y * bounds.Width) + x].Should().BeOneOf(Style.Surface, Style.CentreLine);
        }

        pixels[0].Should().Be(Style.Ground);
    }

    [Fact]
    public void Paint_KerbsOnlyInCorners()
    {
        var pixels = RoadPainter.Paint(Oval, Style, padding: 30, out var bounds);
        Color At(Vector2 world) => pixels[(((int)MathF.Round(world.Y) - bounds.Y) * bounds.Width) + (int)MathF.Round(world.X) - bounds.X];

        // The outer edge in the middle of the first corner, and in the middle of the first straight (left side is -y).
        var corner = Oval.At(200 + (Math.PI * 40 / 2));
        var cornerEdge = corner.Position + (new Vector2(MathF.Cos(corner.Heading - (MathF.PI / 2)), MathF.Sin(corner.Heading - (MathF.PI / 2))) * -9);
        var straightEdge = Oval.At(100).Position + new Vector2(0, 9);

        At(cornerEdge).Should().BeOneOf(Style.EdgeA, Style.EdgeB);
        At(straightEdge).Should().Be(Style.Surface);
    }

    [Fact]
    public void Paint_IncludesPaddingInBounds()
    {
        RoadPainter.Paint(Oval, Style, padding: 30, out var bounds);

        bounds.Left.Should().BeLessThanOrEqualTo(-40 - 30);
        bounds.Top.Should().BeLessThanOrEqualTo(-80 - 30);
        bounds.Right.Should().BeGreaterThanOrEqualTo(240 + 30);
    }
}
