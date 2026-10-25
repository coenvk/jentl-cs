using System.Numerics;

namespace Jentl.Geometry;

/// <summary>A half-line: starts at <paramref name="Origin"/> and goes on along <paramref name="Direction"/>.</summary>
/// <param name="Origin">Where it starts.</param>
/// <param name="Direction">Which way it goes; distances along the ray are in multiples of this vector's length.</param>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    /// <summary>The point <paramref name="distance"/> along the ray.</summary>
    public Vector3 At(float distance) => Origin + (Direction * distance);
}

/// <summary>A straight piece between two points in the plane.</summary>
public readonly record struct Segment2(Vector2 A, Vector2 B)
{
    public float Length => Vector2.Distance(A, B);

    /// <summary>Where this segment crosses <paramref name="other"/>, if it does (parallel segments never cross).</summary>
    public bool Intersect(Segment2 other, out Vector2 at)
    {
        var r = B - A;
        var s = other.B - other.A;
        var denominator = Cross(r, s);
        at = default;
        if (MathF.Abs(denominator) < 1e-9f)
        {
            return false;
        }

        var t = Cross(other.A - A, s) / denominator;
        var u = Cross(other.A - A, r) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1)
        {
            return false;
        }

        at = A + (r * t);
        return true;
    }

    /// <summary>The 2D cross product: positive when <paramref name="b"/> turns left from <paramref name="a"/>.</summary>
    public static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
}

/// <summary>A straight piece between two points in space.</summary>
public readonly record struct Segment3(Vector3 A, Vector3 B)
{
    public float Length => Vector3.Distance(A, B);

    /// <summary>The point of the segment nearest to <paramref name="point"/>.</summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        var d = B - A;
        var lengthSquared = d.LengthSquared();
        if (lengthSquared == 0)
        {
            return A;
        }

        var t = Math.Clamp(Vector3.Dot(point - A, d) / lengthSquared, 0, 1);
        return A + (d * t);
    }
}

/// <summary>Making and using <see cref="Plane"/>s: through points, signed distances, where lines cross them.</summary>
public static class Planes
{
    /// <summary>The plane through <paramref name="point"/> facing <paramref name="normal"/> (normalised).</summary>
    public static Plane Through(Vector3 point, Vector3 normal)
    {
        var n = Vector3.Normalize(normal);
        return new Plane(n, -Vector3.Dot(n, point));
    }

    /// <summary>The plane through three points, facing the side from which they run counter-clockwise.</summary>
    public static Plane Through(Vector3 a, Vector3 b, Vector3 c) => Through(a, Vector3.Cross(b - a, c - a));

    /// <summary>How far <paramref name="point"/> is in front of the plane (negative behind), in units of a normalised plane.</summary>
    public static float DistanceTo(this Plane plane, Vector3 point) => Plane.DotCoordinate(plane, point);

    /// <summary>Where <paramref name="segment"/> crosses the plane, if its ends lie on different sides.</summary>
    public static bool Intersect(this Plane plane, Segment3 segment, out Vector3 at)
    {
        var a = plane.DistanceTo(segment.A);
        var b = plane.DistanceTo(segment.B);
        at = default;
        if ((a > 0 && b > 0) || (a < 0 && b < 0) || a == b)
        {
            return false;
        }

        at = Vector3.Lerp(segment.A, segment.B, a / (a - b));
        return true;
    }

    /// <summary>How far along <paramref name="ray"/> it meets the plane, if it does (ahead of its origin, not parallel).</summary>
    public static bool Intersect(this Plane plane, Ray ray, out float distance)
    {
        var facing = Vector3.Dot(plane.Normal, ray.Direction);
        distance = 0;
        if (MathF.Abs(facing) < 1e-9f)
        {
            return false;
        }

        distance = -plane.DistanceTo(ray.Origin) / facing;
        return distance >= 0;
    }
}

/// <summary>Three corners. Counter-clockwise, seen from the front, is the front: the normal points towards the viewer.</summary>
public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C)
{
    /// <summary>The unit normal of the front face (zero for a degenerate triangle).</summary>
    public Vector3 Normal
    {
        get
        {
            var cross = Vector3.Cross(B - A, C - A);
            var length = cross.Length();
            return length > 0 ? cross / length : Vector3.Zero;
        }
    }

    public float Area => Vector3.Cross(B - A, C - A).Length() / 2;

    public Vector3 Centroid => (A + B + C) / 3;

    /// <summary>The weights of A, B and C that make <paramref name="point"/> (projected onto the triangle's plane).</summary>
    public Vector3 Barycentric(Vector3 point)
    {
        Vector3 e0 = B - A, e1 = C - A, p = point - A;
        float d00 = Vector3.Dot(e0, e0), d01 = Vector3.Dot(e0, e1), d11 = Vector3.Dot(e1, e1);
        float d20 = Vector3.Dot(p, e0), d21 = Vector3.Dot(p, e1);
        var denominator = (d00 * d11) - (d01 * d01);
        if (denominator == 0)
        {
            return new Vector3(float.NaN);
        }

        var v = ((d11 * d20) - (d01 * d21)) / denominator;
        var w = ((d00 * d21) - (d01 * d20)) / denominator;
        return new Vector3(1 - v - w, v, w);
    }

    /// <summary>Whether <paramref name="point"/> lies on the triangle (within a small tolerance of its plane and edges).</summary>
    public bool Contains(Vector3 point, float tolerance = 1e-5f)
    {
        if (MathF.Abs(Vector3.Dot(point - A, Normal)) > tolerance)
        {
            return false;
        }

        var w = Barycentric(point);
        return w.X >= -tolerance && w.Y >= -tolerance && w.Z >= -tolerance;
    }

    /// <summary>How far along <paramref name="ray"/> it hits the triangle, from either side (Möller–Trumbore).</summary>
    public bool Intersect(Ray ray, out float distance)
    {
        distance = 0;
        Vector3 e1 = B - A, e2 = C - A;
        var p = Vector3.Cross(ray.Direction, e2);
        var determinant = Vector3.Dot(e1, p);
        if (MathF.Abs(determinant) < 1e-9f)
        {
            return false;
        }

        var inverse = 1 / determinant;
        var s = ray.Origin - A;
        var u = Vector3.Dot(s, p) * inverse;
        if (u < 0 || u > 1)
        {
            return false;
        }

        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(ray.Direction, q) * inverse;
        if (v < 0 || u + v > 1)
        {
            return false;
        }

        distance = Vector3.Dot(e2, q) * inverse;
        return distance > 1e-7f;
    }
}

/// <summary>The three corners of a triangle in a mesh, as indices into its vertices.</summary>
public readonly record struct TriangleIndices(int A, int B, int C);
