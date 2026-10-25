using System.Numerics;

namespace Jentl.Geometry;

/// <summary>Polygons in the plane, as lists of corners in order (y up: counter-clockwise has a positive area).</summary>
public static class Polygon2
{
    /// <summary>The area, positive when the corners run counter-clockwise (shoelace formula).</summary>
    public static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var sum = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            sum += Segment2.Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        }

        return sum / 2;
    }

    public static bool IsCounterClockwise(IReadOnlyList<Vector2> polygon) => SignedArea(polygon) > 0;

    /// <summary>Whether <paramref name="point"/> lies inside (even-odd rule, so it works for concave polygons).</summary>
    public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (a, b) = (polygon[i], polygon[j]);
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// A convex polygon with every side moved <paramref name="distance"/> inwards (outwards if negative), counter-clockwise:
    /// a window inside its frame, a border inside a panel. The distance must leave something of the polygon.
    /// </summary>
    public static IReadOnlyList<Vector2> Inset(IReadOnlyList<Vector2> polygon, float distance)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var points = IsCounterClockwise(polygon) ? polygon.ToList() : polygon.Reverse().ToList();
        var n = points.Count;

        // Each side's line moved in along its inward normal (to the left, counter-clockwise), as a point and a direction.
        var lines = new (Vector2 Point, Vector2 Direction)[n];
        for (var i = 0; i < n; i++)
        {
            var d = Vector2.Normalize(points[(i + 1) % n] - points[i]);
            lines[i] = (points[i] + (new Vector2(-d.Y, d.X) * distance), d);
        }

        // Each corner is where the lines of its two sides now meet.
        var result = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (lines[(i + n - 1) % n], lines[i]);
            var cross = Segment2.Cross(a.Direction, b.Direction);
            result[i] = MathF.Abs(cross) < 1e-9f
                ? b.Point
                : a.Point + (a.Direction * (Segment2.Cross(b.Point - a.Point, b.Direction) / cross));
        }

        return result;
    }

    /// <summary>
    /// The outline of a picture's solid pixels (e.g. a sprite's opaque ones), along the pixel edges with only its
    /// corners, in pixel units with y down as the picture runs: pixel (x, y) is the square from (x, y) to (x + 1, y + 1).
    /// The biggest piece is kept (pieces touching only at a corner are apart) and holes in it are filled, so the outline
    /// is one simple polygon, ready for <see cref="Triangulate"/> or <see cref="MeshBuilder.Extrude"/>. Empty if no
    /// pixel is solid.
    /// </summary>
    public static IReadOnlyList<Vector2> Outline(int width, int height, Func<int, int, bool> solid)
    {
        ArgumentNullException.ThrowIfNull(solid);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        // A border of empty pixels round the picture, so every piece has outside all round it.
        var (w, h) = (width + 2, height + 2);
        var grid = new bool[w * h];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                grid[((y + 1) * w) + x + 1] = solid(x, y);
            }
        }

        // The biggest piece, then everything not reachable from the border without crossing it (the piece and its holes).
        var piece = new bool[grid.Length];
        var best = 0;
        var seen = new bool[grid.Length];
        for (var i = 0; i < grid.Length; i++)
        {
            if (grid[i] && !seen[i])
            {
                var found = Flood(w, h, i, j => grid[j], seen);
                if (found.Count > best)
                {
                    best = found.Count;
                    Array.Clear(piece);
                    found.ForEach(j => piece[j] = true);
                }
            }
        }

        if (best == 0)
        {
            return [];
        }

        var outside = new bool[grid.Length];
        Flood(w, h, 0, j => !piece[j], outside);

        // Each solid pixel's sides that face outside, all running the same way round; with holes filled and pieces
        // joined by sides only, every corner has one way on (a corner touch would close off a gap, which is filled).
        var next = new Dictionary<(int X, int Y), (int X, int Y)>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (outside[(y * w) + x])
                {
                    continue;
                }

                if (outside[((y - 1) * w) + x])
                {
                    next[(x, y)] = (x + 1, y);
                }

                if (outside[(y * w) + x + 1])
                {
                    next[(x + 1, y)] = (x + 1, y + 1);
                }

                if (outside[((y + 1) * w) + x])
                {
                    next[(x + 1, y + 1)] = (x, y + 1);
                }

                if (outside[(y * w) + x - 1])
                {
                    next[(x, y + 1)] = (x, y);
                }
            }
        }

        // Walk round once, keeping only the corners where the way turns; back out of the border's offset.
        var start = next.Keys.Min();
        var path = new List<(int X, int Y)>();
        var at = start;
        do
        {
            path.Add(at);
            at = next[at];
        }
        while (at != start);

        var corners = new List<Vector2>();
        for (var i = 0; i < path.Count; i++)
        {
            var (p, c, n) = (path[(i + path.Count - 1) % path.Count], path[i], path[(i + 1) % path.Count]);
            if ((c.X - p.X, c.Y - p.Y) != (n.X - c.X, n.Y - c.Y))
            {
                corners.Add(new Vector2(c.X - 1, c.Y - 1));
            }
        }

        return corners;
    }

    // The pixels reachable from `from` through side neighbours that pass `open`, marked in `seen`.
    private static List<int> Flood(int width, int height, int from, Func<int, bool> open, bool[] seen)
    {
        var found = new List<int>();
        var stack = new Stack<int>();
        stack.Push(from);
        seen[from] = true;
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            found.Add(i);
            var (x, y) = (i % width, i / width);
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                var j = (ny * width) + nx;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && !seen[j] && open(j))
                {
                    seen[j] = true;
                    stack.Push(j);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Cuts a simple polygon (concave is fine, no holes or self-crossings) into n - 2 triangles by ear clipping. The
    /// triangles index the input and always run counter-clockwise, whichever way the input runs.
    /// </summary>
    public static IReadOnlyList<TriangleIndices> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var order = Enumerable.Range(0, polygon.Count).ToList();
        if (!IsCounterClockwise(polygon))
        {
            order.Reverse();
        }

        var triangles = new List<TriangleIndices>();
        var guard = polygon.Count * polygon.Count;
        var i = 0;
        while (order.Count > 3 && guard-- > 0)
        {
            var (p, c, n) = (order[(i + order.Count - 1) % order.Count], order[i % order.Count], order[(i + 1) % order.Count]);
            if (IsEar(polygon, order, p, c, n))
            {
                triangles.Add(new TriangleIndices(p, c, n));
                order.RemoveAt(i % order.Count);
                i = 0;
            }
            else
            {
                i++;
            }
        }

        if (order.Count == 3)
        {
            triangles.Add(new TriangleIndices(order[0], order[1], order[2]));
        }

        return triangles;
    }

    // A convex corner whose triangle holds no other remaining corner.
    private static bool IsEar(IReadOnlyList<Vector2> polygon, List<int> order, int p, int c, int n)
    {
        Vector2 a = polygon[p], b = polygon[c], d = polygon[n];
        if (Segment2.Cross(b - a, d - b) <= 0)
        {
            return false;
        }

        foreach (var k in order)
        {
            if (k != p && k != c && k != n && InTriangle(polygon[k], a, b, d))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c) =>
        Segment2.Cross(b - a, point - a) >= 0 && Segment2.Cross(c - b, point - b) >= 0 && Segment2.Cross(a - c, point - c) >= 0;
}

