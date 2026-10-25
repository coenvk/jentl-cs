using System.Numerics;

namespace Jentl.Geometry.Tests;

public class ShapeTests
{
    private static readonly Vector2[] Square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

    // An L: a 4x4 square missing its top-right 2x2 quarter (counter-clockwise, y up).
    private static readonly Vector2[] L = [new(0, 0), new(4, 0), new(4, 2), new(2, 2), new(2, 4), new(0, 4)];

    [Fact]
    public void Polygon_SignedArea_PositiveCounterClockwise()
    {
        Polygon2.SignedArea(Square).Should().Be(16);
        Polygon2.SignedArea([.. Square.Reverse()]).Should().Be(-16);
        Polygon2.IsCounterClockwise(L).Should().BeTrue();
        Polygon2.SignedArea(L).Should().Be(12);
    }

    [Fact]
    public void Polygon_Contains_ConcaveCorner()
    {
        Polygon2.Contains(L, new Vector2(1, 3)).Should().BeTrue();
        Polygon2.Contains(L, new Vector2(3, 3)).Should().BeFalse();
        Polygon2.Contains(L, new Vector2(3, 1)).Should().BeTrue();
        Polygon2.Contains(L, new Vector2(-1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Polygon_Triangulate_CoversTheArea_WithNMinusTwoTriangles()
    {
        var triangles = Polygon2.Triangulate(L);

        triangles.Should().HaveCount(L.Length - 2);
        triangles.Sum(t => Polygon2.SignedArea([L[t.A], L[t.B], L[t.C]])).Should().BeApproximately(12, 1e-4f);
        triangles.Select(t => Polygon2.SignedArea([L[t.A], L[t.B], L[t.C]])).Should().OnlyContain(area => area > 0);
    }

    [Fact]
    public void Polygon_Triangulate_ClockwiseInput_StillGivesCounterClockwiseTriangles()
    {
        Vector2[] clockwise = [.. L.Reverse()];

        var triangles = Polygon2.Triangulate(clockwise);

        triangles.Should().HaveCount(4);
        triangles.Select(t => Polygon2.SignedArea([clockwise[t.A], clockwise[t.B], clockwise[t.C]])).Should().OnlyContain(area => area > 0);
    }

    [Fact]
    public void Box2_ContainsIntersectsUnion()
    {
        var a = new Box2(new Vector2(0, 0), new Vector2(4, 4));
        var b = new Box2(new Vector2(3, 3), new Vector2(6, 5));

        a.Contains(new Vector2(4, 4)).Should().BeTrue();
        a.Intersects(b).Should().BeTrue();
        a.Intersects(new Box2(new Vector2(5, 5), new Vector2(6, 6))).Should().BeFalse();
        a.Union(b).Should().Be(new Box2(Vector2.Zero, new Vector2(6, 5)));
        a.Center.Should().Be(new Vector2(2, 2));
        a.Size.Should().Be(new Vector2(4, 4));
        Box2.FromPoints(Square).Should().Be(a);
    }

    [Fact]
    public void Box3_RayHitsAndMisses()
    {
        var box = new Box3(new Vector3(-1), new Vector3(1));

        box.Intersect(new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ), out var distance).Should().BeTrue();
        distance.Should().BeApproximately(4, 1e-5f);
        box.Intersect(new Ray(new Vector3(3, 0, 5), -Vector3.UnitZ), out _).Should().BeFalse();
        box.Intersect(new Ray(Vector3.Zero, Vector3.UnitX), out distance).Should().BeTrue(); // from inside
        distance.Should().Be(0);
        box.Intersect(new Ray(new Vector3(0, 0, 5), Vector3.UnitZ), out _).Should().BeFalse(); // behind
    }

    [Fact]
    public void Box3_CornersAndTransform()
    {
        var box = new Box3(Vector3.Zero, Vector3.One);

        box.Corners().Should().HaveCount(8).And.Contain(Vector3.One);
        var moved = box.Transform(Matrix4x4.CreateTranslation(2, 0, 0));
        moved.Should().Be(new Box3(new Vector3(2, 0, 0), new Vector3(3, 1, 1)));
        var turned = box.Transform(Matrix4x4.CreateRotationZ(MathF.PI / 2));
        turned.Min.X.Should().BeApproximately(-1, 1e-5f);
        turned.Max.Y.Should().BeApproximately(1, 1e-5f);
    }

    [Fact]
    public void Frustum_SeesWhatIsInFront_OnlyInsideTheView()
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, 1, 100);
        var frustum = Frustum.From(view * projection);

        frustum.Contains(new Vector3(0, 0, -10)).Should().BeTrue();
        frustum.Contains(new Vector3(0, 0, 10)).Should().BeFalse();
        frustum.Contains(new Vector3(0, 0, -200)).Should().BeFalse();
        frustum.Contains(new Vector3(20, 0, -10)).Should().BeFalse();

        frustum.Test(new Box3(new Vector3(-1, -1, -11), new Vector3(1, 1, -9))).Should().Be(Containment.Inside);
        frustum.Test(new Box3(new Vector3(-1, -1, -101), new Vector3(1, 1, -99))).Should().Be(Containment.Intersects);
        frustum.Test(new Box3(new Vector3(-1, -1, 5), new Vector3(1, 1, 6))).Should().Be(Containment.Outside);
    }
}
