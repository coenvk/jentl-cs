namespace Jentl.Cards;

/// <summary>A card game's rules: how an input changes the table, and when the game is won.</summary>
public interface ICardRules
{
    /// <summary>Plays one input through <paramref name="turn"/>. Return false to reject it; any changes are rolled back.</summary>
    bool Play(CardTurnBuilder turn, CardInput input);

    bool IsWon(Table table);

    /// <summary>Useful moves to suggest, best first. Optional.</summary>
    IEnumerable<CardInput> Hints(Table table) => [];

    /// <summary>
    /// A move the game should make on the player's behalf right now (such as finishing a solitaire
    /// whose outcome is settled), or null. Optional.
    /// </summary>
    CardInput? AutoMove(Table table) => null;
}

/// <summary>Runs a card game: plays inputs through the rules, keeps the history, and undoes or redoes whole turns.</summary>
public sealed class CardGame
{
    private readonly ICardRules _rules;
    private readonly List<CardTurn> _history = [];
    private readonly Stack<CardTurn> _redo = new();

    public CardGame(Table table, ICardRules rules)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rules);
        Table = table;
        _rules = rules;
    }

    public Table Table { get; }

    public IReadOnlyList<CardTurn> History => _history;

    public CardTurn? LastTurn => _history.Count > 0 ? _history[^1] : null;

    public bool CanUndo => _history.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsWon => _rules.IsWon(Table);

    public IEnumerable<CardInput> Hints => _rules.Hints(Table);

    /// <summary>Plays <paramref name="input"/>; returns the turn, or null if the rules rejected it (the table is unchanged then).</summary>
    public CardTurn? Play(CardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var builder = new CardTurnBuilder(Table);
        bool accepted;
        try
        {
            accepted = _rules.Play(builder, input);
        }
        catch
        {
            builder.Rollback();
            throw;
        }

        if (!accepted || builder.Changes.Count == 0)
        {
            builder.Rollback();
            return null;
        }

        var turn = new CardTurn(input, [.. builder.Changes], new HashSet<string>(builder.Tags, StringComparer.Ordinal));
        _history.Add(turn);
        _redo.Clear();
        return turn;
    }

    /// <summary>Plays the rules' automatic move, if there is one; call repeatedly (e.g. once per animation step) to finish a game.</summary>
    public CardTurn? PlayAutoMove() => _rules.AutoMove(Table) is { } move ? Play(move) : null;

    public CardTurn? Undo()
    {
        if (_history.Count == 0)
        {
            return null;
        }

        var turn = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        for (var i = turn.Changes.Count - 1; i >= 0; i--)
        {
            turn.Changes[i].Revert();
        }

        _redo.Push(turn);
        return turn;
    }

    public CardTurn? Redo()
    {
        if (!_redo.TryPop(out var turn))
        {
            return null;
        }

        foreach (var change in turn.Changes)
        {
            change.Apply();
        }

        _history.Add(turn);
        return turn;
    }

    /// <summary>Undoes every turn back to the deal and forgets the redo history.</summary>
    public void Restart()
    {
        while (Undo() is not null)
        {
        }

        _redo.Clear();
    }

    public int CountTagged(string tag) => _history.Count(t => t.Has(tag));
}