/// <summary>An axis-aligned box in the plane, from <paramref name="Min"/> to <paramref name="Max"/> (edges included).</summary>
public readonly record struct Box2(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;

    public Vector2 Center => (Min + Max) / 2;

    public static Box2 FromPoints(IEnumerable<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.ToList();
        return new Box2(list.Aggregate(Vector2.Min), list.Aggregate(Vector2.Max));
    }

    public bool Contains(Vector2 point) => point.X >= Min.X && point.Y >= Min.Y && point.X <= Max.X && point.Y <= Max.Y;

    /// <summary>Whether the boxes overlap with some area (boxes only touching at an edge don't).</summary>
    public bool Intersects(Box2 other) => Min.X < other.Max.X && other.Min.X < Max.X && Min.Y < other.Max.Y && other.Min.Y < Max.Y;

    public Box2 Union(Box2 other) => new(Vector2.Min(Min, other.Min), Vector2.Max(Max, other.Max));
}

/// <summary>An axis-aligned box in space, from <paramref name="Min"/> to <paramref name="Max"/>.</summary>
public readonly record struct Box3(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 Center => (Min + Max) / 2;

    public static Box3 FromPoints(IEnumerable<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.ToList();
        return new Box3(list.Aggregate(Vector3.Min), list.Aggregate(Vector3.Max));
    }

    public bool Contains(Vector3 point) =>
        point.X >= Min.X && point.Y >= Min.Y && point.Z >= Min.Z && point.X <= Max.X && point.Y <= Max.Y && point.Z <= Max.Z;

    public Box3 Union(Box3 other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>The eight corners.</summary>
    public IReadOnlyList<Vector3> Corners()
    {
        var (a, b) = (Min, Max);
        return
        [
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z),
        ];
    }

    /// <summary>The box around this box's corners once transformed (so it grows when rotated).</summary>
    public Box3 Transform(Matrix4x4 matrix) => FromPoints(Corners().Select(c => Vector3.Transform(c, matrix)));

    /// <summary>How far along <paramref name="ray"/> it enters the box (0 when it starts inside), if it does (slab test).</summary>
    public bool Intersect(Ray ray, out float distance)
    {
        var near = 0f;
        var far = float.PositiveInfinity;
        distance = 0;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = ray.Origin[axis];
            var direction = ray.Direction[axis];
            if (MathF.Abs(direction) < 1e-12f)
            {
                if (origin < Min[axis] || origin > Max[axis])
                {
                    return false;
                }

                continue;
            }

            var t1 = (Min[axis] - origin) / direction;
            var t2 = (Max[axis] - origin) / direction;
            near = MathF.Max(near, MathF.Min(t1, t2));
            far = MathF.Min(far, MathF.Max(t1, t2));
            if (near > far)
            {
                return false;
            }
        }

        distance = near;
        return true;
    }
}

