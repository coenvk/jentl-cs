using System.Numerics;

namespace Jentl.Geometry.Tests;

public class ClippingTests
{
    [Fact]
    public void ClipPolygon_KeepsThePositiveSide_AndInterpolates()
    {
        Vector2[] square = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];

        var kept = Clipping.ClipPolygon(square, p => p.X, Vector2.Lerp);

        kept.Should().HaveCount(4);
        Polygon2.SignedArea([.. kept]).Should().BeApproximately(2, 1e-5f);
        kept.Should().OnlyContain(p => p.X >= -1e-6f);
    }

    [Fact]
    public void ClipPolygon_AllInside_Unchanged_AllOutside_Empty()
    {
        Vector2[] triangle = [new(1, 0), new(3, 0), new(2, 2)];

        Clipping.ClipPolygon(triangle, p => p.X, Vector2.Lerp).Should().Equal(triangle);
        Clipping.ClipPolygon(triangle, p => -p.X, Vector2.Lerp).Should().BeEmpty();
    }

    [Fact]
    public void ClipPolygon_CuttingACorner_AddsAVertex()
    {
        Vector2[] triangle = [new(0, 0), new(4, 0), new(0, 4)];

        var kept = Clipping.ClipPolygon(triangle, p => 1 - p.X, Vector2.Lerp);

        kept.Should().HaveCount(4);
        Polygon2.SignedArea([.. kept]).Should().BeApproximately(3.5f, 1e-5f);
    }

    [Fact]
    public void ClipPolygon_CarriesAnyVertexData()
    {
        (Vector3 Position, float Shade)[] polygon = [(new(0, 0, 0), 0), (new(2, 0, 0), 1), (new(0, 2, 0), 0)];

        var kept = Clipping.ClipPolygon(polygon, v => 1 - v.Position.X, (a, b, t) => (Vector3.Lerp(a.Position, b.Position, t), a.Shade + ((b.Shade - a.Shade) * t)));

        kept.Should().Contain(v => v.Position.X == 1 && Math.Abs(v.Shade - 0.5f) < 1e-6f);
    }
}

public class EdgeTests
{
    [Fact]
    public void Edge_SignTellsTheSide_AndSteps()
    {
        var edge = new Edge(new Vector2(0, 0), new Vector2(10, 0));

        edge.Evaluate(new Vector2(5, 3)).Should().BeGreaterThan(0);
        edge.Evaluate(new Vector2(5, -3)).Should().BeLessThan(0);
        edge.Evaluate(new Vector2(7, 0)).Should().Be(0);
        (edge.Evaluate(new Vector2(6, 3)) - edge.Evaluate(new Vector2(5, 3))).Should().Be(edge.StepX);
        (edge.Evaluate(new Vector2(5, 4)) - edge.Evaluate(new Vector2(5, 3))).Should().Be(edge.StepY);
    }

    [Fact]
    public void Edge_TopLeftRule_SharedEdgesBelongToOneTriangle()
    {
        // Two triangles sharing the diagonal of a square: every pixel centre on the diagonal must be filled exactly once.
        Vector2 a = new(0, 0), b = new(4, 0), c = new(4, 4), d = new(0, 4);
        (Vector2, Vector2, Vector2)[] triangles = [(a, b, c), (a, c, d)];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                triangles.Count(t => Edge.Covers(t.Item1, t.Item2, t.Item3, p)).Should().Be(1, $"pixel {x},{y}");
            }
        }
    }
}

public class MeshTests
{
    [Fact]
    public void Box_HasClosedOutwardFaces()
    {
        var mesh = MeshBuilder.Box(new Vector3(2, 4, 6));

        mesh.Triangles.Should().HaveCount(12);
        mesh.Bounds.Should().Be(new Box3(new Vector3(-1, -2, -3), new Vector3(1, 2, 3)));
        foreach (var t in mesh.Triangles)
        {
            var triangle = mesh.TriangleAt(t);
            Vector3.Dot(triangle.Normal, triangle.Centroid).Should().BeGreaterThan(0, "faces point outwards");
        }
    }

    [Fact]
    public void Sphere_PointsOnTheRadius_NormalsOutward()
    {
        var mesh = MeshBuilder.Sphere(2, segments: 12, rings: 8);

        mesh.Positions.Should().OnlyContain(p => Math.Abs(p.Length() - 2) < 1e-4f);
        mesh.Normals.Zip(mesh.Positions).Should().OnlyContain(pair => Vector3.Dot(pair.First, pair.Second) > 0);
        mesh.Triangles.Select(t => mesh.TriangleAt(t)).Where(t => t.Area > 1e-6f)
            .Should().OnlyContain(t => Vector3.Dot(t.Normal, t.Centroid) > 0);
    }

    [Fact]
    public void Cylinder_And_Plane_HaveTheirShape()
    {
        var cylinder = MeshBuilder.Cylinder(1, 3, segments: 16);
        cylinder.Bounds.Max.Y.Should().BeApproximately(1.5f, 1e-5f);
        cylinder.Triangles.Select(t => cylinder.TriangleAt(t)).Should().OnlyContain(t => Vector3.Dot(t.Normal, t.Centroid) > 0);

        var plane = MeshBuilder.Plane(10, 4, columns: 5, rows: 2);
        plane.Triangles.Should().HaveCount(5 * 2 * 2);
        plane.Triangles.Select(t => plane.TriangleAt(t)).Should().OnlyContain(t => t.Normal == Vector3.UnitY);
        plane.UVs.Should().HaveCount(plane.Positions.Count);
    }

    [Fact]
    public void Extrude_MakesAPrismFromAnOutline()
    {
        Vector2[] l = [new(0, 0), new(4, 0), new(4, 2), new(2, 2), new(2, 4), new(0, 4)];

        var mesh = MeshBuilder.Extrude(l, depth: 2);

        mesh.Bounds.Should().Be(new Box3(new Vector3(0, 0, -1), new Vector3(4, 4, 1)));
        mesh.Triangles.Should().HaveCount((2 * 4) + (6 * 2)); // two caps of four triangles, six sides of two
        mesh.Triangles.Sum(t => mesh.TriangleAt(t).Area).Should().BeApproximately((2 * 12) + (2 * 16), 1e-3f); // caps + sides (perimeter 16)
        mesh.Triangles.Select(t => mesh.TriangleAt(t))
            .Where(t => Math.Abs(t.Normal.Z) > 0.5f)
            .Should().OnlyContain(t => Math.Sign(t.Normal.Z) == Math.Sign(t.Centroid.Z));
    }

    [Fact]
    public void Transform_MovesPositions_AndTurnsNormals()
    {
        var mesh = MeshBuilder.Box(Vector3.One).Transform(Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(0, 5, 0));

        mesh.Bounds.Min.Y.Should().BeApproximately(4.5f, 1e-5f);
        mesh.Normals.Should().OnlyContain(n => Math.Abs(n.Length() - 1) < 1e-5f);
    }

    [Fact]
    public void Mesh_RejectsIndicesOutOfRange()
    {
        var make = () => new Mesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [new TriangleIndices(0, 1, 3)]);

        make.Should().Throw<ArgumentException>();
    }
}
