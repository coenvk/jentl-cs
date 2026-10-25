using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster;

/// <summary>A corner of a triangle to draw: where it is (before the world transform) and its colour.</summary>
public readonly record struct Vertex(Vector3 Position, Vector4 Color);

/// <summary>How a mesh's surface takes the light.</summary>
public enum Shading
{
    /// <summary>Its own colour, whatever the light.</summary>
    Unlit,

    /// <summary>One shade per triangle, from its face: hard, faceted, low-poly.</summary>
    Flat,

    /// <summary>Lit at each vertex from its normal and blended across: smooth.</summary>
    Smooth,
}

/// <summary>What a mesh is made of: a colour, and how it is lit.</summary>
public sealed record Material(Vector4 Color, Shading Shading = Shading.Smooth);

/// <summary>One directional light (the way it travels, and its colour) plus light from everywhere.</summary>
public sealed record Lighting(Vector3 Direction, Vector3 Color, Vector3 Ambient)
{
    /// <summary>Sunlight from above and a little in front, with soft fill.</summary>
    public static Lighting Default { get; } = new(new Vector3(-0.3f, -1, -0.5f), new Vector3(0.9f), new Vector3(0.25f));

    /// <summary>The colour of a surface of <paramref name="color"/> facing <paramref name="normal"/> (Lambert).</summary>
    public Vector4 Shade(Vector4 color, Vector3 normal)
    {
        var toLight = -Vector3.Normalize(Direction);
        var diffuse = MathF.Max(0, Vector3.Dot(normal, toLight));
        var light = Ambient + (Color * diffuse);
        return new Vector4(Vector3.Min(new Vector3(color.X, color.Y, color.Z) * light, Vector3.One), color.W);
    }
}

/// <summary>What the last frame drew.</summary>
public sealed class RenderStats
{
    /// <summary>Triangles that reached the rasterizer (not culled, not wholly outside the view).</summary>
    public int Triangles { get; internal set; }

    /// <summary>Pixels written (passed the depth test).</summary>
    public int Pixels { get; internal set; }

    internal void Reset() => (Triangles, Pixels) = (0, 0);
}

/// <summary>
/// Draws triangles into a <see cref="FrameBuffer"/> on the CPU: transform, clip against the view (so triangles through
/// the camera's plane are cut, not smeared), cull back faces, then fill pixel centres with the top-left rule, test
/// depth, and blend vertex colours perspective-correctly. Meant for small, low-resolution pictures (a retro look);
/// the GPU draws everything else.
/// </summary>
public sealed class Renderer
{
    private readonly List<ClipVertex> _polygon = new(9);

    /// <param name="target">Where to draw.</param>
    /// <param name="depth">The depth buffer, or null to make one the same size.</param>
    public Renderer(FrameBuffer target, DepthBuffer? depth = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        Depth = depth ?? new DepthBuffer(target.Width, target.Height);
        if (Depth.Width != target.Width || Depth.Height != target.Height)
        {
            throw new ArgumentException("The depth buffer must be the frame's size.", nameof(depth));
        }
    }

    public FrameBuffer Target { get; }

    public DepthBuffer Depth { get; }

    /// <summary>World to clip space, e.g. <see cref="Camera.ViewProjection"/>.</summary>
    public Matrix4x4 ViewProjection { get; set; } = Matrix4x4.Identity;

    /// <summary>Whether triangles seen from behind (clockwise on screen) are skipped.</summary>
    public bool CullBackFaces { get; set; } = true;

    public Lighting Lighting { get; set; } = Lighting.Default;

    public RenderStats Stats { get; } = new();

    /// <summary>Clears the picture to <paramref name="color"/>, the depth to far, and the stats.</summary>
    public void Clear(uint color)
    {
        Target.Clear(color);
        Depth.Clear();
        Stats.Reset();
    }

