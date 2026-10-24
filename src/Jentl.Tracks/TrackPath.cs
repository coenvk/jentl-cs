using System.Numerics;

namespace Jentl.Tracks;

/// <summary>
/// A path made of straights and arcs, for race tracks and roads: the position, heading and curvature at any distance
/// along it. A path whose end meets its start is closed (a circuit) and wraps; an open one (a drag strip, a hill
/// climb) stops at its ends.
/// </summary>
public sealed class TrackPath
{
    private readonly PathSegment[] _segments;
    private readonly double[] _starts;
    private readonly (double X, double Y, double Heading)[] _poses;

    private TrackPath(PathSegment[] segments, double[] starts, (double X, double Y, double Heading)[] poses, double length)
    {
        _segments = segments;
        _starts = starts;
        _poses = poses;
        Length = length;
        var end = Evaluate(segments.Length - 1, segments[^1].Length);
        IsClosed = segments.Length > 0 && Vector2.Distance(end.Position, new Vector2((float)poses[0].X, (float)poses[0].Y)) < 1;
        Bounds = MeasureBounds();
    }

    /// <summary>The path's total length.</summary>
    public double Length { get; }

    /// <summary>Whether the end meets the start (within one unit), so distances wrap round.</summary>
    public bool IsClosed { get; }

    /// <summary>The smallest box around the path's centre line.</summary>
    public (Vector2 Min, Vector2 Max) Bounds { get; }

    /// <summary>Lays out <paramref name="segments"/> one after another.</summary>
    /// <param name="segments">The pieces, at least one.</param>
    /// <param name="start">Where the path starts.</param>
    /// <param name="heading">The direction it starts in (radians, 0 along +X).</param>
    public static TrackPath Build(IReadOnlyList<PathSegment> segments, Vector2 start = default, float heading = 0)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException("A path needs at least one segment.", nameof(segments));
        }

        var pieces = segments.ToArray();
        var starts = new double[pieces.Length];
        var poses = new (double X, double Y, double Heading)[pieces.Length];
        var pose = ((double)start.X, (double)start.Y, (double)heading);
        var distance = 0.0;
        for (var i = 0; i < pieces.Length; i++)
        {
            starts[i] = distance;
            poses[i] = pose;
            pose = Advance(pieces[i], pose, pieces[i].Length);
            distance += pieces[i].Length;
        }

        return new TrackPath(pieces, starts, poses, distance);
    }

    /// <summary>The position and heading at <paramref name="distance"/> along the path.</summary>
    public PathPose At(double distance)
    {
        var (index, along) = Locate(distance);
        return Evaluate(index, along);
    }

    /// <summary>The curvature at <paramref name="distance"/>: 1/radius, positive in left turns, 0 on straights.</summary>
    public double CurvatureAt(double distance)
    {
        var (index, _) = Locate(distance);
        return _segments[index] is Arc arc ? Math.Sign(arc.Degrees) / arc.Radius : 0;
    }

    /// <summary>Points along the path every <paramref name="step"/> units, both ends included.</summary>
    public IReadOnlyList<Vector2> Polyline(double step)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        var count = (int)Math.Ceiling(Length / step);
        var points = new List<Vector2>(count + 1);
        for (var i = 0; i < count; i++)
        {
            points.Add(At(Math.Min(i * step, Length)).Position);
        }

        points.Add(Evaluate(_segments.Length - 1, _segments[^1].Length).Position);
        return points;
    }

    private (int Index, double Along) Locate(double distance)
    {
        if (IsClosed)
        {
            distance %= Length;
            if (distance < 0)
            {
                distance += Length;
            }
        }
        else
        {
            distance = Math.Clamp(distance, 0, Length);
        }

        var index = Array.BinarySearch(_starts, distance);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, _segments.Length - 1);
        return (index, distance - _starts[index]);
    }

    private PathPose Evaluate(int index, double along)
    {
        var (x, y, heading) = Advance(_segments[index], _poses[index], along);
        return new PathPose(new Vector2((float)x, (float)y), (float)heading);
    }

    private static (double X, double Y, double Heading) Advance(PathSegment segment, (double X, double Y, double Heading) pose, double along)
    {
        var (x, y, h) = pose;
        if (segment is not Arc arc)
        {
            return (x + (Math.Cos(h) * along), y + (Math.Sin(h) * along), h);
        }

        // A left turn (positive degrees) turns towards negative y; the centre lies to that side.
        var turn = along / arc.Radius * Math.Sign(arc.Degrees);
        var r = arc.Radius;
        if (arc.Degrees >= 0)
        {
            var (cx, cy) = (x + (r * Math.Sin(h)), y - (r * Math.Cos(h)));
            var heading = h - turn;
            return (cx - (r * Math.Sin(heading)), cy + (r * Math.Cos(heading)), heading);
        }
        else
        {
            var (cx, cy) = (x - (r * Math.Sin(h)), y + (r * Math.Cos(h)));
            var heading = h - turn;
            return (cx + (r * Math.Sin(heading)), cy - (r * Math.Cos(heading)), heading);
        }
    }

    private (Vector2 Min, Vector2 Max) MeasureBounds()
    {
        var points = Polyline(Math.Max(Length / 2000, 0.5));
        return (points.Aggregate(Vector2.Min), points.Aggregate(Vector2.Max));
    }
}
