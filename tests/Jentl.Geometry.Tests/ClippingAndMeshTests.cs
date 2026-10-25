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

public class MeshTextureCoordinateTests
{
    [Fact]
    public void Builders_GiveEveryVertexATextureCoordinate()
    {
        Vector2[] outline = [new(0, 0), new(4, 0), new(4, 2), new(0, 2)];
        foreach (var mesh in new[] { MeshBuilder.Box(Vector3.One), MeshBuilder.Cylinder(1, 2), MeshBuilder.Extrude(outline, 1) })
        {
            mesh.UVs.Should().HaveCount(mesh.Positions.Count);
            mesh.UVs.Should().OnlyContain(uv => uv.X >= 0 && uv.X <= 1 && uv.Y >= 0 && uv.Y <= 1);
        }
    }

    [Fact]
    public void Box_EachFaceSpansTheWholeTexture()
    {
        var box = MeshBuilder.Box(Vector3.One);

        for (var face = 0; face < 6; face++)
        {
            var uvs = box.UVs.Skip(face * 4).Take(4).ToList();
            uvs.Should().Contain(Vector2.Zero).And.Contain(Vector2.One);
        }
    }

    [Fact]
    public void Extrude_CapsAreMappedAcrossTheOutline()
    {
        Vector2[] outline = [new(0, 0), new(4, 0), new(4, 2), new(0, 2)];

        var mesh = MeshBuilder.Extrude(outline, 1);

        var corner = mesh.Positions.Select((p, i) => (p, i)).First(x => x.p == new Vector3(4, 2, 0.5f)).i;
        mesh.UVs[corner].Should().Be(new Vector2(1, 0)); // right, top (v runs down the image)
    }
}

public class ExtrudeSideTests
{
    [Fact]
    public void Extrude_SidesTakeTheColoursAlongTheirEdge()
    {
        Vector2[] outline = [new(0, 0), new(4, 0), new(4, 2), new(0, 2)];

        var mesh = MeshBuilder.Extrude(outline, 1);

        // Every vertex of a side (normal across the outline) has the cap's texture coordinate for its corner of the outline,
        // nudged a hair inside it.
        Vector2 CapUv(Vector3 p) => new(p.X / 4, 1 - (p.Y / 2));
        var sides = Enumerable.Range(0, mesh.Positions.Count).Where(i => Math.Abs(mesh.Normals[i].Z) < 0.5f).ToList();
        sides.Should().NotBeEmpty();
        sides.Select(i => Vector2.Distance(mesh.UVs[i], CapUv(mesh.Positions[i]))).Should().OnlyContain(d => d < 1e-3f);
    }
}

public class MoreMeshTests
{
    [Fact]
    public void Extrude_CanMapTheCapsAcrossAGivenPicture()
    {
        // A 2x2 square in the middle of a 4x4 picture takes the middle of the texture.
        Vector2[] outline = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];

        var mesh = MeshBuilder.Extrude(outline, 1, picture: new Box2(Vector2.Zero, new Vector2(4)));

        var front = Enumerable.Range(0, mesh.Positions.Count).Where(i => mesh.Normals[i] == Vector3.UnitZ).ToList();
        front.Select(i => mesh.UVs[i]).Should().OnlyContain(uv => uv.X >= 0.25f - 1e-5f && uv.X <= 0.75f + 1e-5f && uv.Y >= 0.25f - 1e-5f && uv.Y <= 0.75f + 1e-5f);
        front.Should().Contain(i => mesh.Positions[i].X == 1 && mesh.Positions[i].Y == 3 && Vector2.Distance(mesh.UVs[i], new Vector2(0.25f, 0.25f)) < 1e-5f);
    }

    [Fact]
    public void Extrude_SidesReadTheirColoursJustInsideTheOutline()
    {
        Vector2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

        // Strictly inside by default (so nearest sampling never lands on the texel beyond the edge), and as deep as asked.
        foreach (var (inset, low) in new[] { (0f, 0f), (1f, 0.25f) })
        {
            var mesh = MeshBuilder.Extrude(square, 1, sideInset: inset);
            var sides = Enumerable.Range(0, mesh.Positions.Count).Where(i => mesh.Normals[i].Z == 0).Select(i => mesh.UVs[i]).ToList();
            sides.Should().NotBeEmpty();
            sides.Should().OnlyContain(uv => uv.X > low - 1e-5f && uv.X < 1 - low + 1e-5f && uv.Y > low - 1e-5f && uv.Y < 1 - low + 1e-5f);
            sides.Should().OnlyContain(uv => uv.X > 0 && uv.X < 1 && uv.Y > 0 && uv.Y < 1);
        }
    }

    [Fact]
    public void Cone_StandsOnItsBase_PointUp()
    {
        var cone = MeshBuilder.Cone(1, 2, segments: 8);

        cone.Bounds.Min.Y.Should().BeApproximately(-1, 1e-5f);
        cone.Bounds.Max.Y.Should().BeApproximately(1, 1e-5f);
        cone.Triangles.Should().HaveCount(16);
        cone.Triangles.Select(t => cone.TriangleAt(t)).Should().OnlyContain(t => Vector3.Dot(t.Normal, t.Centroid) > 0);
        cone.UVs.Should().HaveCount(cone.Positions.Count);
    }
}

