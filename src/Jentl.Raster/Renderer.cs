using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster;

/// <summary>A corner of a triangle to draw: where it is (before the world transform), its colour, and its texture coordinate.</summary>
public readonly record struct Vertex(Vector3 Position, Vector4 Color, Vector2 UV = default);

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

/// <summary>What a mesh is made of: a colour, how it is lit, and optionally a texture (tinted by the colour).</summary>
public sealed record Material(Vector4 Color, Shading Shading = Shading.Smooth, Texture? Texture = null);

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

/// <summary>
/// Haze that thickens with distance in front of the camera: nothing nearer than <paramref name="Start"/>, only
/// <paramref name="Color"/> from <paramref name="End"/> on, blended evenly between. Clear to the same colour for a
/// horizon that melts into the sky.
/// </summary>
public sealed record Fog(Vector4 Color, float Start, float End);

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

    /// <summary>How textures are spread across triangles: correctly, or affine for a wobbly retro look.</summary>
    public TextureMapping Mapping { get; set; } = TextureMapping.PerspectiveCorrect;

    public Lighting Lighting { get; set; } = Lighting.Default;

    /// <summary>Distance fog, or null for none.</summary>
    public Fog? Fog { get; set; }

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
        var uvs = mesh.UVs.Count == mesh.Positions.Count ? mesh.UVs : null;
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

            Vector2 ua = uvs?[t.A] ?? default, ub = uvs?[t.B] ?? default, uc = uvs?[t.C] ?? default;
            DrawTriangle(new Vertex(pa, ca, ua), new Vertex(pb, cb, ub), new Vertex(pc, cc, uc), world, material.Texture);
        }
    }

    /// <summary>
    /// Draws one triangle, its corners placed by <paramref name="world"/>, with its vertex colours blended across and,
    /// given a <paramref name="texture"/>, that texture on it, tinted by the colours.
    /// </summary>
    public void DrawTriangle(in Vertex a, in Vertex b, in Vertex c, Matrix4x4 world, Texture? texture = null)
    {
        var transform = world * ViewProjection;
        _polygon.Clear();
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(a.Position, 1), transform), a.Color, a.UV));
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(b.Position, 1), transform), b.Color, b.UV));
        _polygon.Add(new ClipVertex(Vector4.Transform(new Vector4(c.Position, 1), transform), c.Color, c.UV));

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

        // To the screen: pixels, y down; depth, and the colours and texture coordinates divided by w so they blend
        // perspective-correctly (affine mapping keeps the texture coordinates as they are instead).
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
                v.Color * inverseW,
                v.UV * inverseW,
                v.UV);
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
            Fill(screen[0], screen[i], screen[i + 1], texture);
        }
    }

    /// <summary>
    /// Draws a square <paramref name="size"/> across facing the camera at <paramref name="position"/>, as a particle:
    /// blended over what is there by the colour's alpha, hidden behind nearer things, fogged, but writing no depth, so
    /// particles never hide each other or what is drawn after them. Nothing is drawn behind the camera or past far.
    /// </summary>
    public void DrawPoint(Vector3 position, float size, Vector4 color)
    {
        var clip = Vector4.Transform(new Vector4(position, 1), ViewProjection);
        if (clip.W <= 0 || clip.Z < 0 || clip.Z > clip.W)
        {
            return;
        }

        // The view-projection's y column is the view's (unit length) times the projection's vertical scale, so its
        // length gives how many pixels a unit spans at distance w.
        var inverseW = 1 / clip.W;
        var (width, height) = (Target.Width, Target.Height);
        var m = ViewProjection;
        var half = size * new Vector3(m.M12, m.M22, m.M32).Length() * 0.5f * height * inverseW / 2;
        var (cx, cy) = ((clip.X * inverseW + 1) * 0.5f * width, (1 - (clip.Y * inverseW)) * 0.5f * height);

        // The pixels whose centres fall inside the square.
        var minX = Math.Max(0, (int)MathF.Ceiling(cx - half - 0.5f));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(cx + half - 0.5f) - 1);
        var minY = Math.Max(0, (int)MathF.Ceiling(cy - half - 0.5f));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(cy + half - 0.5f) - 1);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        var z = clip.Z * inverseW;
        var (r, g, b, a) = (color.X, color.Y, color.Z, Math.Clamp(color.W, 0, 1));
        if (Fog is { } fog)
        {
            var f = Math.Clamp((clip.W - fog.Start) / MathF.Max(fog.End - fog.Start, 1e-6f), 0, 1);
            (r, g, b) = (r + ((fog.Color.X - r) * f), g + ((fog.Color.Y - g) * f), b + ((fog.Color.Z - b) * f));
        }

        var pixels = Target.Pixels;
        var depths = Depth.Depths;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var i = (y * width) + x;
                if (z >= depths[i])
                {
                    continue;
                }

                var under = pixels[i];
                const float Scale = 1 / 255f;
                var keep = 1 - a;
                pixels[i] = Colors.Pack(
                    (r * a) + ((under & 0xFF) * Scale * keep),
                    (g * a) + (((under >> 8) & 0xFF) * Scale * keep),
                    (b * a) + (((under >> 16) & 0xFF) * Scale * keep),
                    a + ((under >> 24) * Scale * keep));
                Stats.Pixels++;
            }
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

    // Fills the pixel centres inside the triangle. Everything blended across it (depth, 1/w, colour and texture
    // coordinates over w) is linear in screen space, so each row finds its covered span from the edge functions,
    // works the values out once at the span's start, and then only adds a fixed step per pixel: cheap enough for the
    // browser's interpreter.
    private void Fill(in ScreenVertex v0, in ScreenVertex v1, in ScreenVertex v2, Texture? texture)
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
        var (sa, sb, sc) = (ea.StepX * inverseArea, eb.StepX * inverseArea, ec.StepX * inverseArea);

        // How much each value changes per pixel along x.
        var dz = (sa * a.Depth) + (sb * b.Depth) + (sc * c.Depth);
        var dw = (sa * a.InverseW) + (sb * b.InverseW) + (sc * c.InverseW);
        var dColor = (sa * a.ColorOverW) + (sb * b.ColorOverW) + (sc * c.ColorOverW);
        var dSurface = (sa * a.UVOverW) + (sb * b.UVOverW) + (sc * c.UVOverW);
        var dScreen = (sa * a.UV) + (sb * b.UV) + (sc * c.UV);

        var pixels = Target.Pixels;
        var depths = Depth.Depths;
        var affine = Mapping == TextureMapping.Affine;
        var drawnPixels = 0;

        // Fog by the distance in front of the camera, which is w.
        var fogged = Fog is not null;
        var (fogColor, fogStart) = fogged ? (Fog!.Color, Fog.Start) : (Vector4.Zero, 0);
        var fogScale = fogged ? 1 / MathF.Max(Fog!.End - Fog.Start, 1e-6f) : 0;
        for (var y = minY; y <= maxY; y++)
        {
            // Find the first covered pixel of the row; the span runs until a pixel is not covered (triangles are convex).
            var start = new Vector2(minX + 0.5f, y + 0.5f);
            float wa = ea.Evaluate(start), wb = eb.Evaluate(start), wc = ec.Evaluate(start);
            var x = minX;
            while (x <= maxX && (wa < ba || wb < bb || wc < bc))
            {
                wa += ea.StepX;
                wb += eb.StepX;
                wc += ec.StepX;
                x++;
            }

            if (x > maxX)
            {
                continue;
            }

            var (la, lb, lc) = (wa * inverseArea, wb * inverseArea, wc * inverseArea);
            var z = (la * a.Depth) + (lb * b.Depth) + (lc * c.Depth);
            var iw = (la * a.InverseW) + (lb * b.InverseW) + (lc * c.InverseW);
            var color = (la * a.ColorOverW) + (lb * b.ColorOverW) + (lc * c.ColorOverW);
            var surface = (la * a.UVOverW) + (lb * b.UVOverW) + (lc * c.UVOverW);
            var screen = (la * a.UV) + (lb * b.UV) + (lc * c.UV);
            var row = y * width;
            for (; x <= maxX && wa >= ba && wb >= bb && wc >= bc; x++)
            {
                var i = row + x;
                if (z < depths[i])
                {
                    var w = 1 / iw;
                    float r = color.X * w, g = color.Y * w, bl = color.Z * w, al = color.W * w;
                    var drawn = true;
                    if (texture is not null)
                    {
                        var texel = affine ? texture.Fetch(screen.X, screen.Y) : texture.Fetch(surface.X * w, surface.Y * w);
                        if (texel >> 24 < 128)
                        {
                            drawn = false; // cut out: nothing drawn, no depth written
                        }
                        else
                        {
                            const float Scale = 1 / 255f;
                            r *= (texel & 0xFF) * Scale;
                            g *= ((texel >> 8) & 0xFF) * Scale;
                            bl *= ((texel >> 16) & 0xFF) * Scale;
                            al *= (texel >> 24) * Scale;
                        }
                    }

                    if (drawn)
                    {
                        if (fogged)
                        {
                            var f = Math.Clamp((w - fogStart) * fogScale, 0, 1);
                            r += (fogColor.X - r) * f;
                            g += (fogColor.Y - g) * f;
                            bl += (fogColor.Z - bl) * f;
                        }

                        depths[i] = z;
                        pixels[i] = Colors.Pack(r, g, bl, al);
                        drawnPixels++;
                    }
                }

                wa += ea.StepX;
                wb += eb.StepX;
                wc += ec.StepX;
                z += dz;
                iw += dw;
                color += dColor;
                surface += dSurface;
                screen += dScreen;
            }
        }

        Stats.Pixels += drawnPixels;
    }

    // A pixel exactly on an edge counts only for a "top-left" edge, so shared edges are drawn once (see Edge.Covers).
    // A point is inside when its value is at least the bias: 0 for those edges, just above 0 for the others.
    private static float Bias(Edge edge)
    {
        var d = edge.B - edge.A;
        return d.Y < 0 || (d.Y == 0 && d.X < 0) ? 0 : float.Epsilon;
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 0 ? Vector3.Normalize(v) : Vector3.Zero;

    private readonly record struct ClipVertex(Vector4 Position, Vector4 Color, Vector2 UV)
    {
        public static ClipVertex Lerp(ClipVertex a, ClipVertex b, float t) =>
            new(Vector4.Lerp(a.Position, b.Position, t), Vector4.Lerp(a.Color, b.Color, t), Vector2.Lerp(a.UV, b.UV, t));
    }

    private readonly record struct ScreenVertex(Vector2 Position, float Depth, float InverseW, Vector4 ColorOverW, Vector2 UVOverW, Vector2 UV);
}
