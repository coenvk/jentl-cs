using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D;

/// <summary>
/// A camera on a sphere round a point, as model viewers and strategy games have it: turned by
/// <see cref="Yaw"/> and <see cref="Pitch"/>, zoomed by <see cref="Distance"/>, panned by moving the point. Feed it
/// the pointer's drags and the wheel, and <see cref="Apply"/> it to a <see cref="Camera"/>.
/// </summary>
public sealed class OrbitCamera
{
    private float _pitch = 0.4f;
    private float _distance = 10;

    /// <summary>The point it looks at and turns round.</summary>
    public Vector3 Target { get; set; }

    /// <summary>Round the vertical, in radians: 0 looks along -Z (from +Z), growing anticlockwise seen from above.</summary>
    public float Yaw { get; set; }

    /// <summary>Above the horizontal, in radians, kept between <see cref="MinPitch"/> and <see cref="MaxPitch"/>.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, MinPitch, MaxPitch);
    }

    /// <summary>From the target, kept between <see cref="MinDistance"/> and <see cref="MaxDistance"/>.</summary>
    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, MinDistance, MaxDistance);
    }

    public float MinPitch { get; set; } = -1.5f;

    public float MaxPitch { get; set; } = 1.5f;

    public float MinDistance { get; set; } = 0.5f;

    public float MaxDistance { get; set; } = 1000;

    /// <summary>Where the camera is.</summary>
    public Vector3 Position => Target + (new Vector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw)) * Distance);

    /// <summary>Turns it by a drag: radians round and up.</summary>
    public void Rotate(float yaw, float pitch)
    {
        Yaw += yaw;
        Pitch += pitch;
    }

    /// <summary>Moves nearer (below 1) or further (above 1) by a factor, as a mouse wheel does.</summary>
    public void Zoom(float factor) => Distance *= factor;

    /// <summary>Slides the target across the view: right and up, in multiples of the distance.</summary>
    public void Pan(float right, float up)
    {
        var forward = Vector3.Normalize(Target - Position);
        var side = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var top = Vector3.Cross(side, forward);
        Target += ((side * right) + (top * up)) * Distance;
    }

    /// <summary>Points it at the middle of <paramref name="box"/> from far enough to see all of it with a vertical field of view of <paramref name="fieldOfView"/>.</summary>
    public void Frame(Box3 box, float fieldOfView = MathF.PI / 3)
    {
        Target = box.Center;
        var radius = box.Size.Length() / 2;
        Distance = Math.Max(radius / MathF.Sin(fieldOfView / 2), MinDistance);
    }

    /// <summary>Places <paramref name="camera"/> where this says.</summary>
    public void Apply(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        camera.Position = Position;
        camera.Target = Target;
        camera.Up = Vector3.UnitY;
    }
}

/// <summary>
/// A camera that trails a node (a car, the player) from a place of its own relative to it, catching up smoothly:
/// behind and above by default, as the node faces +Z. Call <see cref="Update"/> once a frame.
/// </summary>
public sealed class FollowCamera(Node target)
{
    private bool _placed;

    public Node Target { get; set; } = target ?? throw new ArgumentNullException(nameof(target));

    /// <summary>Where the camera wants to be, in the target's own space (turning with it).</summary>
    public Vector3 Offset { get; set; } = new(0, 3, -8);

    /// <summary>How far in front of the target it looks, and how high, in the target's own space.</summary>
    public Vector3 LookAt { get; set; } = new(0, 1, 4);

    /// <summary>How quickly it catches up, per second: higher is tighter (6 closes 95% of a gap in half a second).</summary>
    public float Stiffness { get; set; } = 6;

    /// <summary>Jumps the camera to where it wants to be at the next <see cref="Update"/> (after a teleport, say).</summary>
    public void Snap() => _placed = false;

    public void Update(Camera camera, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var world = Target.World;
        var wanted = Vector3.Transform(Offset, world);
        var look = Vector3.Transform(LookAt, world);
        var follow = _placed ? 1 - MathF.Exp(-Stiffness * (float)elapsed.TotalSeconds) : 1;
        camera.Position = Vector3.Lerp(camera.Position, wanted, follow);
        camera.Target = _placed ? Vector3.Lerp(camera.Target, look, follow) : look;
        camera.Up = Vector3.UnitY;
        _placed = true;
    }
}
