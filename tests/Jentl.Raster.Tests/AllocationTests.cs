using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class AllocationTests
{
    // A frame like a game's: a textured, lit mesh cut by the view's edges and the camera's plane, fog, and particles.
    private static void Frame(Renderer renderer, Mesh ground, Mesh ball, Texture texture, Particles particles)
    {
        renderer.Clear(0);
        renderer.DrawMesh(ground, Matrix4x4.Identity, new Material(Vector4.One, Shading.Flat, texture));
        renderer.DrawMesh(ball, Matrix4x4.CreateTranslation(0, 1, -3), new Material(Vector4.One, Shading.Smooth));
        particles.Update(1 / 60f);
        foreach (var p in particles.Live)
        {
            renderer.DrawPoint(p.Position, p.Size, p.Color with { W = p.Alpha });
        }
    }

    [Fact]
    public void AFrame_OnceWarmedUp_MakesNoGarbage()
    {
        var camera = new Camera { Position = new Vector3(0, 1, 2), Target = new Vector3(0, 0.5f, -5), Near = 0.3f, Far = 60 };
        var renderer = new Renderer(new FrameBuffer(160, 90)) { ViewProjection = camera.ViewProjection(16 / 9f), Fog = new Fog(Vector4.One, 10, 50) };
        var ground = MeshBuilder.Plane(200, 200, 4, 4);
        var ball = MeshBuilder.Sphere(1, 8, 4);
        var texture = new Texture(8, 8, new uint[64]);
        Array.Fill(texture.Pixels, 0xFF336699u);
        var particles = new Particles(64);
        for (var i = 0; i < 64; i++)
        {
            particles.Emit(new Vector3(i % 8, 0.5f, -i / 8f), Vector3.UnitY, 100, 0.3f, Vector4.One);
        }

        Frame(renderer, ground, ball, texture, particles);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Frame(renderer, ground, ball, texture, particles);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        renderer.Stats.Triangles.Should().BeGreaterThan(0);
        allocated.Should().BeLessThan(1000, "a frame should leave next to nothing for the garbage collector (materials made here aside)");
    }
}
