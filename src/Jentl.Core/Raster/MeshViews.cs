using System.Runtime.CompilerServices;
using Jentl.Geometry;
using Jentl.Raster;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Numerics = System.Numerics;

namespace Jentl.Core.Raster;

/// <summary>
/// Draws 3D meshes into a picture that a scene then shows as a texture. Two kinds draw the same meshes, camera and
/// light: <see cref="SoftwareView"/> on the CPU (Jentl.Raster, a chunky retro look at low resolution) and
/// <see cref="GpuView"/> on the graphics card (smooth, any resolution). Call <c>Begin</c>, draw, then
/// <see cref="Finish"/> while the sprite batch is not begun (e.g. in <c>Scene.DrawLayers</c>), and draw
/// <see cref="Texture"/> in <c>Draw</c>.
/// </summary>
public interface IMeshView : IDisposable
{
    /// <summary>The picture's size in pixels.</summary>
    Point Size { get; }

    /// <summary>World to clip space (System.Numerics conventions).</summary>
    Numerics.Matrix4x4 ViewProjection { get; set; }

    /// <summary>
    /// World to the camera's space, part of <see cref="ViewProjection"/>: the graphics card measures fog from it.
    /// <see cref="MeshViewExtensions.Look"/> sets both from a camera.
    /// </summary>
    Numerics.Matrix4x4 View { get; set; }

    Lighting Lighting { get; set; }

    /// <summary>Distance fog, or null for none.</summary>
    Fog? Fog { get; set; }

    /// <summary>The finished picture, after <see cref="Finish"/>.</summary>
    Texture2D Texture { get; }

    /// <summary>Starts a picture: clears it to <paramref name="background"/>.</summary>
    void Begin(Color background);

    /// <summary>Starts a picture: clears it to <paramref name="sky"/> as the camera sees it (set the camera first).</summary>
    void Begin(Sky sky);

    void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material);

    /// <summary>
    /// A square <paramref name="size"/> across facing the camera, blended by its colour's alpha (a particle). Points
    /// are drawn at <see cref="Finish"/>, after the meshes: hidden by nearer ones, never hiding anything themselves.
    /// </summary>
    void DrawPoint(Numerics.Vector3 position, float size, Numerics.Vector4 color);

    /// <summary>Finishes the picture into <see cref="Texture"/>.</summary>
    void Finish();
}

/// <summary>Meshes drawn on the CPU by <see cref="Renderer"/>, uploaded to a texture at <see cref="Finish"/>.</summary>
public sealed class SoftwareView : IMeshView
{
    private readonly Texture2D _texture;
    private readonly List<(Numerics.Vector3 Position, float Size, Numerics.Vector4 Color)> _points = [];

    public SoftwareView(GraphicsDevice device, int width, int height)
    {
        Renderer = new Renderer(new FrameBuffer(width, height));
        _texture = new Texture2D(device, width, height, false, SurfaceFormat.Color);
    }

    /// <summary>The renderer underneath, for triangles and statistics.</summary>
    public Renderer Renderer { get; }

    public Point Size => new(Renderer.Target.Width, Renderer.Target.Height);

    public Numerics.Matrix4x4 ViewProjection
    {
        get => Renderer.ViewProjection;
        set => Renderer.ViewProjection = value;
    }

    /// <summary>Not needed here: the renderer measures fog from the projection.</summary>
    public Numerics.Matrix4x4 View { get; set; } = Numerics.Matrix4x4.Identity;

    public Lighting Lighting
    {
        get => Renderer.Lighting;
        set => Renderer.Lighting = value;
    }

    public Fog? Fog
    {
        get => Renderer.Fog;
        set => Renderer.Fog = value;
    }

    public Texture2D Texture => _texture;

    public void Begin(Color background) => Renderer.Clear(background.PackedValue);

    public void Begin(Sky sky) => Renderer.Clear(sky);

    public void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material) => Renderer.DrawMesh(mesh, world, material);

    public void DrawPoint(Numerics.Vector3 position, float size, Numerics.Vector4 color) => _points.Add((position, size, color));

    public void Finish()
    {
        foreach (var (position, size, color) in _points)
        {
            Renderer.DrawPoint(position, size, color);
        }

        _points.Clear();
        _texture.SetData(Renderer.Target.Pixels);
    }

    public void Dispose() => _texture.Dispose();
}

/// <summary>
/// Meshes drawn on the graphics card with MonoGame's <see cref="BasicEffect"/> (one directional light and ambient, as
/// <see cref="Lighting"/> says) into a render target of its own with a depth buffer. Each mesh's vertices go to the
/// card once and are kept while the mesh lives.
/// </summary>
public sealed class GpuView : IMeshView
{
    // Jentl's meshes face the viewer when their corners run counter-clockwise; XNA culls those by default.
    private static readonly RasterizerState Cull = new() { CullMode = CullMode.CullClockwiseFace };

