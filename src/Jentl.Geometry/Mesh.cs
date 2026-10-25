using System.Numerics;

namespace Jentl.Geometry;

/// <summary>
/// A triangle mesh: vertex positions, a normal per vertex, optional texture coordinates, and triangles that index the
/// vertices, counter-clockwise seen from outside. Immutable; <see cref="Transform"/> makes a moved copy.
/// </summary>
public sealed class Mesh
{
    /// <param name="positions">Where the vertices are.</param>
    /// <param name="triangles">The triangles, counter-clockwise from the front.</param>
    /// <param name="normals">A normal per vertex; worked out from the faces (area-weighted) when left out.</param>
    /// <param name="uvs">Texture coordinates per vertex, or none.</param>
    public Mesh(IReadOnlyList<Vector3> positions, IReadOnlyList<TriangleIndices> triangles, IReadOnlyList<Vector3>? normals = null, IReadOnlyList<Vector2>? uvs = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Any(t => Outside(t.A) || Outside(t.B) || Outside(t.C)))
        {
            throw new ArgumentException("A triangle points at a vertex that isn't there.", nameof(triangles));
        }

        if (normals is not null && normals.Count != positions.Count)
        {
            throw new ArgumentException("There must be one normal per vertex.", nameof(normals));
        }

        if (uvs is not null && uvs.Count != 0 && uvs.Count != positions.Count)
        {
            throw new ArgumentException("There must be one texture coordinate per vertex, or none.", nameof(uvs));
        }

        Positions = positions;
        Triangles = triangles;
        Normals = normals ?? FaceNormals(positions, triangles);
        UVs = uvs ?? [];
        Bounds = positions.Count > 0 ? Box3.FromPoints(positions) : default;

