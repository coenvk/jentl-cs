using System.Numerics;
using Jentl.Core.Raster;
using Jentl.Geometry;

namespace Jentl.Core.Tests;

public class GpuMeshDataTests
{
    [Fact]
    public void Smooth_SharesVertices_AndKeepsTheIndices()
    {
        var mesh = MeshBuilder.Sphere(1, 8, 4);

        var (vertices, indices) = GpuMeshData.Build(mesh, flat: false);

        vertices.Should().HaveCount(mesh.Positions.Count);
        indices.Should().HaveCount(mesh.Triangles.Count * 3);
        vertices[3].Normal.X.Should().BeApproximately(mesh.Normals[3].X, 1e-6f);
    }

    [Fact]
    public void Flat_GivesEachTriangleItsOwnCornersFacingTheFace()
    {
        var mesh = MeshBuilder.Sphere(1, 8, 4);

        var (vertices, indices) = GpuMeshData.Build(mesh, flat: true);

        vertices.Should().HaveCount(mesh.Triangles.Count * 3);
        indices.Should().BeEmpty();
        var face = mesh.TriangleAt(mesh.Triangles[0]).Normal;
        vertices[0].Normal.X.Should().BeApproximately(face.X, 1e-5f);
        vertices[1].Normal.Should().Be(vertices[0].Normal);
    }

    [Fact]
    public void Build_KeepsTextureCoordinates_WhenTheMeshHasThem()
    {
        var plane = MeshBuilder.Plane(2, 2);

        var (vertices, _) = GpuMeshData.Build(plane, flat: false);

        vertices.Select(v => v.TextureCoordinate.X).Should().Contain(1);
    }
}

public class MatrixConversionTests
{
    [Fact]
    public void ToXna_KeepsEveryElement_SoTransformsAgree()
    {
        var m = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(1, 2, 3) * Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, 0.1f, 50);

        var xna = Convert3D.ToXna(m);

        var a = Vector4.Transform(new Vector4(0.3f, -2, -5, 1), m);
        var b = Microsoft.Xna.Framework.Vector4.Transform(new Microsoft.Xna.Framework.Vector4(0.3f, -2, -5, 1), xna);
        b.X.Should().BeApproximately(a.X, 1e-5f);
        b.W.Should().BeApproximately(a.W, 1e-5f);
    }
}