    private readonly GraphicsDevice _device;
    private readonly RenderTarget2D _target;
    private readonly BasicEffect _effect;
    private readonly ConditionalWeakTable<Mesh, Buffers> _smooth = [];
    private readonly ConditionalWeakTable<Mesh, Buffers> _flat = [];
    private readonly ConditionalWeakTable<Jentl.Raster.Texture, Texture2D> _textures = [];
    private readonly List<VertexPositionColor> _points = [];
    private RenderTargetBinding[] _previous = [];

    public GpuView(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
        _effect = new BasicEffect(device) { View = Matrix.Identity, SpecularColor = Vector3.Zero };
    }

    public Point Size => new(_target.Width, _target.Height);

    public Numerics.Matrix4x4 ViewProjection { get; set; } = Numerics.Matrix4x4.Identity;

    public Numerics.Matrix4x4 View { get; set; } = Numerics.Matrix4x4.Identity;

    public Lighting Lighting { get; set; } = Lighting.Default;

    public Fog? Fog { get; set; }

    public Texture2D Texture => _target;

    /// <summary>Triangles drawn since <c>Begin</c>.</summary>
    public int Triangles { get; private set; }

    public void Begin(Color background)
    {
        _previous = _device.GetRenderTargets();
        _device.SetRenderTarget(_target);
        _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, background, 1, 0);
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = Cull;
        Triangles = 0;
    }

    public void Begin(Sky sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        Begin(Color.Black);

        // Bands across the screen, each blending between the sky's colours at its edges, drawn straight in clip space.
        const int Bands = 32;
        Numerics.Matrix4x4.Invert(ViewProjection, out var inverse);
        var vertices = new VertexPositionColor[Bands * 6];
        for (var i = 0; i < Bands; i++)
        {
            var (top, bottom) = (1 - (2f * i / Bands), 1 - (2f * (i + 1) / Bands));
            var (above, below) = (Tint(sky.ColorAtRow(inverse, top)), Tint(sky.ColorAtRow(inverse, bottom)));
            VertexPositionColor At(float x, float y, Color c) => new(new Vector3(x, y, 0), c);
            var (a, b, c, d) = (At(-1, top, above), At(1, top, above), At(1, bottom, below), At(-1, bottom, below));
            (vertices[i * 6], vertices[(i * 6) + 1], vertices[(i * 6) + 2]) = (a, b, c);
            (vertices[(i * 6) + 3], vertices[(i * 6) + 4], vertices[(i * 6) + 5]) = (a, c, d);
        }

        _effect.World = _effect.View = _effect.Projection = Matrix.Identity;
        _effect.LightingEnabled = _effect.TextureEnabled = _effect.FogEnabled = false;
        _effect.VertexColorEnabled = true;
        _effect.DiffuseColor = Vector3.One;
        _effect.Alpha = 1;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, Bands * 2);
        }

        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = Cull;

        static Color Tint(Numerics.Vector4 v) => new(v.X, v.Y, v.Z, 1f);
    }

    public void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        var flat = material.Shading == Shading.Flat;
        var buffers = (flat ? _flat : _smooth).GetValue(mesh, m => Buffers.Create(_device, m, flat));

        // The view goes in View (fog is measured there) and the rest of the view-projection in Projection; lighting is
        // worked out in world space from World.
        Numerics.Matrix4x4.Invert(View, out var inverseView);
        _effect.World = Convert3D.ToXna(world);
        _effect.View = Convert3D.ToXna(View);
        _effect.Projection = Convert3D.ToXna(inverseView * ViewProjection);
        _effect.FogEnabled = Fog is not null;
        if (Fog is { } fog)
        {
            _effect.FogColor = new Vector3(fog.Color.X, fog.Color.Y, fog.Color.Z);
            _effect.FogStart = fog.Start;
            _effect.FogEnd = fog.End;
        }

        _effect.DiffuseColor = new Vector3(material.Color.X, material.Color.Y, material.Color.Z);
        _effect.Alpha = material.Color.W;
        _effect.LightingEnabled = material.Shading != Shading.Unlit;
        _effect.VertexColorEnabled = false;
        _effect.TextureEnabled = material.Texture is not null;
        if (material.Texture is { } texture)
        {
            // The same texels on the card; see-through ones are blended here rather than cut out as on the CPU.
            _effect.Texture = _textures.GetValue(texture, t =>
            {
                var uploaded = new Texture2D(_device, t.Width, t.Height, false, SurfaceFormat.Color);
                uploaded.SetData(t.Pixels);
                return uploaded;
            });
            var point = texture.Filter == Jentl.Raster.TextureFilter.Nearest;
            _device.SamplerStates[0] = texture.Wrap == Jentl.Raster.TextureWrap.Repeat
                ? (point ? SamplerState.PointWrap : SamplerState.LinearWrap)
                : (point ? SamplerState.PointClamp : SamplerState.LinearClamp);
            _device.BlendState = BlendState.NonPremultiplied;
        }
        else
        {
            _device.BlendState = BlendState.Opaque;
        }

        // Translucent: blended over what is there, tested against depth but not writing it.
        if (material.Translucent)
        {
            _device.BlendState = BlendState.NonPremultiplied;
        }

        _device.DepthStencilState = material.Translucent ? DepthStencilState.DepthRead : DepthStencilState.Default;

        if (_effect.LightingEnabled)
        {
            _effect.AmbientLightColor = Convert3D.ToXna(Lighting.Ambient);
            _effect.DirectionalLight0.Enabled = true;
            _effect.DirectionalLight0.Direction = Convert3D.ToXna(Numerics.Vector3.Normalize(Lighting.Direction));
            _effect.DirectionalLight0.DiffuseColor = Convert3D.ToXna(Lighting.Color);
            _effect.DirectionalLight0.SpecularColor = Vector3.Zero;
            _effect.DirectionalLight1.Enabled = false;
            _effect.DirectionalLight2.Enabled = false;
        }

        _device.SetVertexBuffer(buffers.Vertices);
        _device.Indices = buffers.Indices;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (buffers.Indices is null)
            {
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, buffers.Triangles);
            }
            else
            {
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, buffers.Triangles);
            }
        }

        Triangles += buffers.Triangles;
    }

    public void DrawPoint(Numerics.Vector3 position, float size, Numerics.Vector4 color)
    {
        // The camera's right and up in the world are the view's first two columns.
        var right = new Numerics.Vector3(View.M11, View.M21, View.M31) * (size / 2);
        var up = new Numerics.Vector3(View.M12, View.M22, View.M32) * (size / 2);
        var tint = new Color(color.X, color.Y, color.Z, color.W);
        VertexPositionColor Corner(Numerics.Vector3 p) => new(Convert3D.ToXna(p), tint);
        var (a, b, c, d) = (Corner(position - right - up), Corner(position + right - up), Corner(position + right + up), Corner(position - right + up));
        _points.AddRange([a, b, c, a, c, d]);
    }

    public void Finish()
    {
        if (_points.Count > 0)
        {
            Numerics.Matrix4x4.Invert(View, out var inverseView);
            _effect.World = Matrix.Identity;
            _effect.View = Convert3D.ToXna(View);
            _effect.Projection = Convert3D.ToXna(inverseView * ViewProjection);
            _effect.LightingEnabled = false;
            _effect.TextureEnabled = false;
            _effect.VertexColorEnabled = true;
            _effect.DiffuseColor = Vector3.One;
            _effect.Alpha = 1;
            _effect.FogEnabled = Fog is not null;
            if (Fog is { } fog)
            {
                _effect.FogColor = new Vector3(fog.Color.X, fog.Color.Y, fog.Color.Z);
                _effect.FogStart = fog.Start;
                _effect.FogEnd = fog.End;
            }

            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.DepthRead;
            _device.RasterizerState = RasterizerState.CullNone;
            var vertices = _points.ToArray();
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }

            _points.Clear();
            _device.DepthStencilState = DepthStencilState.Default;
        }

        _device.SetRenderTargets(_previous);
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    public void Dispose()
    {
        _effect.Dispose();
        _target.Dispose();
    }

    // A mesh's buffers on the card; dropped (and finalised) with the mesh.
    private sealed class Buffers(VertexBuffer vertices, IndexBuffer? indices, int triangles)
    {
        public VertexBuffer Vertices { get; } = vertices;

        public IndexBuffer? Indices { get; } = indices;

        public int Triangles { get; } = triangles;

        public static Buffers Create(GraphicsDevice device, Mesh mesh, bool flat)
        {
            var (vertices, indices) = GpuMeshData.Build(mesh, flat);
            if (indices.Length > 0 && GpuMeshData.IndexSize(vertices.Length) is null)
            {
                // Too many vertices for 16-bit indices: draw every triangle's corners in order instead.
                vertices = [.. indices.Select(i => vertices[i])];
                indices = [];
            }

            var vertexBuffer = new VertexBuffer(device, typeof(VertexPositionNormalTexture), vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
            IndexBuffer? indexBuffer = null;
            if (indices.Length > 0)
            {
                indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(GpuMeshData.ToShorts(indices));
            }

            return new Buffers(vertexBuffer, indexBuffer, mesh.Triangles.Count);
        }
    }
}

/// <summary>Helpers for any <see cref="IMeshView"/>.</summary>
public static class MeshViewExtensions
{
    /// <summary>Draws every part of <paramref name="model"/>, placed by <paramref name="world"/>, translucent parts last.</summary>
    public static void DrawModel(this IMeshView view, MeshModel model, Numerics.Matrix4x4 world)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(model);
        var parts = model.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            view.DrawMesh(parts[i].Mesh, world, parts[i].Material);
        }
    }

    /// <summary>Draws the living <paramref name="particles"/> as points, each its size, colour and fading alpha.</summary>
    public static void DrawParticles(this IMeshView view, Particles particles)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(particles);
        var live = particles.Live;
        for (var i = 0; i < live.Count; i++)
        {
            var p = live[i];
            view.DrawPoint(p.Position, p.Size, p.Color with { W = p.Alpha });
        }
    }

    /// <summary>Looks through <paramref name="camera"/>: sets the view and the view-projection, at the view's shape.</summary>
    public static void Look(this IMeshView view, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(camera);
        view.View = camera.View;
        view.ViewProjection = camera.ViewProjection(view.Size.X / (float)Math.Max(1, view.Size.Y));
    }
}
