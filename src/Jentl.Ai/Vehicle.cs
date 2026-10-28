using System.Numerics;

namespace Jentl.Ai;

/// <summary>
/// Something that steers itself, Reynolds style: a point with a velocity, a top speed and a top force. Each behaviour
/// (<see cref="Seek"/>, <see cref="Arrive"/>, <see cref="Separation"/>...) returns the force that would turn the
/// velocity towards what it wants; add up the ones that matter (weighted), and <see cref="Move"/> applies the sum.
/// To steer a physics body instead, read its position and velocity into one and apply the force to the body.
/// </summary>
public sealed class Vehicle(Vector2 position, float maxSpeed, float maxForce)
{
    private float _wanderAngle;

    public Vector2 Position { get; set; } = position;

    public Vector2 Velocity { get; set; }

    public float MaxSpeed { get; set; } = maxSpeed;

    /// <summary>The strongest steering force <see cref="Move"/> applies: how sharply it can turn and brake.</summary>
    public float MaxForce { get; set; } = maxForce;

    public float Mass { get; set; } = 1;

    /// <summary>
    /// How quickly it takes up the velocity a behaviour wants, per second: at 8 it closes most of the gap in an eighth
    /// of a second, as far as <see cref="MaxForce"/> allows. Lower is lazier, like a boat; higher, twitchier.
    /// </summary>
    public float Responsiveness { get; set; } = 8;

    /// <summary>Which way it faces: its velocity's direction, or the last one it had while standing still.</summary>
    public Vector2 Heading { get; private set; } = Vector2.UnitX;

    /// <summary>Applies <paramref name="force"/> (capped at <see cref="MaxForce"/>) for <paramref name="seconds"/>, the speed capped at <see cref="MaxSpeed"/>.</summary>
    public void Move(Vector2 force, float seconds)
    {
        Velocity = Cap(Velocity + Cap(force, MaxForce) / Mass * seconds, MaxSpeed);
        Position += Velocity * seconds;
        if (Velocity.LengthSquared() > 1e-8f)
        {
            Heading = Vector2.Normalize(Velocity);
        }
    }

    /// <summary>Full speed straight at <paramref name="target"/>.</summary>
    public Vector2 Seek(Vector2 target) => Toward((target - Position).Normalized() * MaxSpeed);

    /// <summary>Full speed straight away from <paramref name="threat"/>, if it is within <paramref name="panicDistance"/>.</summary>
    public Vector2 Flee(Vector2 threat, float panicDistance = float.PositiveInfinity) =>
        Vector2.DistanceSquared(Position, threat) > panicDistance * panicDistance
            ? Vector2.Zero
            : Toward((Position - threat).Normalized() * MaxSpeed);

    /// <summary>Like <see cref="Seek"/>, but slowing down within <paramref name="slowingRadius"/> to stop on the target.</summary>
    public Vector2 Arrive(Vector2 target, float slowingRadius)
    {
        var offset = target - Position;
        var distance = offset.Length();
        if (distance < 1e-4f)
        {
            return Toward(Vector2.Zero);
        }

        var speed = distance < slowingRadius ? MaxSpeed * distance / slowingRadius : MaxSpeed;
        return Toward(offset / distance * speed);
    }

    /// <summary>Seeks where a moving target will be, judging the time to reach it by the distance.</summary>
    public Vector2 Pursue(Vector2 target, Vector2 targetVelocity) => Seek(Predict(target, targetVelocity));

    /// <summary>Flees from where a moving threat will be.</summary>
    public Vector2 Evade(Vector2 threat, Vector2 threatVelocity, float panicDistance = float.PositiveInfinity) =>
        Flee(Predict(threat, threatVelocity), panicDistance);

    /// <summary>
    /// Ambles about: seeks a point on a circle ahead that drifts a little each call, so the path curves smoothly
    /// rather than jittering. <paramref name="random"/> decides the drift.
    /// </summary>
    public Vector2 Wander(Random random, float distance = 40, float radius = 20, float jitter = 0.4f)
    {
        ArgumentNullException.ThrowIfNull(random);
#pragma warning disable CA5394 // Wandering, not security.
        _wanderAngle += (float)(random.NextDouble() * 2 - 1) * jitter;
#pragma warning restore CA5394
        var circle = Position + Heading * distance;
        return Seek(circle + new Vector2(MathF.Cos(_wanderAngle), MathF.Sin(_wanderAngle)) * radius);
    }

