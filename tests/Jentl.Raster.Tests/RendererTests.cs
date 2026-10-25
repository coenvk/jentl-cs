using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class BufferTests
{
    [Fact]
    public void Clear_FillsColourAndDepth()
    {
        var frame = new FrameBuffer(4, 3);
        var depth = new DepthBuffer(4, 3);

        frame.Clear(Colors.Pack(new Vector4(0, 0, 1, 1)));
        depth.Clear();

        frame.Pixels.Should().OnlyContain(p => p == Colors.Pack(new Vector4(0, 0, 1, 1)));
        depth.Depths.Should().OnlyContain(d => d == 1);
        frame[3, 2].Should().Be(Colors.Pack(new Vector4(0, 0, 1, 1)));
    }

    [Fact]
    public void Pack_MatchesXnaColourOrder_RedInTheLowByte()
    {
        Colors.Pack(new Vector4(1, 0, 0, 1)).Should().Be(0xFF0000FFu);
        Colors.Unpack(0xFF00FF00u).Should().Be(new Vector4(0, 1, 0, 1));
    }
}

public class RendererTests
{
    private const int Size = 64;
    private static readonly Vector4 Red = new(1, 0, 0, 1);
    private static readonly Vector4 Green = new(0, 1, 0, 1);
    private static readonly Vector4 Blue = new(0, 0, 1, 1);
    private static readonly uint Black = Colors.Pack(new Vector4(0, 0, 0, 1));

