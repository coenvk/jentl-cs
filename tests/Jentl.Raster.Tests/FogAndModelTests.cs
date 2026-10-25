using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class FogTests
{
    private const int Size = 16;
    private static readonly Vector4 Red = new(1, 0, 0, 1);
    private static readonly Vector4 Blue = new(0, 0, 1, 1);

    // The middle pixel after drawing a big red wall at `distance` in front of a camera with fog from 10 to 20.
    private static Vector4 WallAt(float distance, Fog? fog)
    {
        var camera = new Camera { Position = Vector3.Zero, Target = -Vector3.UnitZ, FieldOfView = MathF.PI / 2, Near = 0.5f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1), Fog = fog };
        renderer.Clear(0);
        var r = distance * 4;
        renderer.DrawTriangle(
            new Vertex(new Vector3(-r, -r, -distance), Red),
            new Vertex(new Vector3(r, -r, -distance), Red),
            new Vertex(new Vector3(0, r, -distance), Red),
            Matrix4x4.Identity);
        return Colors.Unpack(renderer.Target[Size / 2, Size / 2]);
    }

    [Fact]
    public void Fog_LeavesNearThingsAlone_HidesFarOnes_AndBlendsBetween()
    {
        var fog = new Fog(Blue, Start: 10, End: 20);

        WallAt(5, fog).Should().Be(Red);
        WallAt(30, fog).Should().Be(Blue);
        var middle = WallAt(15, fog);
        middle.X.Should().BeApproximately(0.5f, 0.02f);
        middle.Z.Should().BeApproximately(0.5f, 0.02f);
    }

    [Fact]
    public void NoFog_DrawsFarThingsAsThemselves()
    {
        WallAt(30, null).Should().Be(Red);
    }
}

public class ModelTests
{
    [Fact]
    public void FromPicture_ExtrudesTheOpaquePixels_StandingOnTheirLowestRow()
    {
        // A 4x4 picture with an opaque 2x2 block in the middle.
        var pixels = new uint[16];
        foreach (var i in new[] { 5, 6, 9, 10 })
        {
            pixels[i] = Colors.Pack(new Vector4(1, 0, 0, 1));
        }

        var mesh = Models.FromPicture(new Texture(4, 4, pixels), pixelSize: 0.5f, depth: 2);

        mesh.Bounds.Min.Should().Be(new Vector3(-0.5f, 0, -1));
        mesh.Bounds.Max.Should().Be(new Vector3(0.5f, 1, 1));
        Enumerable.Range(0, mesh.Positions.Count).Where(i => mesh.Normals[i] == Vector3.UnitZ).Select(i => mesh.UVs[i])
            .Should().OnlyContain(uv => uv.X >= 0.25f - 1e-5f && uv.X <= 0.75f + 1e-5f && uv.Y >= 0.25f - 1e-5f && uv.Y <= 0.75f + 1e-5f);
    }

    [Fact]
    public void FromPicture_OfAnEmptyPicture_IsEmpty()
    {
        Models.FromPicture(new Texture(2, 2, new uint[4]), 1, 1).Triangles.Should().BeEmpty();
    }
}
