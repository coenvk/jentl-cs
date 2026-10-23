using Jentl.Puzzle;

namespace Jentl.Rpg.Maps;

/// <summary>Shortest paths on a grid with four-way movement (A* with the Manhattan distance).</summary>
public static class Pathfinder
{
    /// <summary>
    /// The cells to walk from <paramref name="start"/> to <paramref name="goal"/>, excluding the start and
    /// including the goal; null when there is no way within <paramref name="maxSearch"/> explored cells.
    /// The goal itself need not be enterable, so a monster can path to the cell its target stands on.
    /// </summary>
    public static IReadOnlyList<Cell>? Find(Cell start, Cell goal, Func<Cell, bool> canEnter, int maxSearch = 4000)
    {
        ArgumentNullException.ThrowIfNull(canEnter);
        if (start == goal)
        {
            return [];
        }

        var cameFrom = new Dictionary<Cell, Cell>();
        var cost = new Dictionary<Cell, int> { [start] = 0 };
        var open = new PriorityQueue<Cell, int>();
        open.Enqueue(start, Distance(start, goal));
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                var path = new List<Cell>();
                for (var at = goal; at != start; at = cameFrom[at])
                {
                    path.Add(at);
                }

                path.Reverse();
                return path;
            }

            if (cost.Count > maxSearch)
            {
                return null;
            }

            foreach (var direction in GridDirections.All)
            {
                var next = current.Step(direction);
                if (next != goal && !canEnter(next))
                {
                    continue;
                }

                var nextCost = cost[current] + 1;
                if (!cost.TryGetValue(next, out var known) || nextCost < known)
                {
                    cost[next] = nextCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, nextCost + Distance(next, goal));
                }
            }
        }

        return null;
    }

    /// <summary>Steps between two cells moving only horizontally and vertically.</summary>
    public static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
