using System.Numerics;

namespace Jentl.Geometry.Tests;

public class PrimitiveTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Ray_At_WalksAlongTheDirection() =>
        new Ray(new Vector3(1, 2, 3), new Vector3(0, 0, -2)).At(1.5f).Should().Be(new Vector3(1, 2, 0));

    [Fact]
    public void Planes_ThroughAPoint_MeasureSignedDistance()
    {
        var floor = Planes.Through(new Vector3(0, 2, 0), Vector3.UnitY);

        floor.DistanceTo(new Vector3(5, 7, -1)).Should().BeApproximately(5, Tolerance);
        floor.DistanceTo(new Vector3(0, -1, 0)).Should().BeApproximately(-3, Tolerance);
    }

    [Fact]
    public void Planes_FromTriangle_FaceTheCounterClockwiseSide()
    {
        var plane = Planes.Through(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);

        plane.Normal.Should().Be(Vector3.UnitZ);
    }

    [Fact]
    public void Plane_IntersectsASegmentThatCrossesIt_NotOneThatDoesnt()
    {
        var floor = Planes.Through(Vector3.Zero, Vector3.UnitY);

        floor.Intersect(new Segment3(new Vector3(1, 2, 0), new Vector3(1, -2, 4)), out var at).Should().BeTrue();
        at.X.Should().BeApproximately(1, Tolerance);
        at.Y.Should().BeApproximately(0, Tolerance);
        at.Z.Should().BeApproximately(2, Tolerance);
        floor.Intersect(new Segment3(new Vector3(0, 1, 0), new Vector3(0, 3, 0)), out _).Should().BeFalse();
        floor.Intersect(new Segment3(new Vector3(0, 1, 0), new Vector3(5, 1, 0)), out _).Should().BeFalse();
    }

    [Fact]
    public void Plane_IntersectsARayInFront_NotBehind()
    {
        var wall = Planes.Through(new Vector3(0, 0, -10), Vector3.UnitZ);

        wall.Intersect(new Ray(Vector3.Zero, -Vector3.UnitZ), out var distance).Should().BeTrue();
        distance.Should().BeApproximately(10, Tolerance);
        wall.Intersect(new Ray(Vector3.Zero, Vector3.UnitZ), out _).Should().BeFalse();
    }

    [Fact]
    public void Segment2_Crossing_FindsThePoint()
    {
        var a = new Segment2(new Vector2(0, 0), new Vector2(4, 4));
        var b = new Segment2(new Vector2(0, 4), new Vector2(4, 0));

        a.Intersect(b, out var at).Should().BeTrue();
        at.Should().Be(new Vector2(2, 2));
        a.Intersect(new Segment2(new Vector2(5, 0), new Vector2(9, 0)), out _).Should().BeFalse();
        a.Intersect(new Segment2(new Vector2(1, 0), new Vector2(5, 4)), out _).Should().BeFalse(); // parallel
    }

    [Fact]
    public void Segment_LengthAndClosestPoint()
    {
        var s = new Segment3(Vector3.Zero, new Vector3(10, 0, 0));

        s.Length.Should().Be(10);
        s.ClosestPoint(new Vector3(3, 5, 0)).Should().Be(new Vector3(3, 0, 0));
        s.ClosestPoint(new Vector3(-4, 1, 0)).Should().Be(Vector3.Zero);
        s.ClosestPoint(new Vector3(40, 1, 0)).Should().Be(new Vector3(10, 0, 0));
    }

    [Fact]
    public void Triangle_NormalArea_CounterClockwiseIsFront()
    {
        var t = new Triangle(Vector3.Zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0));

        t.Normal.Should().Be(Vector3.UnitZ);
        t.Area.Should().BeApproximately(2, Tolerance);
        t.Centroid.X.Should().BeApproximately(2 / 3f, Tolerance);
    }

    [Fact]
    public void Triangle_Barycentric_WeighsTheCorners()
    {
        var t = new Triangle(Vector3.Zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0));

        var w = t.Barycentric(new Vector3(0.5f, 0.5f, 0));

        w.X.Should().BeApproximately(0.5f, Tolerance);
        w.Y.Should().BeApproximately(0.25f, Tolerance);
        w.Z.Should().BeApproximately(0.25f, Tolerance);
        t.Contains(new Vector3(0.5f, 0.5f, 0)).Should().BeTrue();
        t.Contains(new Vector3(2, 2, 0)).Should().BeFalse();
    }

    [Fact]
    public void Triangle_RayHit_FrontAndBack_AndMiss()
    {
        var t = new Triangle(new Vector3(-1, -1, -5), new Vector3(1, -1, -5), new Vector3(0, 1, -5));

        t.Intersect(new Ray(Vector3.Zero, -Vector3.UnitZ), out var distance).Should().BeTrue();
        distance.Should().BeApproximately(5, Tolerance);
        t.Intersect(new Ray(new Vector3(5, 0, 0), -Vector3.UnitZ), out _).Should().BeFalse();
        t.Intersect(new Ray(Vector3.Zero, Vector3.UnitZ), out _).Should().BeFalse();
        t.Intersect(new Ray(new Vector3(0, 0, -10), Vector3.UnitZ), out distance).Should().BeTrue(); // from behind
        distance.Should().BeApproximately(5, Tolerance);
    }
}