/// <summary>How a shape lies against a <see cref="Frustum"/>.</summary>
public enum Containment
{
    Outside,
    Intersects,
    Inside,
}

/// <summary>
/// What a camera can see: six planes facing inwards, taken from a view-projection matrix (System.Numerics
/// conventions: row vectors, depth 0 to 1). Use it to skip objects that are out of sight.
/// </summary>
public sealed class Frustum
{
    private readonly Plane[] _planes;

    private Frustum(Plane[] planes) => _planes = planes;

    /// <summary>Left, right, bottom, top, near and far, each facing into the view.</summary>
    public IReadOnlyList<Plane> Planes => _planes;

    /// <summary>The frustum of <paramref name="viewProjection"/> (view × projection).</summary>
    public static Frustum From(Matrix4x4 viewProjection)
    {
        var m = viewProjection;
        Vector4 x = new(m.M11, m.M21, m.M31, m.M41), y = new(m.M12, m.M22, m.M32, m.M42);
        Vector4 z = new(m.M13, m.M23, m.M33, m.M43), w = new(m.M14, m.M24, m.M34, m.M44);
        Plane Make(Vector4 v) => Plane.Normalize(new Plane(v.X, v.Y, v.Z, v.W));
        return new Frustum([Make(w + x), Make(w - x), Make(w + y), Make(w - y), Make(z), Make(w - z)]);
    }

    public bool Contains(Vector3 point) => _planes.All(p => p.DistanceTo(point) >= 0);

    /// <summary>Whether <paramref name="box"/> is fully in view, partly, or not at all (a box near a corner may be called partly when it is outside).</summary>
    public Containment Test(Box3 box)
    {
        var result = Containment.Inside;
        foreach (var plane in _planes)
        {
            // The corner furthest along the normal, and the one furthest against it.
            var n = plane.Normal;
            var positive = new Vector3(n.X >= 0 ? box.Max.X : box.Min.X, n.Y >= 0 ? box.Max.Y : box.Min.Y, n.Z >= 0 ? box.Max.Z : box.Min.Z);
            var negative = new Vector3(n.X >= 0 ? box.Min.X : box.Max.X, n.Y >= 0 ? box.Min.Y : box.Max.Y, n.Z >= 0 ? box.Min.Z : box.Max.Z);
            if (plane.DistanceTo(positive) < 0)
            {
                return Containment.Outside;
            }

            if (plane.DistanceTo(negative) < 0)
            {
                result = Containment.Intersects;
            }
        }

        return result;
    }
}
