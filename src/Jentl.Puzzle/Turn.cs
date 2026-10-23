namespace Jentl.Puzzle;

/// <summary>One recorded change to a board. A turn is a list of these, which is what undo, redo and animation use.</summary>
public abstract record Change
{
    internal abstract void Apply(Board board);

    internal abstract void Revert(Board board);
}

public sealed record PieceMoved(Piece Piece, Cell From, Cell To) : Change
{
    internal override void Apply(Board board) => board.Relocate(Piece, To);

    internal override void Revert(Board board) => board.Relocate(Piece, From);
}

public sealed record TileChanged(Cell Cell, TileKind From, TileKind To) : Change
{
    internal override void Apply(Board board) => board.SetTile(Cell, To);

    internal override void Revert(Board board) => board.SetTile(Cell, From);
}

public sealed record PieceAdded(Piece Piece) : Change
{
    internal override void Apply(Board board) => board.Insert(Piece);

    internal override void Revert(Board board) => board.Extract(Piece);
}

public sealed record PieceRemoved(Piece Piece) : Change
{
    internal override void Apply(Board board) => board.Extract(Piece);

    internal override void Revert(Board board) => board.Insert(Piece);
}

/// <summary>A played turn: the input, every change it made, and tags the rules attached (e.g. "push").</summary>
public sealed class Turn<TInput>
{
    internal Turn(TInput input, IReadOnlyList<Change> changes, IReadOnlySet<string> tags)
    {
        Input = input;
        Changes = changes;
        Tags = tags;
    }

    public TInput Input { get; }

    public IReadOnlyList<Change> Changes { get; }

    public IReadOnlySet<string> Tags { get; }

    public bool Has(string tag) => Tags.Contains(tag);

    /// <summary>Every piece movement in this turn, in order.</summary>
    public IEnumerable<PieceMoved> Moves => Changes.OfType<PieceMoved>();
}

/// <summary>
/// What rules use to change the board during a turn. Every change is applied at once (so later
/// checks in the same turn see it) and recorded, so a rejected turn can be rolled back and an
/// accepted one undone.
/// </summary>
public sealed class TurnBuilder
{
    private readonly List<Change> _changes = [];
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

    internal TurnBuilder(Board board) => Board = board;

    public Board Board { get; }

    internal IReadOnlyList<Change> Changes => _changes;

    internal IReadOnlySet<string> Tags => _tags;

    public void Move(Piece piece, Cell to)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Position != to)
        {
            Record(new PieceMoved(piece, piece.Position, to));
        }
    }

    public void SetTile(Cell cell, TileKind kind)
    {
        var from = Board.TileAt(cell);
        if (from != kind)
        {
            Record(new TileChanged(cell, from, kind));
        }
    }

    public Piece Add(PieceKind kind, Cell cell)
    {
        var piece = Board.Create(kind, cell);
        _changes.Add(new PieceAdded(piece));
        return piece;
    }

    public void Remove(Piece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        Record(new PieceRemoved(piece));
    }

    /// <summary>Marks the turn, e.g. "push", for statistics, sounds or animation.</summary>
    public void Tag(string tag) => _tags.Add(tag);

    internal void Rollback()
    {
        for (var i = _changes.Count - 1; i >= 0; i--)
        {
            _changes[i].Revert(Board);
        }

        _changes.Clear();
        _tags.Clear();
    }

    private void Record(Change change)
    {
        change.Apply(Board);
        _changes.Add(change);
    }
}