    // A camera at the origin looking down -Z, 90° field of view, square.
    private static Renderer Perspective()
    {
        var camera = new Camera { Position = Vector3.Zero, Target = -Vector3.UnitZ, FieldOfView = MathF.PI / 2, Near = 0.5f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1) };
        renderer.Clear(Black);
        return renderer;
    }

    // World x and y are pixels: (0, 0) is the bottom-left corner, y up.
    private static Renderer Pixels(int width = Size, int height = Size)
    {
        var renderer = new Renderer(new FrameBuffer(width, height))
        {
            ViewProjection = Matrix4x4.CreateOrthographicOffCenter(0, width, 0, height, -10, 10),
        };
        renderer.Clear(Black);
        return renderer;
    }

    private static Vector4 At(Renderer renderer, int x, int y) => Colors.Unpack(renderer.Target[x, y]);

    private static void Quad(Renderer renderer, float z, float half, Vector4 color, bool flip = false)
    {
        Vertex a = new(new(-half, -half, z), color), b = new(new(half, -half, z), color);
        Vertex c = new(new(half, half, z), color), d = new(new(-half, half, z), color);
        if (flip)
        {
            renderer.DrawTriangle(a, c, b, Matrix4x4.Identity);
            renderer.DrawTriangle(a, d, c, Matrix4x4.Identity);
        }
        else
        {
            renderer.DrawTriangle(a, b, c, Matrix4x4.Identity);
            renderer.DrawTriangle(a, c, d, Matrix4x4.Identity);
        }
    }

    [Fact]
    public void FrontFacingQuad_IsDrawnInTheMiddle_NotInTheCorner()
    {
        var renderer = Perspective();

        Quad(renderer, -5, 1, Red);

        At(renderer, Size / 2, Size / 2).Should().Be(Red);
        renderer.Target[0, 0].Should().Be(Black);
        renderer.Stats.Triangles.Should().Be(2);
    }

    [Fact]
    public void BackFaces_AreCulled_UnlessCullingIsOff()
    {
        var culled = Perspective();
        Quad(culled, -5, 1, Red, flip: true);
        culled.Stats.Pixels.Should().Be(0);

        var open = Perspective();
        open.CullBackFaces = false;
        Quad(open, -5, 1, Red, flip: true);
        At(open, Size / 2, Size / 2).Should().Be(Red);
    }

    [Fact]
    public void Nearer_Wins_WhicheverIsDrawnFirst()
    {
        var renderer = Perspective();

        Quad(renderer, -5, 1, Red);
        Quad(renderer, -10, 4, Blue);

        At(renderer, Size / 2, Size / 2).Should().Be(Red);
        At(renderer, Size / 2, 22).Should().Be(Blue); // the blue quad spans rows 19-45, the red one 26-38
    }

    [Fact]
    public void SharedEdges_HaveNoGapsAndNoDoublePixels()
    {
        // A 10x10 square from two triangles covers exactly 100 pixel centres.
        var renderer = Pixels();
        Vertex a = new(new(10, 10, 0), Red), b = new(new(20, 10, 0), Red), c = new(new(20, 20, 0), Red), d = new(new(10, 20, 0), Red);

        renderer.DrawTriangle(a, b, c, Matrix4x4.Identity);
        renderer.DrawTriangle(a, c, d, Matrix4x4.Identity);

        renderer.Stats.Pixels.Should().Be(100);
        renderer.Target.Pixels.Count(p => p != Black).Should().Be(100);
    }

    [Fact]
    public void Colours_BlendAcrossTheTriangle()
    {
        var renderer = Pixels();

        renderer.DrawTriangle(new(new(0, 0, 0), Red), new(new(60, 0, 0), Green), new(new(0, 60, 0), Blue), Matrix4x4.Identity);

        var corner = At(renderer, 2, Size - 3); // near the red corner (screen y is down)
        corner.X.Should().BeGreaterThan(0.85f);
        var middle = At(renderer, 20, Size - 21);
        middle.X.Should().BeInRange(0.2f, 0.45f);
        middle.Y.Should().BeInRange(0.2f, 0.45f);
        middle.Z.Should().BeInRange(0.2f, 0.45f);
    }

    [Fact]
    public void Colours_ArePerspectiveCorrect()
    {
        // A floor strip from z = -1 (black) to z = -11 (white): where its world midpoint lands, it must be mid grey,
        // which an interpolation straight across the screen would get wrong (it would be much darker).
        var camera = new Camera { Position = new Vector3(0, 1, 0), Target = new Vector3(0, 1, -10), FieldOfView = MathF.PI / 2, Near = 0.1f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1), CullBackFaces = false };
        renderer.Clear(Black);
        Vector4 dark = new(0, 0, 0, 1), light = new(1, 1, 1, 1);
        Vertex a = new(new(-2, 0, -1), dark), b = new(new(2, 0, -1), dark), c = new(new(2, 0, -11), light), d = new(new(-2, 0, -11), light);
        renderer.DrawTriangle(a, b, c, Matrix4x4.Identity);
        renderer.DrawTriangle(a, c, d, Matrix4x4.Identity);

        var middle = Vector4.Transform(new Vector4(0, 0, -6, 1), renderer.ViewProjection);
        var y = (int)((1 - (middle.Y / middle.W)) / 2 * Size);

        At(renderer, Size / 2, y).X.Should().BeApproximately(0.5f, 0.08f);
    }

    [Fact]
    public void TriangleThroughTheNearPlane_IsClipped_NotLost()
    {
        var renderer = Perspective();

        // One corner behind the camera: without clipping the projection would flip and smear it.
        renderer.DrawTriangle(new(new(-1, -1, -5), Red), new(new(1, -1, -5), Red), new(new(0, 3, 5), Red), Matrix4x4.Identity);

        renderer.Stats.Pixels.Should().BeGreaterThan(0);
        Enumerable.Range(0, Size).Should().OnlyContain(x => renderer.Target[x, Size - 4] == Black, "nothing below the near edge (y = -0.2)");
        renderer.Target.Pixels.Should().OnlyContain(p => p == Black || p == Colors.Pack(Red));
    }

    [Fact]
    public void OffScreenAndBehind_DrawNothing()
    {
        var renderer = Perspective();

        renderer.DrawTriangle(new(new(50, 50, -5), Red), new(new(52, 50, -5), Red), new(new(51, 52, -5), Red), Matrix4x4.Identity);
        renderer.DrawTriangle(new(new(-1, -1, 5), Red), new(new(1, -1, 5), Red), new(new(0, 1, 5), Red), Matrix4x4.Identity);

        renderer.Stats.Pixels.Should().Be(0);
    }

    [Fact]
    public void LitBox_TopFacingTheLight_IsBrighterThanItsSide()
    {
        var camera = new Camera { Position = new Vector3(3, 3, 3), Target = Vector3.Zero, FieldOfView = MathF.PI / 3, Near = 0.5f, Far = 50 };
        var renderer = new Renderer(new FrameBuffer(Size, Size))
        {
            ViewProjection = camera.ViewProjection(1),
            Lighting = new Lighting(Direction: new Vector3(0, -1, -0.2f), Color: Vector3.One, Ambient: new Vector3(0.2f)),
        };
        renderer.Clear(Black);

        renderer.DrawMesh(MeshBuilder.Box(new Vector3(2)), Matrix4x4.Identity, new Material(new Vector4(0.8f, 0.8f, 0.8f, 1), Shading.Flat));

        var top = Project(renderer, new Vector3(0, 1, 0));
        var side = Project(renderer, new Vector3(1, 0, 0));
        At(renderer, top.X, top.Y).X.Should().BeGreaterThan(At(renderer, side.X, side.Y).X + 0.2f);
        At(renderer, side.X, side.Y).X.Should().BeGreaterThan(0.1f, "ambient light reaches the side");
    }

    [Fact]
    public void Unlit_IgnoresTheLight()
    {
        var renderer = Pixels();
        renderer.Lighting = new Lighting(Vector3.UnitZ, Vector3.Zero, Vector3.Zero);

        renderer.DrawMesh(MeshBuilder.Box(new Vector3(20)).Transform(Matrix4x4.CreateTranslation(32, 32, 0)), Matrix4x4.Identity, new Material(Green, Shading.Unlit));

        At(renderer, 32, 32).Should().Be(Green);
    }

    [Fact]
    public void Camera_ViewProjection_PutsTheTargetInTheMiddle()
    {
        var camera = new Camera { Position = new Vector3(4, 2, 7), Target = new Vector3(-1, 0, 0) };

        var clip = Vector4.Transform(new Vector4(camera.Target, 1), camera.ViewProjection(16 / 9f));

        (clip.X / clip.W).Should().BeApproximately(0, 1e-5f);
        (clip.Y / clip.W).Should().BeApproximately(0, 1e-5f);
        (clip.Z / clip.W).Should().BeInRange(0, 1);
    }

    private static Point Project(Renderer renderer, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), renderer.ViewProjection);
        return new Point((int)((clip.X / clip.W + 1) / 2 * Size), (int)((1 - (clip.Y / clip.W)) / 2 * Size));
    }

    private readonly record struct Point(int X, int Y);
}