        bool Outside(int index) => index < 0 || index >= positions.Count;
    }

    public IReadOnlyList<Vector3> Positions { get; }

    public IReadOnlyList<Vector3> Normals { get; }

    /// <summary>Texture coordinates, one per vertex, or empty.</summary>
    public IReadOnlyList<Vector2> UVs { get; }

    public IReadOnlyList<TriangleIndices> Triangles { get; }

    public Box3 Bounds { get; }

    /// <summary>The corners of one of the mesh's triangles.</summary>
    public Triangle TriangleAt(TriangleIndices t) => new(Positions[t.A], Positions[t.B], Positions[t.C]);

    /// <summary>A copy moved by <paramref name="matrix"/>; normals turn with it (by the inverse transpose) and stay unit length.</summary>
    public Mesh Transform(Matrix4x4 matrix)
    {
        Matrix4x4.Invert(matrix, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        return new Mesh(
            [.. Positions.Select(p => Vector3.Transform(p, matrix))],
            Triangles,
            [.. Normals.Select(n => SafeNormalize(Vector3.TransformNormal(n, normalMatrix)))],
            UVs);
    }

    private static Vector3[] FaceNormals(IReadOnlyList<Vector3> positions, IReadOnlyList<TriangleIndices> triangles)
    {
        var sums = new Vector3[positions.Count];
        foreach (var t in triangles)
        {
            // The cross product's length is twice the area, so bigger faces count for more.
            var n = Vector3.Cross(positions[t.B] - positions[t.A], positions[t.C] - positions[t.A]);
            sums[t.A] += n;
            sums[t.B] += n;
            sums[t.C] += n;
        }

        return [.. sums.Select(SafeNormalize)];
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 0 ? Vector3.Normalize(v) : Vector3.Zero;
}

/// <summary>Ready-made meshes, centred on the origin, y up, faces outwards.</summary>
public static class MeshBuilder
{
    /// <summary>A box of <paramref name="size"/>, with flat faces (four vertices each) and texture coordinates per face.</summary>
    public static Mesh Box(Vector3 size)
    {
        var h = size / 2;
        var builder = new Builder();
        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        [
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
        ];
        foreach (var (normal, u, v) in faces)
        {
            var centre = normal * h;
            var du = u * h;
            var dv = v * h;
            builder.Quad(centre - du - dv, centre + du - dv, centre + du + dv, centre - du + dv, normal);
        }

        return builder.Build();
    }

    /// <summary>A flat grid in the XZ plane facing +Y, <paramref name="columns"/> by <paramref name="rows"/> squares, with texture coordinates over the whole.</summary>
    public static Mesh Plane(float width, float depth, int columns = 1, int rows = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (var r = 0; r <= rows; r++)
        {
            for (var c = 0; c <= columns; c++)
            {
                var (u, v) = (c / (float)columns, r / (float)rows);
                positions.Add(new Vector3((u - 0.5f) * width, 0, (v - 0.5f) * depth));
                uvs.Add(new Vector2(u, v));
            }
        }

        var triangles = new List<TriangleIndices>();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var i = (r * (columns + 1)) + c;
                var (a, b, d, e) = (i, i + 1, i + columns + 1, i + columns + 2);
                triangles.Add(new TriangleIndices(a, d, b));
                triangles.Add(new TriangleIndices(b, d, e));
            }
        }

        return new Mesh(positions, triangles, [.. positions.Select(_ => Vector3.UnitY)], uvs);
    }

    /// <summary>A sphere of <paramref name="radius"/>: <paramref name="segments"/> around, <paramref name="rings"/> from pole to pole, smooth normals.</summary>
    public static Mesh Sphere(float radius, int segments = 16, int rings = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(rings, 2);
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (var r = 0; r <= rings; r++)
        {
            var polar = MathF.PI * r / rings;
            for (var s = 0; s <= segments; s++)
            {
                var azimuth = MathF.Tau * s / segments;
                var unit = new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(azimuth));
                positions.Add(unit * radius);
                uvs.Add(new Vector2(s / (float)segments, r / (float)rings));
            }
        }

        var triangles = new List<TriangleIndices>();
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                var (a, b) = ((r * (segments + 1)) + s, (r * (segments + 1)) + s + 1);
                var (c, d) = (a + segments + 1, b + segments + 1);
                if (r > 0)
                {
                    triangles.Add(Outward(positions, a, b, c));
                }

                if (r < rings - 1)
                {
                    triangles.Add(Outward(positions, b, d, c));
                }
            }
        }

        return new Mesh(positions, triangles, [.. positions.Select(Vector3.Normalize)], uvs);
    }

    /// <summary>An upright cylinder of <paramref name="radius"/> and <paramref name="height"/> with flat caps; smooth sides.</summary>
    public static Mesh Cylinder(float radius, float height, int segments = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        var builder = new Builder();
        var (top, bottom) = (height / 2, -height / 2);
        for (var s = 0; s < segments; s++)
        {
            var (a0, a1) = (MathF.Tau * s / segments, MathF.Tau * (s + 1) / segments);
            Vector3 Rim(float angle, float y) => new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
            var (n0, n1) = (Vector3.Normalize(Rim(a0, 0)), Vector3.Normalize(Rim(a1, 0)));
            var (u0, u1) = (s / (float)segments, (s + 1) / (float)segments);
            builder.Smooth([(Rim(a0, bottom), n0), (Rim(a1, bottom), n1), (Rim(a1, top), n1), (Rim(a0, top), n0)], [new(u0, 1), new(u1, 1), new(u1, 0), new(u0, 0)]);
            Vector2 Cap(float angle) => new((MathF.Cos(angle) + 1) / 2, (MathF.Sin(angle) + 1) / 2);
            builder.Triangle(new Vector3(0, top, 0), Rim(a0, top), Rim(a1, top), Vector3.UnitY, [new(0.5f, 0.5f), Cap(a0), Cap(a1)]);
            builder.Triangle(new Vector3(0, bottom, 0), Rim(a1, bottom), Rim(a0, bottom), -Vector3.UnitY, [new(0.5f, 0.5f), Cap(a1), Cap(a0)]);
        }

        return builder.Build();
    }

    /// <summary>
    /// A prism from a 2D outline (any simple polygon, either winding) in the XY plane, <paramref name="depth"/> deep
    /// along Z and centred on it: a flat cap at each end and flat sides. A side view becomes a low-poly model this way.
    /// </summary>
    public static Mesh Extrude(IReadOnlyList<Vector2> outline, float depth)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var points = Polygon2.IsCounterClockwise(outline) ? outline.ToList() : outline.Reverse().ToList();
        var (front, back) = (depth / 2, -depth / 2);
        var builder = new Builder();

        // The caps take the texture across the outline's bounds, upright (v runs down), as a side view would be drawn.
        var bounds = Box2.FromPoints(points);
        var span = Vector2.Max(bounds.Size, new Vector2(1e-6f));
        Vector2 Uv(int i) => new((points[i].X - bounds.Min.X) / span.X, 1 - ((points[i].Y - bounds.Min.Y) / span.Y));
        foreach (var t in Polygon2.Triangulate(points))
        {
            Vector3 At(int i, float z) => new(points[i].X, points[i].Y, z);
            builder.Triangle(At(t.A, front), At(t.B, front), At(t.C, front), Vector3.UnitZ, [Uv(t.A), Uv(t.B), Uv(t.C)]);
            builder.Triangle(At(t.A, back), At(t.C, back), At(t.B, back), -Vector3.UnitZ, [Uv(t.A), Uv(t.C), Uv(t.B)]);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var (p, q) = (points[i], points[(i + 1) % points.Count]);
            var d = q - p;
            var outward = Vector3.Normalize(new Vector3(d.Y, -d.X, 0));
            builder.Quad(new Vector3(p, back), new Vector3(q, back), new Vector3(q, front), new Vector3(p, front), outward);
        }

        return builder.Build();
    }

    // Orders a triangle so it faces away from the origin.
    private static TriangleIndices Outward(List<Vector3> positions, int a, int b, int c)
    {
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        var centre = (positions[a] + positions[b] + positions[c]) / 3;
        return Vector3.Dot(normal, centre) >= 0 ? new TriangleIndices(a, b, c) : new TriangleIndices(a, c, b);
    }

    // Collects flat or smooth faces, turning each so it faces the way it should.
    private sealed class Builder
    {
        private readonly List<Vector3> _positions = [];
        // A quad's corners run bottom-left, bottom-right, top-right, top-left: the whole texture, upright.
        private static readonly Vector2[] QuadUVs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        private static readonly Vector2[] TriangleUVs = [new(0, 1), new(1, 1), new(0.5f, 0)];

        private readonly List<Vector3> _normals = [];
        private readonly List<Vector2> _uvs = [];
        private readonly List<TriangleIndices> _triangles = [];

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Vector2[]? uvs = null) =>
            Add([(a, facing), (b, facing), (c, facing)], facing, uvs ?? TriangleUVs);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Vector2[]? uvs = null) =>
            Add([(a, facing), (b, facing), (c, facing), (d, facing)], facing, uvs ?? QuadUVs);

        /// <summary>A quad with its own normal per corner; it faces the way its corners' normals average.</summary>
        public void Smooth((Vector3 Position, Vector3 Normal)[] corners, Vector2[] uvs) =>
            Add(corners, corners.Aggregate(Vector3.Zero, (sum, c) => sum + c.Normal), uvs);

        public Mesh Build() => new(_positions, _triangles, _normals, _uvs);

        // A fan of triangles over the corners (a triangle or a quad), flipped if it would face away from `facing`.
        private void Add((Vector3 Position, Vector3 Normal)[] corners, Vector3 facing, Vector2[] uvs)
        {
            var first = _positions.Count;
            for (var i = 0; i < corners.Length; i++)
            {
                _positions.Add(corners[i].Position);
                _normals.Add(corners[i].Normal);
                _uvs.Add(uvs[i]);
            }

            var flip = Vector3.Dot(Vector3.Cross(corners[1].Position - corners[0].Position, corners[2].Position - corners[0].Position), facing) < 0;
            for (var i = 1; i < corners.Length - 1; i++)
            {
                _triangles.Add(flip ? new TriangleIndices(first, first + i + 1, first + i) : new TriangleIndices(first, first + i, first + i + 1));
            }
        }
    }
}
