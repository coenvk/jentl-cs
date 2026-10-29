using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class LightingTests
{
    private static readonly Vector4 Grey = new(0.5f, 0.5f, 0.5f, 1);

    [Fact]
    public void FillLights_AddToTheMainOne()
    {
        var lighting = new Lighting(-Vector3.UnitY, new Vector3(0.5f), Vector3.Zero)
        {
            Fill = [new ParallelLight(Vector3.UnitY, new Vector3(0.25f)), new ParallelLight(-Vector3.UnitX, new Vector3(1))],
        };

        lighting.Shade(Vector4.One, Vector3.UnitY).Should().Be(new Vector4(0.5f, 0.5f, 0.5f, 1));
        lighting.Shade(Vector4.One, -Vector3.UnitY).Should().Be(new Vector4(0.25f, 0.25f, 0.25f, 1));
        lighting.Shade(Vector4.One, Vector3.UnitX).Should().Be(Vector4.One);
        var act = () => lighting with { Fill = [default, default, default] };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Lightings_WithTheSameFill_AreEqual()
    {
        var a = Lighting.Default with { Fill = [new ParallelLight(Vector3.UnitX, Vector3.One)] };
        var b = Lighting.Default with { Fill = [new ParallelLight(Vector3.UnitX, Vector3.One)] };

        a.Should().Be(b);
        a.Should().NotBe(Lighting.Default);
    }

    [Fact]
    public void Emissive_GlowsInTheDark()
    {
        var dark = new Lighting(-Vector3.UnitY, Vector3.Zero, Vector3.Zero);
        var lamp = new Material(Grey) { Emissive = new Vector3(0.25f, 0, 0) };

        lamp.Shade(dark, Vector3.UnitY).Should().Be(new Vector4(0.25f, 0, 0, 1));
    }

    [Fact]
    public void DoubleSided_DrawsTheBackFace()
    {
        var camera = new Camera { Position = new Vector3(0, 0, -3), Target = Vector3.Zero };
        var renderer = new Renderer(new FrameBuffer(16, 16)) { ViewProjection = camera.ViewProjection(1) };
        var quad = MeshBuilder.Box(new Vector3(2, 2, 0.01f));
        var front = new Mesh(quad.Positions, [.. quad.Triangles.Where(t => quad.TriangleAt(t).Normal.Z > 0.5f)], quad.Normals, quad.UVs);

        renderer.Clear(0);
        renderer.DrawMesh(front, Matrix4x4.Identity, new Material(Vector4.One, Shading.Unlit));
        var culled = renderer.Target.Pixels.Count(p => p != 0);
        renderer.Clear(0);
        renderer.DrawMesh(front, Matrix4x4.Identity, new Material(Vector4.One, Shading.Unlit) { DoubleSided = true });

        culled.Should().Be(0);
        renderer.Target.Pixels.Count(p => p != 0).Should().BeGreaterThan(0);
        renderer.CullBackFaces.Should().BeTrue("the material only changes it for its own mesh");
    }
}
