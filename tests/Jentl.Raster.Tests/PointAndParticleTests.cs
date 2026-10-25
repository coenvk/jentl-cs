using System.Numerics;

namespace Jentl.Raster.Tests;

public class PointTests
{
    private const int Size = 32;
    private static readonly uint Black = Colors.Pack(new Vector4(0, 0, 0, 1));

    // A camera at the origin looking down -Z, 90° field of view, square.
    private static Renderer Perspective()
    {
        var camera = new Camera { Position = Vector3.Zero, Target = -Vector3.UnitZ, FieldOfView = MathF.PI / 2, Near = 0.5f, Far = 100 };
        var renderer = new Renderer(new FrameBuffer(Size, Size)) { ViewProjection = camera.ViewProjection(1) };
        renderer.Clear(Black);
        return renderer;
    }

    private static int Lit(Renderer renderer) => renderer.Target.Pixels.Count(p => p != Black);

    [Fact]
    public void Point_IsASquareFacingTheCamera_SmallerFurtherAway()
    {
        // At distance 4 the view is 8 units across, 4 pixels to the unit: a point 1 across covers 4 by 4 pixels.
        var near = Perspective();
        near.DrawPoint(new Vector3(0, 0, -4), 1, Vector4.One);
        var far = Perspective();
        far.DrawPoint(new Vector3(0, 0, -8), 1, Vector4.One);

        Lit(near).Should().Be(16);
        Lit(far).Should().Be(4);
        Colors.Unpack(near.Target[Size / 2, Size / 2]).Should().Be(Vector4.One);
    }

    [Fact]
    public void Point_BlendsByItsAlpha_AndHidesBehindNearerThings()
    {
        var renderer = Perspective();
        renderer.DrawPoint(new Vector3(0, 0, -4), 1, new Vector4(1, 1, 1, 0.5f));
        var blended = Colors.Unpack(renderer.Target[Size / 2, Size / 2]);
        blended.X.Should().BeApproximately(0.5f, 0.01f);
        blended.W.Should().Be(1);

        // A wall nearer than the point covers it; the point writes no depth, so a later farther one is not hidden by it.
        var hidden = Perspective();
        var red = new Vector4(1, 0, 0, 1);
        hidden.DrawTriangle(new Vertex(new Vector3(-9, -9, -2), red), new Vertex(new Vector3(9, -9, -2), red), new Vertex(new Vector3(0, 9, -2), red), Matrix4x4.Identity);
        hidden.DrawPoint(new Vector3(0, 0, -4), 1, Vector4.One);
        Colors.Unpack(hidden.Target[Size / 2, Size / 2]).Should().Be(red);
    }

    [Fact]
    public void Point_BehindTheCamera_DrawsNothing()
    {
        var renderer = Perspective();
        renderer.DrawPoint(new Vector3(0, 0, 4), 1, Vector4.One);
        Lit(renderer).Should().Be(0);
    }
}

public class ParticleTests
{
    [Fact]
    public void Particles_MoveFallAndExpire()
    {
        var particles = new Particles(capacity: 4) { Gravity = 10 };
        particles.Emit(Vector3.Zero, new Vector3(2, 0, 0), life: 1, size: 0.5f, new Vector4(1, 1, 1, 1));

        particles.Update(0.5f);

        var p = particles.Live.Should().ContainSingle().Subject;
        p.Position.X.Should().BeApproximately(1, 1e-4f);
        p.Position.Y.Should().BeLessThan(0);
        p.Alpha.Should().BeApproximately(0.5f, 1e-4f, "it fades over its life");

        particles.Update(0.6f);
        particles.Live.Should().BeEmpty();
    }

    [Fact]
    public void Particles_BeyondCapacity_ReplaceTheOldest()
    {
        var particles = new Particles(capacity: 2);
        for (var i = 0; i < 3; i++)
        {
            particles.Emit(new Vector3(i, 0, 0), Vector3.Zero, 1, 1, Vector4.One);
        }

        particles.Live.Select(p => p.Position.X).Should().BeEquivalentTo([1f, 2f]);
    }

    [Fact]
    public void Particles_SlowWithDrag_AndStayOnTheGround()
    {
        var particles = new Particles(capacity: 1) { Drag = 2, Gravity = 10, Floor = 0 };
        particles.Emit(new Vector3(0, 0.1f, 0), new Vector3(4, 0, 0), 5, 1, Vector4.One);

        particles.Update(1);

        var p = particles.Live.Should().ContainSingle().Subject;
        p.Position.X.Should().BeLessThan(4);
        p.Position.Y.Should().Be(0);
    }
}