    /// <summary>Draws <paramref name="mesh"/> placed by <paramref name="world"/>, coloured and lit as <paramref name="material"/> says.</summary>
    public void DrawMesh(Mesh mesh, Matrix4x4 world, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        Matrix4x4.Invert(world, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        Vector3 WorldNormal(Vector3 n) => SafeNormalize(Vector3.TransformNormal(n, normalMatrix));
        foreach (var t in mesh.Triangles)
        {
            Vector3 pa = mesh.Positions[t.A], pb = mesh.Positions[t.B], pc = mesh.Positions[t.C];
            Vector4 ca, cb, cc;
            switch (material.Shading)
            {
                case Shading.Unlit:
                    ca = cb = cc = material.Color;
                    break;
                case Shading.Flat:
                    var face = WorldNormal(Vector3.Cross(pb - pa, pc - pa));
                    ca = cb = cc = Lighting.Shade(material.Color, face);
                    break;
                default:
                    ca = Lighting.Shade(material.Color, WorldNormal(mesh.Normals[t.A]));
                    cb = Lighting.Shade(material.Color, WorldNormal(mesh.Normals[t.B]));
                    cc = Lighting.Shade(material.Color, WorldNormal(mesh.Normals[t.C]));
                    break;
            }

            DrawTriangle(new Vertex(pa, ca), new Vertex(pb, cb), new Vertex(pc, cc), world);
        }
    }

    /// <summary>Draws one triangle, its corners placed by <paramref name="world"/>, with its vertex colours blended across.</summary>
    public void DrawTriangle(in Vertex a, in Vertex b, in Vertex c, Matrix4x4 world)
    {
        var transform = world * ViewProjection;
        _polygon.Clear();
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(a.Position, 1), transform), a.Color));
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(b.Position, 1), transform), b.Color));
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(c.Position, 1), transform), c.Color));

        IReadOnlyList<ClipVertex> polygon = _polygon;
        if (!AllInside(polygon))
        {
            foreach (var plane in ClipPlanes)
            {
                polygon = Clipping.ClipPolygon(polygon, plane, ClipVertex.Lerp);
                if (polygon.Count < 3)
                {
                    return;
                }
            }
        }

        // To the screen: pixels, y down; depth and the colours divided by w for perspective-correct blending.
        Span<ScreenVertex> screen = stackalloc ScreenVertex[polygon.Count];
        for (var i = 0; i < polygon.Count; i++)
        {
            var v = polygon[i];
            var inverseW = 1 / v.Position.W;
            var ndc = v.Position * inverseW;
            screen[i] = new ScreenVertex(
                new Vector2((ndc.X + 1) * 0.5f * Target.Width, (1 - ndc.Y) * 0.5f * Target.Height),
                ndc.Z,
                inverseW,
                v.Color * inverseW);
        }

        // Counter-clockwise in view (y up) is clockwise on the y-down screen: a positive screen area is a back face.
        var area = Segment2.Cross(screen[1].Position - screen[0].Position, screen[2].Position - screen[0].Position);
        if (area == 0 || (CullBackFaces && area > 0))
        {
            return;
        }

        Stats.Triangles++;
        for (var i = 1; i < screen.Length - 1; i++)
        {
            Fill(screen[0], screen[i], screen[i + 1]);
        }
    }

    // Each plane of the view in clip space; inside is zero or more (depth runs 0 to 1, as System.Numerics projects).
    private static readonly Func<ClipVertex, float>[] ClipPlanes =
    [
        v => v.Position.W + v.Position.X,
        v => v.Position.W - v.Position.X,
        v => v.Position.W + v.Position.Y,
        v => v.Position.W - v.Position.Y,
        v => v.Position.Z,
        v => v.Position.W - v.Position.Z,
    ];

    private static bool AllInside(IReadOnlyList<ClipVertex> polygon)
    {
        foreach (var v in polygon)
        {
            var p = v.Position;
            if (p.X < -p.W || p.X > p.W || p.Y < -p.W || p.Y > p.W || p.Z < 0 || p.Z > p.W)
            {
                return false;
            }
        }

        return true;
    }

    // Fills the pixel centres inside the triangle, walking the three edge functions across its bounding box.
    private void Fill(in ScreenVertex v0, in ScreenVertex v1, in ScreenVertex v2)
    {
        var (a, b, c) = (v0, v1, v2);
        var area = Segment2.Cross(b.Position - a.Position, c.Position - a.Position);
        if (area < 0)
        {
            (b, c) = (c, b);
            area = -area;
        }

        if (area == 0)
        {
            return;
        }

        var width = Target.Width;
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Position.X, MathF.Min(b.Position.X, c.Position.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.Position.X, MathF.Max(b.Position.X, c.Position.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Position.Y, MathF.Min(b.Position.Y, c.Position.Y))));
        var maxY = Math.Min(Target.Height - 1, (int)MathF.Ceiling(MathF.Max(a.Position.Y, MathF.Max(b.Position.Y, c.Position.Y))));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        // The weight of each corner is the edge function of the side opposite it.
        Edge ea = new(b.Position, c.Position), eb = new(c.Position, a.Position), ec = new(a.Position, b.Position);
        var (ba, bb, bc) = (Bias(ea), Bias(eb), Bias(ec));
        var inverseArea = 1 / area;
        var pixels = Target.Pixels;
        var depths = Depth.Depths;
        for (var y = minY; y <= maxY; y++)
        {
            var start = new Vector2(minX + 0.5f, y + 0.5f);
            float wa = ea.Evaluate(start), wb = eb.Evaluate(start), wc = ec.Evaluate(start);
            for (var x = minX; x <= maxX; x++, wa += ea.StepX, wb += eb.StepX, wc += ec.StepX)
            {
                if (wa < ba || wb < bb || wc < bc)
                {
                    continue;
                }

                float la = wa * inverseArea, lb = wb * inverseArea, lc = wc * inverseArea;
                var z = (la * a.Depth) + (lb * b.Depth) + (lc * c.Depth);
                var i = (y * width) + x;
                if (z >= depths[i])
                {
                    continue;
                }

                depths[i] = z;
                var inverseW = (la * a.InverseW) + (lb * b.InverseW) + (lc * c.InverseW);
                var color = ((la * a.ColorOverW) + (lb * b.ColorOverW) + (lc * c.ColorOverW)) / inverseW;
                pixels[i] = Colors.Pack(color);
                Stats.Pixels++;
            }
        }
    }

    // A pixel exactly on an edge counts only for a "top-left" edge, so shared edges are drawn once (see Edge.Covers).
    // A point is inside when its value is at least the bias: 0 for those edges, just above 0 for the others.
    private static float Bias(Edge edge)
    {
        var d = edge.B - edge.A;
        return d.Y < 0 || (d.Y == 0 && d.X < 0) ? 0 : float.Epsilon;
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 0 ? Vector3.Normalize(v) : Vector3.Zero;

    private readonly record struct ClipVertex(Vector4 Position, Vector4 Color)
    {
        public static ClipVertex Lerp(ClipVertex a, ClipVertex b, float t) =>
            new(Vector4.Lerp(a.Position, b.Position, t), Vector4.Lerp(a.Color, b.Color, t));
    }

    private readonly record struct ScreenVertex(Vector2 Position, float Depth, float InverseW, Vector4 ColorOverW);
}
