using System.Numerics;

namespace Jentl.Physics;

/// <summary>Two bodies touching: <see cref="Normal"/> points from <see cref="A"/> to <see cref="B"/>, and they overlap by <see cref="Depth"/>.</summary>
public readonly record struct Contact(Body A, Body B, Vector2 Normal, float Depth, Vector2 Point)
{
    /// <summary>The other body, seen from <paramref name="body"/>.</summary>
    public Body Other(Body body) => ReferenceEquals(body, A) ? B : A;

    /// <summary>The normal pointing away from <paramref name="body"/>, towards the other one.</summary>
    public Vector2 NormalFrom(Body body) => ReferenceEquals(body, A) ? Normal : -Normal;
}

/// <summary>Where a ray first met a body.</summary>
public readonly record struct RayHit(Body Body, Vector2 Point, Vector2 Normal, float Distance);

/// <summary>Overlap tests between shapes.</summary>
internal static class Collision
{
    /// <summary>The contact between two bodies, if their shapes overlap.</summary>
    public static Contact? Test(Body a, Body b) => (a.Shape, b.Shape) switch
    {
        (Circle ca, Circle cb) => Circles(a, ca.Radius, b, cb.Radius),
        (Box ba, Box bb) => Boxes(a, ba.HalfSize, b, bb.HalfSize),
        (Box ba, Circle cb) => BoxCircle(a, ba.HalfSize, b, cb.Radius),
        (Circle, Box) => Test(b, a) is { } flipped ? flipped with { A = a, B = b, Normal = -flipped.Normal } : null,
        _ => throw new NotSupportedException($"No collision test for {a.Shape.GetType().Name} and {b.Shape.GetType().Name}."),
    };

    public static float? Ray(Body body, Vector2 origin, Vector2 direction, out Vector2 normal) => body.Shape switch
    {
        Circle circle => RayCircle(origin, direction, body.Position, circle.Radius, out normal),
        Box box => RayBox(origin, direction, body.Bounds, out normal),
        _ => throw new NotSupportedException($"No ray test for {body.Shape.GetType().Name}."),
    };

    public static bool Contains(Body body, Vector2 point) => body.Shape switch
    {
        Circle circle => Vector2.DistanceSquared(point, body.Position) <= circle.Radius * circle.Radius,
        Box => body.Bounds.Contains(point),
        _ => false,
    };

    private static Contact? Circles(Body a, float ra, Body b, float rb)
    {
        var between = b.Position - a.Position;
        var reach = ra + rb;
        var distanceSquared = between.LengthSquared();
        if (distanceSquared >= reach * reach)
        {
            return null;
        }

        var distance = MathF.Sqrt(distanceSquared);
        var normal = distance > 1e-6f ? between / distance : Vector2.UnitY;
        return new Contact(a, b, normal, reach - distance, a.Position + normal * ra);
    }

    private static Contact? Boxes(Body a, Vector2 ha, Body b, Vector2 hb)
    {
        var between = b.Position - a.Position;
        var overlapX = ha.X + hb.X - MathF.Abs(between.X);
        var overlapY = ha.Y + hb.Y - MathF.Abs(between.Y);
        if (overlapX <= 0 || overlapY <= 0)
        {
            return null;
        }

        // Push apart along the axis they overlap least on.
        var overlap = Aabb.FromCenter(a.Position, ha) is var boxA && Aabb.FromCenter(b.Position, hb) is var boxB
            ? new Aabb(Vector2.Max(boxA.Min, boxB.Min), Vector2.Min(boxA.Max, boxB.Max)).Center
            : default;
        return overlapX < overlapY
            ? new Contact(a, b, new Vector2(Sign(between.X), 0), overlapX, overlap)
            : new Contact(a, b, new Vector2(0, Sign(between.Y)), overlapY, overlap);
    }

    private static Contact? BoxCircle(Body box, Vector2 half, Body circle, float radius)
    {
        var local = circle.Position - box.Position;
        var closest = Vector2.Clamp(local, -half, half);
        if (closest != local)
        {
            var away = local - closest;
            var distanceSquared = away.LengthSquared();
            if (distanceSquared >= radius * radius)
            {
                return null;
            }

            var distance = MathF.Sqrt(distanceSquared);
            return new Contact(box, circle, away / distance, radius - distance, box.Position + closest);
        }

        // The centre is inside the box: leave through the nearest side.
        var toSideX = half.X - MathF.Abs(local.X);
        var toSideY = half.Y - MathF.Abs(local.Y);
        var normal = toSideX < toSideY ? new Vector2(Sign(local.X), 0) : new Vector2(0, Sign(local.Y));
        return new Contact(box, circle, normal, radius + MathF.Min(toSideX, toSideY), circle.Position);
    }

    private static float? RayCircle(Vector2 origin, Vector2 direction, Vector2 centre, float radius, out Vector2 normal)
    {
        normal = default;
        var toOrigin = origin - centre;
        var b = Vector2.Dot(toOrigin, direction);
        var c = toOrigin.LengthSquared() - radius * radius;
        if (c > 0 && b > 0)
        {
            return null;
        }

        var discriminant = b * b - c;
        if (discriminant < 0)
        {
            return null;
        }

        var t = MathF.Max(0, -b - MathF.Sqrt(discriminant));
        var hit = origin + direction * t;
        normal = hit == centre ? -direction : Vector2.Normalize(hit - centre);
        return t;
    }

    private static float? RayBox(Vector2 origin, Vector2 direction, Aabb box, out Vector2 normal)
    {
        normal = default;
        var near = float.NegativeInfinity;
        var far = float.PositiveInfinity;
        var nearNormal = Vector2.Zero;
        for (var axis = 0; axis < 2; axis++)
        {
            var o = axis == 0 ? origin.X : origin.Y;
            var d = axis == 0 ? direction.X : direction.Y;
            var min = axis == 0 ? box.Min.X : box.Min.Y;
            var max = axis == 0 ? box.Max.X : box.Max.Y;
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < min || o > max)
                {
                    return null;
                }

                continue;
            }

            var t1 = (min - o) / d;
            var t2 = (max - o) / d;
            var side = -MathF.Sign(d);
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            if (t1 > near)
            {
                near = t1;
                nearNormal = axis == 0 ? new Vector2(side, 0) : new Vector2(0, side);
            }

            far = MathF.Min(far, t2);
            if (near > far)
            {
                return null;
            }
        }

        if (far < 0)
        {
            return null;
        }

        // Starting inside the box counts as hitting it straight away.
        normal = near >= 0 ? nearNormal : -direction;
        return MathF.Max(0, near);
    }

    private static float Sign(float value) => value < 0 ? -1 : 1;
}
