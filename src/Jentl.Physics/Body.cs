using System.Numerics;

namespace Jentl.Physics;

public enum BodyType
{
    /// <summary>Never moves: walls, floors.</summary>
    Static,

    /// <summary>Moved by forces, gravity and collisions.</summary>
    Dynamic,

    /// <summary>Moves at its velocity, as if infinitely heavy: pushes dynamic bodies but is never pushed (platforms, paddles).</summary>
    Kinematic,
}

/// <summary>
/// Something in a <see cref="PhysicsWorld"/>: a shape at a position, with velocity and material. Units are up to
/// the game (pixels work well); velocities are per second.
/// </summary>
public sealed class Body
{
    private float _mass = 1;

    public Body(Shape shape, Vector2 position, BodyType type = BodyType.Dynamic)
    {
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        Position = position;
        Type = type;
    }

    /// <summary>Unique within its world once added.</summary>
    public int Id { get; internal set; } = -1;

    public BodyType Type { get; }

    public Shape Shape { get; set; }

    public Vector2 Position { get; set; }

    public Vector2 Velocity { get; set; }

    /// <summary>A dynamic body's mass; static and kinematic bodies act as if infinitely heavy.</summary>
    public float Mass
    {
        get => _mass;
        set => _mass = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Mass must be positive.");
    }

    public float InverseMass => Type == BodyType.Dynamic ? 1 / _mass : 0;

    /// <summary>Bounciness, 0 (dead) to 1 (keeps all its speed). Two bodies bounce with the higher of theirs.</summary>
    public float Restitution { get; set; } = 0.1f;

    /// <summary>Grip when sliding, 0 (ice) and up. Two bodies grip with the geometric mean of theirs.</summary>
    public float Friction { get; set; } = 0.4f;

    /// <summary>How strongly gravity pulls this body: 0 floats, 1 normal.</summary>
    public float GravityScale { get; set; } = 1;

    /// <summary>Slows the body down over time, like air resistance (per second).</summary>
    public float LinearDamping { get; set; }

    /// <summary>A sensor reports contacts but doesn't push or get pushed: pickups, trigger zones.</summary>
    public bool IsSensor { get; set; }

    /// <summary>The collision layers the body is on (bits).</summary>
    public uint Layer { get; set; } = 1;

    /// <summary>The layers the body collides with (bits); two bodies collide only if each is in the other's mask.</summary>
    public uint Mask { get; set; } = uint.MaxValue;

    /// <summary>Whatever the game wants to find from the body again, such as its entity.</summary>
    public object? UserData { get; set; }

    public Aabb Bounds => Shape.Bounds(Position);

    internal Vector2 Force { get; set; }

    /// <summary>Pushes for the next step only (accumulates until then): thrust, wind.</summary>
    public void ApplyForce(Vector2 force) => Force += force;

    /// <summary>Changes the velocity at once by <paramref name="impulse"/> ÷ mass: a kick, a jump.</summary>
    public void ApplyImpulse(Vector2 impulse) => Velocity += impulse * InverseMass;

    public bool CollidesWith(Body other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (Layer & other.Mask) != 0 && (other.Layer & Mask) != 0;
    }

    public override string ToString() => $"{Type} body {Id} at {Position}";
}
