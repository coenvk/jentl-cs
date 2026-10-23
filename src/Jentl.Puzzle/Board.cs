namespace Jentl.Puzzle;

/// <summary>
/// A grid of tiles with at most one piece per cell. Set it up with <see cref="SetTile"/> and
/// <see cref="Place"/>; once a <see cref="PuzzleGame{TInput}"/> plays on it, change it only through
/// turns, so every change can be undone.
/// </summary>
public sealed class Board
{
    private readonly TileKind[] _tiles;
    private readonly Piece?[] _occupants;
    private readonly List<Piece> _pieces = [];
    private int _nextId;

    public Board(int width, int height, TileKind fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Board size must be positive, got {width}x{height}.");
        }

        Width = width;
        Height = height;
        _tiles = Enumerable.Repeat(fill, width * height).ToArray();
        _occupants = new Piece?[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<Piece> Pieces => _pieces;

    public IEnumerable<Cell> Cells => Enumerable.Range(0, Width * Height).Select(i => new Cell(i % Width, i / Width));

    public bool Contains(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    /// <summary>The tile at <paramref name="cell"/>, which must be on the board.</summary>
    public TileKind TileAt(Cell cell) =>
        Contains(cell) ? _tiles[Index(cell)] : throw new ArgumentOutOfRangeException(nameof(cell), cell, "Outside the board.");

    public Piece? PieceAt(Cell cell) => Contains(cell) ? _occupants[Index(cell)] : null;

    public IEnumerable<Piece> PiecesOf(PieceKind kind) => _pieces.Where(p => p.Kind == kind);

    public IEnumerable<Cell> CellsOf(TileKind kind) => Cells.Where(c => _tiles[Index(c)] == kind);

    /// <summary>True when a piece could stand on <paramref name="cell"/>: on the board, not solid, not occupied.</summary>
    public bool IsFree(Cell cell) => Contains(cell) && !TileAt(cell).IsSolid && PieceAt(cell) is null;

    public void SetTile(Cell cell, TileKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _tiles[CheckedIndex(cell)] = kind;
    }

    /// <summary>Adds a piece during setup.</summary>
    public Piece Place(PieceKind kind, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var piece = new Piece(_nextId++, kind, cell);
        Insert(piece);
        return piece;
    }

    internal void Insert(Piece piece)
    {
        var index = CheckedIndex(piece.Position);
        if (_occupants[index] is { } other)
        {
            throw new InvalidOperationException($"{piece} cannot go where {other} already is.");
        }

        _occupants[index] = piece;
        _pieces.Add(piece);
    }

    internal void Extract(Piece piece)
    {
        _occupants[Index(piece.Position)] = null;
        _pieces.Remove(piece);
    }

    internal void Relocate(Piece piece, Cell to)
    {
        var target = CheckedIndex(to);
        if (_occupants[target] is { } other && other != piece)
        {
            throw new InvalidOperationException($"{piece} cannot move onto {other}.");
        }

        _occupants[Index(piece.Position)] = null;
        piece.Position = to;
        _occupants[target] = piece;
    }

    internal Piece Create(PieceKind kind, Cell cell)
    {
        var piece = new Piece(_nextId++, kind, cell);
        Insert(piece);
        return piece;
    }

    private int Index(Cell cell) => cell.Y * Width + cell.X;

    private int CheckedIndex(Cell cell) =>
        Contains(cell) ? Index(cell) : throw new ArgumentOutOfRangeException(nameof(cell), cell, "Outside the board.");
}
