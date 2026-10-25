using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class TextureTests
{
    private static readonly uint R = Colors.Pack(new Vector4(1, 0, 0, 1));
    private static readonly uint G = Colors.Pack(new Vector4(0, 1, 0, 1));
    private static readonly uint B = Colors.Pack(new Vector4(0, 0, 1, 1));
    private static readonly uint W = Colors.Pack(Vector4.One);
    private static readonly uint Black = Colors.Pack(new Vector4(0, 0, 0, 1));

    // 2x2: red, green on top (v = 0); blue, white below.
    private static Texture Checker(TextureFilter filter = TextureFilter.Nearest, TextureWrap wrap = TextureWrap.Repeat) =>
        new(2, 2, [R, G, B, W]) { Filter = filter, Wrap = wrap };

    [Fact]
    public void Sample_Nearest_PicksTheTexel()
    {
        var t = Checker();

        t.Sample(new Vector2(0.25f, 0.25f)).Should().Be(Colors.Unpack(R));
        t.Sample(new Vector2(0.75f, 0.25f)).Should().Be(Colors.Unpack(G));
        t.Sample(new Vector2(0.25f, 0.75f)).Should().Be(Colors.Unpack(B));
    }

    [Fact]
    public void Sample_Repeat_Wraps_Clamp_Holds()
    {
        Checker().Sample(new Vector2(1.25f, -0.75f)).Should().Be(Colors.Unpack(R));
        Checker(wrap: TextureWrap.Clamp).Sample(new Vector2(5, 0.25f)).Should().Be(Colors.Unpack(G));
    }

    [Fact]
    public void Sample_Bilinear_BlendsNeighbours()
    {
        var c = Checker(TextureFilter.Bilinear, TextureWrap.Clamp).Sample(new Vector2(0.5f, 0.25f));

        c.X.Should().BeApproximately(0.5f, 0.01f);
        c.Y.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Constructor_NeedsEveryPixel()
    {
        var make = () => new Texture(2, 2, [R, G, B]);

        make.Should().Throw<ArgumentException>();
    }
}

public class TexturedDrawingTests
{
    private const int Size = 64;
    private static readonly uint R = Colors.Pack(new Vector4(1, 0, 0, 1));
    private static readonly uint G = Colors.Pack(new Vector4(0, 1, 0, 1));
    private static readonly uint B = Colors.Pack(new Vector4(0, 0, 1, 1));
    private static readonly uint W = Colors.Pack(Vector4.One);
    private static readonly uint Black = Colors.Pack(new Vector4(0, 0, 0, 1));

    private static Renderer Pixels()
    {
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = Matrix4x4.CreateOrthographicOffCenter(0, Size, 0, Size, -10, 10) };
        renderer.Clear(Black);
        return renderer;
    }

    // A 20x20 square at (10, 10)-(30, 30), uv (0, 0) at its top-left.
    private static void Square(Renderer renderer, Texture texture, Vector4? tint = null)
    {
        var c = tint ?? Vector4.One;
        Vertex a = new(new(10, 10, 0), c, new(0, 1)), b = new(new(30, 10, 0), c, new(1, 1));
        Vertex d = new(new(30, 30, 0), c, new(1, 0)), e = new(new(10, 30, 0), c, new(0, 0));
        renderer.DrawTriangle(a, b, d, Matrix4x4.Identity, texture);
        renderer.DrawTriangle(a, d, e, Matrix4x4.Identity, texture);
    }

    [Fact]
    public void TexturedSquare_ShowsEachQuarter()
    {
        var renderer = Pixels();

        Square(renderer, new Texture(2, 2, [R, G, B, W]));

        // Screen y runs down: the square's top (uv v = 0) is at row 64 - 30 = 34.
        renderer.Target[15, 39].Should().Be(R);
        renderer.Target[25, 39].Should().Be(G);
        renderer.Target[15, 49].Should().Be(B);
        renderer.Target[25, 49].Should().Be(W);
    }

    [Fact]
    public void Texture_IsTintedByTheVertexColour()
    {
        var renderer = Pixels();

        Square(renderer, new Texture(1, 1, [W]), new Vector4(1, 0.5f, 0, 1));

        Colors.Unpack(renderer.Target[20, 44]).Y.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void ClearTexels_AreCutOut_LeavingWhatIsBehind()
    {
        var renderer = Pixels();
        var clear = Colors.Pack(new Vector4(0, 0, 0, 0));

        Square(renderer, new Texture(2, 1, [R, clear]));

        renderer.Target[15, 44].Should().Be(R);
        renderer.Target[25, 44].Should().Be(Black);
        renderer.Depth.Depths[(44 * Size) + 25].Should().Be(1, "a cut-out texel writes no depth");
    }

    [Theory]
    [InlineData(TextureMapping.PerspectiveCorrect, 0u)]
    [InlineData(TextureMapping.Affine, 1u)]
    public void FloorTexture_PerspectiveCorrect_OrAffine(TextureMapping mapping, uint expectWhite)
    {
        // A floor from z = -1 to -11, black on its near half (v < 0.5), white on its far half. At z = -4 (v = 0.3) the
        // correct texel is black; affine mapping, which blends straight across the screen, already shows white there.
        var camera = new Camera { Position = new Vector3(0, 1, 0), Target = new Vector3(0, 1, -10), FieldOfView = MathF.PI / 2, Near = 0.1f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1), CullBackFaces = false, Mapping = mapping };
        renderer.Clear(Colors.Pack(new Vector4(1, 0, 0, 1)));
        var texture = new Texture(1, 2, [W, Black]) { Wrap = TextureWrap.Clamp }; // v = 0 far (white), v = 1 near (black)
        var white = Vector4.One;
        Vertex a = new(new(-2, 0, -1), white, new(0, 1)), b = new(new(2, 0, -1), white, new(1, 1));
        Vertex c = new(new(2, 0, -11), white, new(1, 0)), d = new(new(-2, 0, -11), white, new(0, 0));
        renderer.DrawTriangle(a, b, c, Matrix4x4.Identity, texture);
        renderer.DrawTriangle(a, c, d, Matrix4x4.Identity, texture);

        var p = Vector4.Transform(new Vector4(0, 0, -4, 1), renderer.ViewProjection);
        var row = (int)((1 - (p.Y / p.W)) / 2 * Size);

        (renderer.Target[Size / 2, row] == W ? 1u : 0u).Should().Be(expectWhite);
    }

    [Fact]
    public void DrawMesh_UsesTheMeshesTextureCoordinates()
    {
        var renderer = Pixels();
        var plane = MeshBuilder.Plane(40, 40).Transform(Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(32, 32, 0));

        renderer.DrawMesh(plane, Matrix4x4.Identity, new Material(Vector4.One, Shading.Unlit, new Texture(2, 2, [R, G, B, W])));

        new[] { R, G, B, W }.Should().OnlyContain(c => renderer.Target.Pixels.Contains(c));
    }
}
