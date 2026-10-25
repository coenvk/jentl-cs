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
/// <see cref="GpuView"/> on the graphics card (smooth, any resolution). Call <see cref="Begin"/>, draw, then
/// <see cref="Finish"/> while the sprite batch is not begun (e.g. in <c>Scene.DrawLayers</c>), and draw
/// <see cref="Texture"/> in <c>Draw</c>.
/// </summary>
public interface IMeshView : IDisposable
{
    /// <summary>The picture's size in pixels.</summary>
    Point Size { get; }

    /// <summary>World to clip space (System.Numerics conventions).</summary>
    Numerics.Matrix4x4 ViewProjection { get; set; }

    Lighting Lighting { get; set; }

    /// <summary>The finished picture, after <see cref="Finish"/>.</summary>
    Texture2D Texture { get; }

    /// <summary>Starts a picture: clears it to <paramref name="background"/>.</summary>
    void Begin(Color background);

    void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material);

    /// <summary>Finishes the picture into <see cref="Texture"/>.</summary>
    void Finish();
}

/// <summary>Meshes drawn on the CPU by <see cref="Renderer"/>, uploaded to a texture at <see cref="Finish"/>.</summary>
public sealed class SoftwareView : IMeshView
{
    private readonly Texture2D _texture;

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

    public Lighting Lighting
    {
        get => Renderer.Lighting;
        set => Renderer.Lighting = value;
    }

    public Texture2D Texture => _texture;

    public void Begin(Color background) => Renderer.Clear(background.PackedValue);

    public void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material) => Renderer.DrawMesh(mesh, world, material);

    public void Finish() => _texture.SetData(Renderer.Target.Pixels);

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
    private RenderTargetBinding[] _previous = [];

    public GpuView(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
        _effect = new BasicEffect(device) { View = Matrix.Identity, SpecularColor = Vector3.Zero };
    }

    public Point Size => new(_target.Width, _target.Height);

    public Numerics.Matrix4x4 ViewProjection { get; set; } = Numerics.Matrix4x4.Identity;

    public Lighting Lighting { get; set; } = Lighting.Default;

    public Texture2D Texture => _target;

    /// <summary>Triangles drawn since <see cref="Begin"/>.</summary>
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

    public void DrawMesh(Mesh mesh, Numerics.Matrix4x4 world, Material material)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);
        var flat = material.Shading == Shading.Flat;
        var buffers = (flat ? _flat : _smooth).GetValue(mesh, m => Buffers.Create(_device, m, flat));

        // The combined view-projection goes in Projection; lighting is worked out in world space from World.
        _effect.World = Convert3D.ToXna(world);
        _effect.Projection = Convert3D.ToXna(ViewProjection);
        _effect.DiffuseColor = new Vector3(material.Color.X, material.Color.Y, material.Color.Z);
        _effect.Alpha = material.Color.W;
        _effect.LightingEnabled = material.Shading != Shading.Unlit;
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

    public void Finish()
    {
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
            var vertexBuffer = new VertexBuffer(device, typeof(VertexPositionNormalTexture), vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
            IndexBuffer? indexBuffer = null;
            if (indices.Length > 0)
            {
                indexBuffer = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(indices);
            }

            return new Buffers(vertexBuffer, indexBuffer, mesh.Triangles.Count);
        }
    }
}