public class BillboardTests
{
    [Fact]
    public void Quad_StandsUpright_OnItsBottomMiddle_FacingZ()
    {
        var quad = MeshBuilder.Quad();

        quad.Bounds.Should().Be(new Box3(new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 1, 0)));
        quad.Triangles.Select(t => quad.TriangleAt(t).Normal).Should().OnlyContain(n => n == Vector3.UnitZ);
        quad.UVs.Should().Contain(new Vector2(0, 0)).And.Contain(new Vector2(1, 1));
    }

    [Fact]
    public void Billboard_TurnsTheQuadToFaceTheEye_StayingUpright()
    {
        var world = Billboard.Upright(new Vector3(5, 0, 5), eye: new Vector3(5, 3, 15), width: 2, height: 4);
        var quad = MeshBuilder.Quad().Transform(world);

        quad.Bounds.Min.Y.Should().BeApproximately(0, 1e-5f);
        quad.Bounds.Max.Y.Should().BeApproximately(4, 1e-5f);
        quad.Bounds.Size.X.Should().BeApproximately(2, 1e-4f);
        quad.TriangleAt(quad.Triangles[0]).Normal.Z.Should().BeApproximately(1, 1e-4f, "the eye is straight down +z");

        var side = Billboard.Upright(Vector3.Zero, eye: new Vector3(-10, 0, 0), width: 1, height: 1);
        Vector3.TransformNormal(Vector3.UnitZ, side).X.Should().BeApproximately(-1, 1e-4f);
    }
}

public class CombineAndInsetTests
{
    [Fact]
    public void Combine_JoinsPlacedMeshesIntoOne()
    {
        var box = MeshBuilder.Box(Vector3.One);

        var both = MeshBuilder.Combine([(box, Matrix4x4.Identity), (box, Matrix4x4.CreateTranslation(3, 0, 0))]);

        both.Triangles.Should().HaveCount(box.Triangles.Count * 2);
        both.Positions.Should().HaveCount(box.Positions.Count * 2);
        both.UVs.Should().HaveCount(both.Positions.Count);
        both.Bounds.Should().Be(new Box3(new Vector3(-0.5f), new Vector3(3.5f, 0.5f, 0.5f)));
        both.Triangles.Select(t => both.TriangleAt(t)).Should().OnlyContain(t => t.Area > 0);
    }

    [Fact]
    public void Combine_GivesMeshesWithoutTextureCoordinatesZeroes()
    {
        var bare = new Mesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [new TriangleIndices(0, 1, 2)]);

        var both = MeshBuilder.Combine([(bare, Matrix4x4.Identity), (MeshBuilder.Quad(), Matrix4x4.Identity)]);

        both.UVs.Should().HaveCount(both.Positions.Count);
        both.UVs.Take(3).Should().OnlyContain(uv => uv == Vector2.Zero);
    }

    [Fact]
    public void Inset_ShrinksAConvexPolygonByTheDistance()
    {
        Vector2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

        Polygon2.Inset(square, 1).Should().BeEquivalentTo([new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 3), new Vector2(1, 3)]);
        Polygon2.Inset([.. square.Reverse()], 1).Should().BeEquivalentTo([new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 3), new Vector2(1, 3)]);

        // A trapezoid (a car's side window): its slanted sides move in by the distance too.
        Vector2[] trapezoid = [new(0, 0), new(6, 0), new(4, 2), new(2, 2)];
        var inset = Polygon2.Inset(trapezoid, 0.5f);
        Math.Abs(Polygon2.SignedArea(inset)).Should().BeLessThan(Math.Abs(Polygon2.SignedArea(trapezoid)));
        inset.Should().OnlyContain(p => Polygon2.Contains(trapezoid, p));
    }
}
