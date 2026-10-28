using System.Numerics;

namespace Jentl.Physics;

/// <summary>
/// A link between two bodies' centres, or between a body and a fixed point in the world (<see cref="B"/> null,
/// <see cref="Anchor"/> the point). Bodies don't rotate, so a joint holds distances, not angles: a pendulum, a chain
/// of links, a wrecking ball, a trailer. Add joints with <see cref="PhysicsWorld.Add(Joint)"/>.
/// </summary>
public abstract class Joint
{
    protected Joint(Body a, Body? b, Vector2 anchor)
    {
        A = a ?? throw new ArgumentNullException(nameof(a));
        B = b;
        Anchor = anchor;
    }

    public Body A { get; }

    /// <summary>The other body, or null when the joint holds <see cref="A"/> to <see cref="Anchor"/>.</summary>
    public Body? B { get; }

    /// <summary>The fixed point the joint holds <see cref="A"/> to when there is no <see cref="B"/>; movable.</summary>
    public Vector2 Anchor { get; set; }

    /// <summary>The far end: <see cref="B"/>'s centre, or the anchor.</summary>
    public Vector2 End => B?.Position ?? Anchor;

    /// <summary>How far apart the ends are now.</summary>
    public float Length => Vector2.Distance(A.Position, End);

    /// <summary>Applies forces before the velocities are integrated (springs).</summary>
    internal virtual void ApplyForces()
    {
    }

    /// <summary>Corrects the velocities, once per solver pass, like a contact.</summary>
    internal virtual void SolveVelocity(float seconds)
    {
    }

    /// <summary>Corrects the positions after the bodies moved.</summary>
    internal virtual void SolvePosition()
    {
    }

    private protected float InverseMassB => B?.InverseMass ?? 0;

    private protected Vector2 VelocityB => B?.Velocity ?? Vector2.Zero;

    /// <summary>The unit vector from A to the far end and the distance, or null when they sit on top of each other.</summary>
    private protected (Vector2 Normal, float Distance)? Axis()
    {
        var between = End - A.Position;
        var distance = between.Length();
        return distance < 1e-6f ? null : (between / distance, distance);
    }

    /// <summary>Applies an impulse along <paramref name="normal"/> (towards B) of <paramref name="amount"/>: A takes it backwards, B forwards.</summary>
    private protected void Push(Vector2 normal, float amount)
    {
        A.Velocity -= normal * amount * A.InverseMass;
        if (B is { } b)
        {
            b.Velocity += normal * amount * b.InverseMass;
        }
    }

    /// <summary>
    /// Takes away the ends' speed apart, aiming to close a little of any stretch over the step. With
    /// <paramref name="slack"/> (a rope) it only ever pulls them together, and only once they are <paramref name="length"/> apart.
    /// </summary>
    private protected void Hold(float seconds, float length, bool slack)
    {
        var inverseMass = A.InverseMass + InverseMassB;
        if (inverseMass == 0 || Axis() is not { } axis)
        {
            return;
        }

        var stretch = axis.Distance - length;
        if (slack && stretch < 0)
        {
            return;
        }

        var parting = Vector2.Dot(VelocityB - A.Velocity, axis.Normal);
        var impulse = -(parting + 0.2f * stretch / seconds) / inverseMass;
        if (slack && impulse > 0)
        {
            return;
        }

        Push(axis.Normal, impulse);
    }

    /// <summary>Moves the ends <paramref name="error"/> closer along <paramref name="normal"/>, shared by their inverse masses.</summary>
    private protected void Pull(Vector2 normal, float error)
    {
        var inverseMass = A.InverseMass + InverseMassB;
        if (inverseMass == 0)
        {
            return;
        }

        var correction = normal * (error * 0.8f / inverseMass);
        A.Position += correction * A.InverseMass;
        if (B is { } b)
        {
            b.Position -= correction * b.InverseMass;
        }
    }
}

/// <summary>A rigid rod: the ends stay <see cref="RestLength"/> apart, neither nearer nor further.</summary>
public sealed class DistanceJoint : Joint
{
    public DistanceJoint(Body a, Body b, float? length = null)
        : base(a, b ?? throw new ArgumentNullException(nameof(b)), default) =>
        RestLength = length ?? Length;

    public DistanceJoint(Body a, Vector2 anchor, float? length = null)
        : base(a, null, anchor) =>
        RestLength = length ?? Length;

    public float RestLength { get; set; }

    internal override void SolveVelocity(float seconds) => Hold(seconds, RestLength, slack: false);

    internal override void SolvePosition()
    {
        if (Axis() is { } axis)
        {
            Pull(axis.Normal, axis.Distance - RestLength);
        }
    }
}

/// <summary>A rope: the ends may come together freely but never get further apart than <see cref="MaxLength"/>.</summary>
public sealed class RopeJoint : Joint
{
    public RopeJoint(Body a, Body b, float? maxLength = null)
        : base(a, b ?? throw new ArgumentNullException(nameof(b)), default) =>
        MaxLength = maxLength ?? Length;

    public RopeJoint(Body a, Vector2 anchor, float? maxLength = null)
        : base(a, null, anchor) =>
        MaxLength = maxLength ?? Length;

    public float MaxLength { get; set; }

    /// <summary>Whether the rope is pulled straight.</summary>
    public bool IsTaut => Length >= MaxLength - 1e-3f;

    internal override void SolveVelocity(float seconds) => Hold(seconds, MaxLength, slack: true);

    internal override void SolvePosition()
    {
        if (Axis() is { } axis && axis.Distance > MaxLength)
        {
            Pull(axis.Normal, axis.Distance - MaxLength);
        }
    }
}

/// <summary>
/// A spring: pulls or pushes the ends towards <see cref="RestLength"/> with <see cref="Stiffness"/> (force per unit
/// stretched), and <see cref="Damping"/> slows their speed apart so it settles rather than bouncing for ever.
/// </summary>
public sealed class SpringJoint : Joint
{
    public SpringJoint(Body a, Body b, float stiffness, float damping = 0, float? restLength = null)
        : base(a, b ?? throw new ArgumentNullException(nameof(b)), default)
    {
        Stiffness = stiffness;
        Damping = damping;
        RestLength = restLength ?? Length;
    }

    public SpringJoint(Body a, Vector2 anchor, float stiffness, float damping = 0, float? restLength = null)
        : base(a, null, anchor)
    {
        Stiffness = stiffness;
        Damping = damping;
        RestLength = restLength ?? Length;
    }

    public float RestLength { get; set; }

    public float Stiffness { get; set; }

    public float Damping { get; set; }

    internal override void ApplyForces()
    {
        if (Axis() is not { } axis)
        {
            return;
        }

        var parting = Vector2.Dot(VelocityB - A.Velocity, axis.Normal);
        var force = axis.Normal * (Stiffness * (axis.Distance - RestLength) + Damping * parting);
        if (A.Type == BodyType.Dynamic)
        {
            A.ApplyForce(force);
        }

        if (B is { Type: BodyType.Dynamic } b)
        {
            b.ApplyForce(-force);
        }
    }
}
