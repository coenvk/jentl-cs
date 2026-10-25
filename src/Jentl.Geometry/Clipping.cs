using System.Numerics;

namespace Jentl.Geometry;

/// <summary>Cutting polygons by a plane, as a renderer does at the near plane and the screen's edges.</summary>
public static class Clipping
{
    /// <summary>
    /// The part of <paramref name="polygon"/> on the side where <paramref name="distance"/> is zero or more
    /// (Sutherland–Hodgman). Vertices can carry anything (positions, colours, texture coordinates):
    /// <paramref name="lerp"/> makes the new ones where edges cross. A polygon wholly inside comes back unchanged.
    /// </summary>
    public static IReadOnlyList<T> ClipPolygon<T>(IReadOnlyList<T> polygon, Func<T, float> distance, Func<T, T, float, T> lerp)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var result = new List<T>(polygon.Count + 2);
        ClipPolygon(polygon, distance, lerp, result);
        return result;
    }

    /// <summary>
    /// As <see cref="ClipPolygon{T}(IReadOnlyList{T}, Func{T, float}, Func{T, T, float, T})"/>, into
    /// <paramref name="result"/> (cleared first, and not <paramref name="polygon"/> itself), so a renderer can reuse
    /// its lists and make no garbage.
    /// </summary>
    public static void ClipPolygon<T>(IReadOnlyList<T> polygon, Func<T, float> distance, Func<T, T, float, T> lerp, List<T> result)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(distance);
        ArgumentNullException.ThrowIfNull(lerp);
        ArgumentNullException.ThrowIfNull(result);
        result.Clear();
        if (polygon.Count == 0)
        {
            return;
        }

        var previous = polygon[^1];
        var previousDistance = distance(previous);
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var currentDistance = distance(current);
            if (currentDistance >= 0)
            {
                if (previousDistance < 0)
                {
                    result.Add(lerp(previous, current, previousDistance / (previousDistance - currentDistance)));
                }

                result.Add(current);
            }
            else if (previousDistance >= 0)
            {
                result.Add(lerp(previous, current, previousDistance / (previousDistance - currentDistance)));
            }

            (previous, previousDistance) = (current, currentDistance);
        }
    }
}

/// <summary>
/// The edge function of a triangle's side from <paramref name="A"/> to <paramref name="B"/>: zero on the line, positive
/// on its left (y up), and changing by a fixed step per pixel, which is what a rasterizer walks.
/// </summary>
public readonly record struct Edge(Vector2 A, Vector2 B)
{
    /// <summary>How much <see cref="Evaluate"/> changes for one step in x.</summary>
    public float StepX => A.Y - B.Y;

    /// <summary>How much <see cref="Evaluate"/> changes for one step in y.</summary>
    public float StepY => B.X - A.X;

    public float Evaluate(Vector2 point) => ((B.X - A.X) * (point.Y - A.Y)) - ((B.Y - A.Y) * (point.X - A.X));

    /// <summary>
    /// Whether the triangle covers <paramref name="point"/>, either winding. Points exactly on an edge count only for
    /// that edge's "top-left" side, so where two triangles share an edge, each point on it is drawn once.
    /// </summary>
    public static bool Covers(Vector2 a, Vector2 b, Vector2 c, Vector2 point)
    {
        if (Segment2.Cross(b - a, c - a) < 0)
        {
            (b, c) = (c, b);
        }

        return Inside(new Edge(a, b), point) && Inside(new Edge(b, c), point) && Inside(new Edge(c, a), point);
    }

    // On a counter-clockwise triangle (y up), an edge going down is a left edge, one going left along the top a top edge.
    private static bool Inside(Edge edge, Vector2 point)
    {
        var value = edge.Evaluate(point);
        if (value != 0)
        {
            return value > 0;
        }

        var d = edge.B - edge.A;
        return d.Y < 0 || (d.Y == 0 && d.X < 0);
    }
}
