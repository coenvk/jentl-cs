using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class TranslucentTests
{
    private const int Size = 16;

    private static Renderer Perspective(Vector4 background)
    {
        var camera = new Camera { Position = Vector3.Zero, Target = -Vector3.UnitZ, FieldOfView = MathF.PI / 2, Near = 0.5f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1) };
        renderer.Clear(Colors.Pack(background));
        return renderer;
    }

    private static Mesh Wall(float distance) => MeshBuilder.Quad().Transform(Matrix4x4.CreateScale(40) * Matrix4x4.CreateTranslation(0, -20, -distance));

    [Fact]
    public void Translucent_BlendsOverWhatIsThere_AndWritesNoDepth()
    {
        var renderer = Perspective(new Vector4(0, 0, 1, 1));
        var depth = renderer.Depth.Depths[(Size / 2 * Size) + (Size / 2)];

        renderer.DrawMesh(Wall(4), Matrix4x4.Identity, new Material(new Vector4(1, 0, 0, 0.5f), Shading.Unlit, Translucent: true));

        var mixed = Colors.Unpack(renderer.Target[Size / 2, Size / 2]);
        mixed.X.Should().BeApproximately(0.5f, 0.01f);
        mixed.Z.Should().BeApproximately(0.5f, 0.01f);
        renderer.Depth.Depths[(Size / 2 * Size) + (Size / 2)].Should().Be(depth);
    }

    [Fact]
    public void Translucent_IsHiddenBehindNearerThings()
    {
        var green = new Vector4(0, 1, 0, 1);
        var renderer = Perspective(new Vector4(0, 0, 1, 1));
        renderer.DrawMesh(Wall(2), Matrix4x4.Identity, new Material(green, Shading.Unlit));

        renderer.DrawMesh(Wall(4), Matrix4x4.Identity, new Material(new Vector4(1, 0, 0, 0.5f), Shading.Unlit, Translucent: true));

        Colors.Unpack(renderer.Target[Size / 2, Size / 2]).Should().Be(green);
    }
}

public class SkyTests
{
    [Fact]
    public void Sky_RunsFromTheHorizonToTheZenith_AndBelowIsTheGround()
    {
        var zenith = new Vector4(0, 0, 1, 1);
        var horizon = new Vector4(1, 1, 1, 1);
        var ground = new Vector4(0, 1, 0, 1);
        var camera = new Camera { Position = Vector3.Zero, Target = -Vector3.UnitZ, FieldOfView = MathF.PI / 2 };
        var renderer = new Renderer(new FrameBuffer(8, 32)) { ViewProjection = camera.ViewProjection(8 / 32f) };

        renderer.Clear(new Sky(zenith, horizon, ground));

        var top = Colors.Unpack(renderer.Target[4, 0]);
        var middle = Colors.Unpack(renderer.Target[4, 15]);
        top.Z.Should().BeGreaterThan(top.X, "high up the sky is mostly the zenith's colour");
        middle.X.Should().BeGreaterThan(0.8f, "just above the horizon it is the horizon's");
        Colors.Unpack(renderer.Target[4, 31]).Should().Be(ground);
        renderer.Depth.Depths.Should().OnlyContain(d => d == 1);
    }
}

public class SpotTests
{
    [Fact]
    public void Spot_IsSolidInTheMiddle_AndClearAtTheCorners()
    {
        var spot = Textures.Spot(16, new Vector4(0, 0, 0, 0.5f), softness: 0);

        (spot.Pixels[(8 * 16) + 8] >> 24).Should().Be(128u);
        (spot.Pixels[0] >> 24).Should().Be(0u);
        spot.Wrap.Should().Be(TextureWrap.Clamp);
    }
}