    /// <summary>Away from neighbours closer than <paramref name="radius"/>, the more the closer they are: no crowding.</summary>
    public Vector2 Separation(IEnumerable<Vehicle> neighbours, float radius)
    {
        ArgumentNullException.ThrowIfNull(neighbours);
        var away = Vector2.Zero;
        foreach (var other in neighbours)
        {
            var offset = Position - other.Position;
            var distance = offset.Length();
            if (!ReferenceEquals(other, this) && distance < radius && distance > 1e-4f)
            {
                away += offset / distance * (1 - distance / radius);
            }
        }

        return away == Vector2.Zero ? Vector2.Zero : Toward(away.Normalized() * MaxSpeed);
    }

    /// <summary>Towards the average heading of neighbours within <paramref name="radius"/>.</summary>
    public Vector2 Alignment(IEnumerable<Vehicle> neighbours, float radius)
    {
        var near = Near(neighbours, radius);
        return near.Count == 0 ? Vector2.Zero : Toward(near.Aggregate(Vector2.Zero, (sum, v) => sum + v.Heading).Normalized() * MaxSpeed);
    }

    /// <summary>Towards the middle of neighbours within <paramref name="radius"/>.</summary>
    public Vector2 Cohesion(IEnumerable<Vehicle> neighbours, float radius)
    {
        var near = Near(neighbours, radius);
        return near.Count == 0 ? Vector2.Zero : Seek(near.Aggregate(Vector2.Zero, (sum, v) => sum + v.Position) / near.Count);
    }

    /// <summary>
    /// Flocking: <see cref="Separation"/>, <see cref="Alignment"/> and <see cref="Cohesion"/> together, weighted
    /// (separation strongest, so the flock never clumps), for birds, fish, sheep and swarms.
    /// </summary>
    public Vector2 Flock(IReadOnlyCollection<Vehicle> flock, float radius, float separation = 1.5f, float alignment = 1, float cohesion = 1) =>
        Separation(flock, radius / 2) * separation + Alignment(flock, radius) * alignment + Cohesion(flock, radius) * cohesion;

    /// <summary>
    /// Steers clear of round obstacles in a box <paramref name="lookAhead"/> long in front: away from the nearest one
    /// the path would hit, harder the nearer it is.
    /// </summary>
    public Vector2 Avoid(IEnumerable<(Vector2 Centre, float Radius)> obstacles, float lookAhead, float radius = 0)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        var side = new Vector2(-Heading.Y, Heading.X);
        (Vector2 Centre, float Ahead, float Across)? nearest = null;
        foreach (var (centre, size) in obstacles)
        {
            var offset = centre - Position;
            var ahead = Vector2.Dot(offset, Heading);
            var across = Vector2.Dot(offset, side);
            if (ahead > 0 && ahead < lookAhead + size && MathF.Abs(across) < size + radius && (nearest is null || ahead < nearest.Value.Ahead))
            {
                nearest = (centre, ahead, across);
            }
        }

        if (nearest is not { } hit)
        {
            return Vector2.Zero;
        }

        // Swerve to the side the obstacle is not on, and brake a little.
        var urgency = 1 + (lookAhead - hit.Ahead) / lookAhead;
        var swerve = side * (hit.Across >= 0 ? -1 : 1);
        return (swerve * MaxForce - Heading * MaxForce * 0.2f) * urgency;
    }

    /// <summary>The force that brings the velocity to <paramref name="desired"/> in about 1 / <see cref="Responsiveness"/> seconds.</summary>
    private Vector2 Toward(Vector2 desired) => (desired - Velocity) * Responsiveness * Mass;

    private Vector2 Predict(Vector2 target, Vector2 velocity)
    {
        var ahead = MaxSpeed <= 0 ? 0 : Vector2.Distance(Position, target) / MaxSpeed;
        return target + velocity * ahead;
    }

    private List<Vehicle> Near(IEnumerable<Vehicle> neighbours, float radius)
    {
        ArgumentNullException.ThrowIfNull(neighbours);
        return [.. neighbours.Where(v => !ReferenceEquals(v, this) && Vector2.DistanceSquared(v.Position, Position) < radius * radius)];
    }

    private static Vector2 Cap(Vector2 vector, float max) =>
        vector.LengthSquared() > max * max ? vector.Normalized() * max : vector;
}

internal static class VectorExtensions
{
    /// <summary>The unit vector the same way, or zero for zero.</summary>
    public static Vector2 Normalized(this Vector2 vector) =>
        vector.LengthSquared() < 1e-12f ? Vector2.Zero : Vector2.Normalize(vector);
}
