namespace Jentl.Cards;

/// <summary>One recorded change to the table; a turn is a list of these.</summary>
public abstract record CardChange
{
    internal abstract void Apply();

    internal abstract void Revert();
}

/// <summary>The cards from <paramref name="Index"/> up in <paramref name="From"/> moved, in order, onto <paramref name="To"/>.</summary>
public sealed record CardsMoved(Pile From, int Index, int Count, Pile To) : CardChange
{
    internal override void Apply()
    {
        var moving = From.Mutable.GetRange(Index, Count);
        From.Mutable.RemoveRange(Index, Count);
        To.Mutable.AddRange(moving);
    }

    internal override void Revert()
    {
        var moving = To.Mutable.GetRange(To.Count - Count, Count);
        To.Mutable.RemoveRange(To.Count - Count, Count);
        From.Mutable.InsertRange(Index, moving);
    }
}

public sealed record CardFlipped(Pile Pile, int Index, bool FaceUp) : CardChange
{
    internal override void Apply() => Pile.Mutable[Index] = Pile.Mutable[Index] with { FaceUp = FaceUp };

    internal override void Revert() => Pile.Mutable[Index] = Pile.Mutable[Index] with { FaceUp = !FaceUp };
}

/// <summary>What the player did. Games can add their own inputs by deriving from this.</summary>
public abstract record CardInput;

/// <summary>Drag the cards from <paramref name="Index"/> up in <paramref name="From"/> onto <paramref name="To"/>.</summary>
public sealed record DragCards(Pile From, int Index, Pile To) : CardInput;

/// <summary>A tap or click on a pile; <paramref name="Index"/> is the card hit, or -1 for an empty pile.</summary>
public sealed record ClickPile(Pile Pile, int Index) : CardInput;

/// <summary>A played turn: the input, the changes it made and the tags the rules attached.</summary>
public sealed class CardTurn
{
    internal CardTurn(CardInput input, IReadOnlyList<CardChange> changes, IReadOnlySet<string> tags)
    {
        Input = input;
        Changes = changes;
        Tags = tags;
    }

    public CardInput Input { get; }

    public IReadOnlyList<CardChange> Changes { get; }

    public IReadOnlySet<string> Tags { get; }

    public bool Has(string tag) => Tags.Contains(tag);
}

/// <summary>What rules use to change the table during a turn; every change applies at once and is recorded.</summary>
public sealed class CardTurnBuilder
{
    private readonly List<CardChange> _changes = [];
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

    internal CardTurnBuilder(Table table) => Table = table;

    public Table Table { get; }

    internal IReadOnlyList<CardChange> Changes => _changes;

    internal IReadOnlySet<string> Tags => _tags;

    /// <summary>Moves the cards from <paramref name="index"/> up in <paramref name="from"/> onto <paramref name="to"/>, keeping their order.</summary>
    public void Move(Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (index < 0 || index >= from.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{from.Name} has {from.Count} cards.");
        }

        if (from == to)
        {
            throw new ArgumentException("Cannot move cards onto their own pile.", nameof(to));
        }

        Record(new CardsMoved(from, index, from.Count - index, to));
    }

    /// <summary>Moves the top card of <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public void MoveTop(Pile from, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        Move(from, from.Count - 1, to);
    }

    public void Flip(Pile pile, int index, bool faceUp)
    {
        ArgumentNullException.ThrowIfNull(pile);
        if (pile[index].FaceUp != faceUp)
        {
            Record(new CardFlipped(pile, index, faceUp));
        }
    }

    public void Tag(string tag) => _tags.Add(tag);

    internal void Rollback()
    {
        for (var i = _changes.Count - 1; i >= 0; i--)
        {
            _changes[i].Revert();
        }

        _changes.Clear();
        _tags.Clear();
    }

    private void Record(CardChange change)
    {
        change.Apply();
        _changes.Add(change);
    }
}
