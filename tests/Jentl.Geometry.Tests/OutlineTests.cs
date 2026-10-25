using System.Numerics;

namespace Jentl.Geometry.Tests;

public class OutlineTests
{
    // A picture from rows of text: X is solid, anything else is empty; y runs down.
    private static (int Width, int Height, Func<int, int, bool> Solid) Picture(params string[] rows) =>
        (rows[0].Length, rows.Length, (x, y) => rows[y][x] == 'X');

    private static IReadOnlyList<Vector2> Trace(params string[] rows)
    {
        var (width, height, solid) = Picture(rows);
        return Polygon2.Outline(width, height, solid);
    }

    [Fact]
    public void Outline_OfOnePixel_IsItsSquare()
    {
        var outline = Trace("...", ".X.", "...");

        outline.Should().HaveCount(4);
        outline.Should().BeEquivalentTo([new Vector2(1, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(1, 2)]);
    }

    [Fact]
    public void Outline_RunsAlongPixelEdges_WithOnlyItsCorners()
    {
        var outline = Trace(
            "X..",
            "X..",
            "XXX");

        outline.Should().HaveCount(6);
        Math.Abs(Polygon2.SignedArea(outline)).Should().Be(5);
        Polygon2.Triangulate(outline).Should().HaveCount(4);
    }

    [Fact]
    public void Outline_FillsHoles()
    {
        var outline = Trace(
            "XXX",
            "X.X",
            "XXX");

        outline.Should().HaveCount(4);
        Math.Abs(Polygon2.SignedArea(outline)).Should().Be(9);
    }

    [Fact]
    public void Outline_KeepsTheBiggestPiece()
    {
        var outline = Trace(
            "XX...",
            "XX..X",
            ".....");

        Math.Abs(Polygon2.SignedArea(outline)).Should().Be(4);
        outline.Should().OnlyContain(p => p.X <= 2);
    }

    [Fact]
    public void Outline_OfPiecesTouchingAtACorner_IsStillOneSimpleOutline()
    {
        // Two blocks touching only at a corner, joined round the top right: the gap they close off is filled.
        var outline = Trace(
            "XXXXX",
            "XX..X",
            "..XXX",
            "..XX.");

        Math.Abs(Polygon2.SignedArea(outline)).Should().Be(15); // 13 pixels and the 2 closed off
        outline.Distinct().Should().HaveCount(outline.Count);
        Polygon2.Triangulate(outline).Sum(t => Math.Abs(Polygon2.SignedArea([outline[t.A], outline[t.B], outline[t.C]]))).Should().BeApproximately(15, 1e-3f);
    }

    [Fact]
    public void Outline_OfNothing_IsEmpty()
    {
        Trace("...", "...").Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Outline_OfARaggedPicture_TriangulatesWhole(int seed)
    {
        var random = new Random(seed);
        var pixels = new bool[24 * 12];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = random.NextDouble() < 0.6;
        }

        var outline = Polygon2.Outline(24, 12, (x, y) => pixels[(y * 24) + x]);
        var triangles = Polygon2.Triangulate(outline);

        triangles.Should().HaveCount(outline.Count - 2);
        triangles.Sum(t => Math.Abs(Polygon2.SignedArea([outline[t.A], outline[t.B], outline[t.C]])))
            .Should().BeApproximately(Math.Abs(Polygon2.SignedArea(outline)), 1e-2f);
    }
}
