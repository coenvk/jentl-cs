using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Puzzle;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Puzzle;

/// <summary>
/// Screen side of a grid puzzle: fits the grid into an area, maps between cells and pixels, and
/// animates pieces between cells. Positions are in grid units (cell (3, 2) is at (3, 2)), so the
/// layout can change every frame (resizing, rotating) without disturbing animations.
/// </summary>
public sealed class GridView
{
    private readonly Dictionary<int, (Tween Tween, Vector2 Target)> _tweens = [];

    /// <summary>Largest cell size in pixels; small grids stop growing past it.</summary>
    public int MaxTile { get; set; } = 112;

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>Cell size in pixels as of the last <see cref="Fit"/>; 0 before.</summary>
    public int Tile { get; private set; }

    public Vector2 Origin { get; private set; }

    /// <summary>Lays out a <paramref name="columns"/> by <paramref name="rows"/> grid centred in <paramref name="area"/>.</summary>
    public void Fit(Rectangle area, int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        Tile = Math.Max(1, Math.Min(MaxTile, Math.Min(area.Width / Math.Max(1, columns), area.Height / Math.Max(1, rows))));
        Origin = new Vector2(area.X + (area.Width - Tile * columns) / 2, area.Y + (area.Height - Tile * rows) / 2);
    }

    /// <summary>The screen rectangle of a grid position (fractional while animating).</summary>
    public Rectangle Bounds(Vector2 grid) =>
        new((int)MathF.Round(Origin.X + grid.X * Tile), (int)MathF.Round(Origin.Y + grid.Y * Tile), Tile, Tile);

    public Rectangle Bounds(Cell cell) => Bounds(new Vector2(cell.X, cell.Y));

    /// <summary>The cell under a screen point, or null when it is off the grid.</summary>
    public Cell? CellAt(Vector2 screen)
    {
        if (Tile == 0)
        {
            return null;
        }

        var x = (int)MathF.Floor((screen.X - Origin.X) / Tile);
        var y = (int)MathF.Floor((screen.Y - Origin.Y) / Tile);
        return x >= 0 && y >= 0 && x < Columns && y < Rows ? new Cell(x, y) : null;
    }

    /// <summary>
    /// Slides the thing identified by <paramref name="id"/> to <paramref name="to"/>. It starts from where it
    /// is currently drawn if it was already moving towards <paramref name="from"/>, so quick moves chain smoothly.
    /// </summary>
    public void Animate(int id, Vector2 from, Vector2 to, TimeSpan duration, Func<float, float>? easing = null)
    {
        var start = PositionOf(id, from);
        _tweens[id] = (new Tween(start, to, duration, easing), to);
    }

    public void Animate(int id, Cell from, Cell to, TimeSpan duration, Func<float, float>? easing = null) =>
        Animate(id, new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), duration, easing);

    /// <summary>
    /// Where to draw <paramref name="id"/>, which logically sits at <paramref name="at"/>: its animated
    /// position if it is moving there, otherwise <paramref name="at"/> itself.
    /// </summary>
    public Vector2 PositionOf(int id, Vector2 at) =>
        _tweens.TryGetValue(id, out var moving) && moving.Target == at && !moving.Tween.IsDone ? moving.Tween.Value : at;

    public Vector2 PositionOf(int id, Cell at) => PositionOf(id, new Vector2(at.X, at.Y));

    public bool IsAnimating => _tweens.Values.Any(t => !t.Tween.IsDone);

    public void Update(TimeSpan elapsed)
    {
        foreach (var (tween, _) in _tweens.Values)
        {
            tween.Update(elapsed);
        }
    }

    /// <summary>Drops every animation, e.g. after a restart.</summary>
    public void Stop() => _tweens.Clear();

    public static GridDirection ToGrid(SwipeDirection swipe) => swipe switch
    {
        SwipeDirection.Up => GridDirection.Up,
        SwipeDirection.Down => GridDirection.Down,
        SwipeDirection.Left => GridDirection.Left,
        _ => GridDirection.Right,
    };
}
