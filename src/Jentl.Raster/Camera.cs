using System.Numerics;

namespace Jentl.Raster;

/// <summary>A perspective camera: where it is, what it looks at, how wide it sees and how near and far.</summary>
public sealed class Camera
{
    public Vector3 Position { get; set; } = new(0, 0, 5);

    public Vector3 Target { get; set; } = Vector3.Zero;

    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>The vertical field of view in radians.</summary>
    public float FieldOfView { get; set; } = MathF.PI / 3;

    public float Near { get; set; } = 0.1f;

    public float Far { get; set; } = 1000;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Up);

    /// <summary>The projection for a picture <paramref name="aspect"/> (width over height) wide.</summary>
    public Matrix4x4 Projection(float aspect) => Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Near, Far);

    /// <summary>World to clip space: <see cref="View"/> then <see cref="Projection"/>.</summary>
    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);
}
