using System.Numerics;

namespace Jentl.Traffic;

/// <summary>
/// The line a lane's vehicles follow: a polyline measured along its length, in metres, with x east and y north.
/// Straight lines, Bézier curves for turns, and circle arcs for roundabouts are made from it.
/// </summary>
public sealed class LanePath
{
    private readonly Vector2[] _points;
    private readonly float[] _along;

    public LanePath(IReadOnlyList<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new ArgumentException("A path needs two points or more.", nameof(points));
        }

        _points = [.. points];
        _along = new float[_points.Length];
        for (var i = 1; i < _points.Length; i++)
        {
            _along[i] = _along[i - 1] + Vector2.Distance(_points[i - 1], _points[i]);
        }

        Length = _along[^1];
    }

    public IReadOnlyList<Vector2> Points => _points;

    public float Length { get; }

    public Vector2 Start => _points[0];

    public Vector2 End => _points[^1];

    /// <summary>The way it leaves its start, unit length.</summary>
    public Vector2 StartDirection => Direction(0);

    /// <summary>The way it arrives at its end, unit length.</summary>
    public Vector2 EndDirection => Direction(_points.Length - 2);

    public static LanePath Line(Vector2 from, Vector2 to) => new([from, to]);

    /// <summary>A cubic Bézier curve, as <paramref name="segments"/> straight pieces.</summary>
    public static LanePath Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segments = 16)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            var u = 1 - t;
            points[i] = (u * u * u * p0) + (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t * p3);
        }

        return new LanePath(points);
    }

    /// <summary>
    /// A curve from <paramref name="from"/> heading <paramref name="fromDirection"/> to <paramref name="to"/> arriving
    /// heading <paramref name="toDirection"/>: what a turn through a junction follows.
    /// </summary>
    public static LanePath Turn(Vector2 from, Vector2 fromDirection, Vector2 to, Vector2 toDirection)
    {
        var reach = Vector2.Distance(from, to) * 0.4f;
        return Bezier(from, from + (fromDirection * reach), to - (toDirection * reach), to);
    }

    /// <summary>An arc of a circle, anticlockwise from <paramref name="fromAngle"/> to <paramref name="toAngle"/> (radians).</summary>
    public static LanePath Arc(Vector2 centre, float radius, float fromAngle, float toAngle)
    {
        while (toAngle <= fromAngle)
        {
            toAngle += MathF.Tau;
        }

        var segments = Math.Max(2, (int)MathF.Ceiling((toAngle - fromAngle) / 0.12f));
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = fromAngle + ((toAngle - fromAngle) * i / segments);
            points[i] = centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        return new LanePath(points);
    }

    /// <summary>The point and the way the path heads (unit length) <paramref name="distance"/> metres along it.</summary>
    public (Vector2 Position, Vector2 Direction) At(float distance)
    {
        var d = Math.Clamp(distance, 0, Length);
        var i = Array.BinarySearch(_along, d);
        if (i < 0)
        {
            i = ~i - 1;
        }

        i = Math.Clamp(i, 0, _points.Length - 2);
        var span = _along[i + 1] - _along[i];
        var t = span > 0 ? (d - _along[i]) / span : 0;
        return (Vector2.Lerp(_points[i], _points[i + 1], t), Direction(i));
    }

    /// <summary>The same path moved sideways: positive to the right of the way it goes.</summary>
    public LanePath Offset(float right)
    {
        var points = new Vector2[_points.Length];
        for (var i = 0; i < _points.Length; i++)
        {
            // At each corner, the average of the pieces either side.
            var before = i > 0 ? Direction(i - 1) : Direction(0);
            var after = i < _points.Length - 1 ? Direction(i) : before;
            var normal = RightOf(Vector2.Normalize(before + after));
            points[i] = _points[i] + (normal * right);
        }

        return new LanePath(points);
    }

    /// <summary>The part between two distances along it.</summary>
    public LanePath Between(float from, float to)
    {
        from = Math.Clamp(from, 0, Length);
        to = Math.Clamp(to, from + 0.01f, Length);
        var points = new List<Vector2> { At(from).Position };
        for (var i = 0; i < _points.Length; i++)
        {
            if (_along[i] > from + 0.01f && _along[i] < to - 0.01f)
            {
                points.Add(_points[i]);
            }
        }

        points.Add(At(to).Position);
        return new LanePath(points);
    }

    /// <summary>The same path the other way round.</summary>
    public LanePath Reversed() => new([.. _points.Reverse()]);

    /// <summary>The tightest bend's radius, in metres (infinite when straight).</summary>
    public float MinRadius()
    {
        var tightest = float.PositiveInfinity;
        for (var i = 1; i < _points.Length - 1; i++)
        {
            var turn = MathF.Acos(Math.Clamp(Vector2.Dot(Direction(i - 1), Direction(i)), -1, 1));
            if (turn > 1e-3f)
            {
                var piece = (Vector2.Distance(_points[i - 1], _points[i]) + Vector2.Distance(_points[i], _points[i + 1])) / 2;
                tightest = MathF.Min(tightest, piece / turn);
            }
        }

        return tightest;
    }

    /// <summary>Where two paths first cross, as distances along each; false if they don't.</summary>
    public static bool Cross(LanePath a, LanePath b, out float alongA, out float alongB)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        for (var i = 0; i < a._points.Length - 1; i++)
        {
            for (var j = 0; j < b._points.Length - 1; j++)
            {
                if (Segments(a._points[i], a._points[i + 1], b._points[j], b._points[j + 1], out var t, out var u))
                {
                    alongA = a._along[i] + (t * (a._along[i + 1] - a._along[i]));
                    alongB = b._along[j] + (u * (b._along[j + 1] - b._along[j]));
                    return true;
                }
            }
        }

        (alongA, alongB) = (0, 0);
        return false;
    }

    /// <summary>The right-hand side of a direction (x east, y north).</summary>
    public static Vector2 RightOf(Vector2 direction) => new(direction.Y, -direction.X);

    private Vector2 Direction(int piece) => Vector2.Normalize(_points[piece + 1] - _points[piece]);

    private static bool Segments(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float t, out float u)
    {
        var r = p2 - p;
        var s = q2 - q;
        var denominator = (r.X * s.Y) - (r.Y * s.X);
        (t, u) = (0, 0);
        if (MathF.Abs(denominator) < 1e-9f)
        {
            return false;
        }

        var qp = q - p;
        t = ((qp.X * s.Y) - (qp.Y * s.X)) / denominator;
        u = ((qp.X * r.Y) - (qp.Y * r.X)) / denominator;

        // Touching at the very ends (paths that meet where one starts) is not a crossing.
        return t > 0.001f && t < 0.999f && u > 0.001f && u < 0.999f;
    }
}
