using Jentl.Geometry;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace Jentl.Core.Raster;

/// <summary>Between System.Numerics (Jentl's geometry) and XNA (MonoGame and KNI) maths.</summary>
public static class Convert3D
{
    /// <summary>The same matrix in XNA's type (both use row vectors, so the elements map one to one).</summary>
    public static Xna.Matrix ToXna(System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    public static Xna.Vector3 ToXna(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Xna.Vector2 ToXna(System.Numerics.Vector2 v) => new(v.X, v.Y);
}

/// <summary>
/// A <see cref="Mesh"/> as vertices for the GPU: shared vertices and indices for smooth shading, or every triangle with
/// its own three corners facing its face for flat shading (the GPU only knows per-vertex normals).
/// </summary>
public static class GpuMeshData
{
    public static (VertexPositionNormalTexture[] Vertices, int[] Indices) Build(Mesh mesh, bool flat)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var hasUVs = mesh.UVs.Count == mesh.Positions.Count;
        VertexPositionNormalTexture Vertex(int i, System.Numerics.Vector3 normal) =>
            new(Convert3D.ToXna(mesh.Positions[i]), Convert3D.ToXna(normal), hasUVs ? Convert3D.ToXna(mesh.UVs[i]) : Xna.Vector2.Zero);

        if (!flat)
        {
            var vertices = new VertexPositionNormalTexture[mesh.Positions.Count];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vertex(i, mesh.Normals[i]);
            }

            return (vertices, [.. mesh.Triangles.SelectMany(t => new[] { t.A, t.B, t.C })]);
        }

        var faceted = new VertexPositionNormalTexture[mesh.Triangles.Count * 3];
        for (var i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            var normal = mesh.TriangleAt(t).Normal;
            faceted[i * 3] = Vertex(t.A, normal);
            faceted[(i * 3) + 1] = Vertex(t.B, normal);
            faceted[(i * 3) + 2] = Vertex(t.C, normal);
        }

        return (faceted, []);
    }
}
