namespace Jentl.Puzzle;

public enum GridDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>A cell position on a board; (0, 0) is the top-left corner.</summary>
public readonly record struct Cell(int X, int Y)
{
    public Cell Step(GridDirection direction) => direction switch
    {
        GridDirection.Up => this with { Y = Y - 1 },
        GridDirection.Down => this with { Y = Y + 1 },
        GridDirection.Left => this with { X = X - 1 },
        GridDirection.Right => this with { X = X + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>The direction of a neighbouring cell, or null if <paramref name="other"/> is not adjacent.</summary>
    public GridDirection? DirectionTo(Cell other) => (other.X - X, other.Y - Y) switch
    {
        (0, -1) => GridDirection.Up,
        (0, 1) => GridDirection.Down,
        (-1, 0) => GridDirection.Left,
        (1, 0) => GridDirection.Right,
        _ => null,
    };

    public override string ToString() => $"({X}, {Y})";
}

public static class GridDirections
{
    public static IReadOnlyList<GridDirection> All { get; } = [GridDirection.Up, GridDirection.Down, GridDirection.Left, GridDirection.Right];

    public static GridDirection Opposite(this GridDirection direction) => direction switch
    {
        GridDirection.Up => GridDirection.Down,
        GridDirection.Down => GridDirection.Up,
        GridDirection.Left => GridDirection.Right,
        GridDirection.Right => GridDirection.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };
}

/// <summary>What a cell is made of (floor, wall, goal, ice...). Games declare their kinds once and compare by reference.</summary>
/// <param name="Name">For debugging and error messages.</param>
/// <param name="IsSolid">Pieces can never enter a solid tile.</param>
public sealed record TileKind(string Name, bool IsSolid = false)
{
    public override string ToString() => Name;
}

/// <summary>What a piece is (player, crate, lamp...).</summary>
public sealed record PieceKind(string Name)
{
    public override string ToString() => Name;
}

/// <summary>Something that sits on a cell and can move. Its identity stays the same across moves, undo and redo.</summary>
public sealed class Piece
{
    internal Piece(int id, PieceKind kind, Cell position)
    {
        Id = id;
        Kind = kind;
        Position = position;
    }

    public int Id { get; }

    public PieceKind Kind { get; }

    public Cell Position { get; internal set; }

    public override string ToString() => $"{Kind} #{Id} at {Position}";
}
