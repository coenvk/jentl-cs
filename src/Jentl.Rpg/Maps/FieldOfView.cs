using Jentl.Puzzle;

namespace Jentl.Rpg.Maps;

/// <summary>
/// What can be seen from a cell: recursive shadowcasting over the eight octants, so walls cast proper
/// shadows and the result is symmetric enough for a roguelike. Opaque cells at the edge of sight are
/// visible themselves (you see the wall, not what is behind it).
/// </summary>
public static class FieldOfView
{
    // Transforms from octant coordinates (row, column) to map offsets, one per octant.
    private static readonly (int Xx, int Xy, int Yx, int Yy)[] Octants =
    [
        (1, 0, 0, 1), (0, 1, 1, 0), (0, -1, 1, 0), (-1, 0, 0, 1),
        (-1, 0, 0, -1), (0, -1, -1, 0), (0, 1, -1, 0), (1, 0, 0, -1),
    ];

    /// <summary>The cells visible from <paramref name="origin"/> within <paramref name="radius"/> (a circle).</summary>
    public static HashSet<Cell> Compute(Cell origin, int radius, Func<Cell, bool> blocksSight)
    {
        ArgumentNullException.ThrowIfNull(blocksSight);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        var visible = new HashSet<Cell> { origin };
        foreach (var octant in Octants)
        {
            Cast(origin, radius, 1, 1.0, 0.0, octant, blocksSight, visible);
        }

        return visible;
    }

    /// <summary>Whether nothing opaque lies on the straight line between two cells (the ends do not count).</summary>
    public static bool HasLineOfSight(Cell from, Cell to, Func<Cell, bool> blocksSight)
    {
        ArgumentNullException.ThrowIfNull(blocksSight);
        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
        int sx = Math.Sign(to.X - x), sy = Math.Sign(to.Y - y);
        var error = dx + dy;
        while (x != to.X || y != to.Y)
        {
            var doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y += sy;
            }

            if ((x != to.X || y != to.Y) && blocksSight(new Cell(x, y)))
            {
                return false;
            }
        }

        return true;
    }

    private static void Cast(
        Cell origin,
        int radius,
        int row,
        double start,
        double end,
        (int Xx, int Xy, int Yx, int Yy) octant,
        Func<Cell, bool> blocksSight,
        HashSet<Cell> visible)
    {
        if (start < end)
        {
            return;
        }

        var radiusSquared = radius * radius;
        for (var distance = row; distance <= radius; distance++)
        {
            var blocked = false;
            var nextStart = start;
            for (var dx = -distance; dx <= 0; dx++)
            {
                var dy = -distance;
                var leftSlope = (dx - 0.5) / (dy + 0.5);
                var rightSlope = (dx + 0.5) / (dy - 0.5);
                if (start < rightSlope)
                {
                    continue;
                }

                if (end > leftSlope)
                {
                    break;
                }

                var cell = new Cell(origin.X + dx * octant.Xx + dy * octant.Xy, origin.Y + dx * octant.Yx + dy * octant.Yy);
                if (dx * dx + dy * dy <= radiusSquared)
                {
                    visible.Add(cell);
                }

                var opaque = blocksSight(cell);
                if (blocked)
                {
                    if (opaque)
                    {
                        nextStart = rightSlope;
                        continue;
                    }

                    blocked = false;
                    start = nextStart;
                }
                else if (opaque && distance < radius)
                {
                    blocked = true;
                    Cast(origin, radius, distance + 1, start, leftSlope, octant, blocksSight, visible);
                    nextStart = rightSlope;
                }
            }

            if (blocked)
            {
                break;
            }
        }
    }
}
