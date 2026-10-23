namespace Jentl.Puzzle;

/// <summary>A game's rules: how one input changes the board, and when the puzzle is solved.</summary>
/// <typeparam name="TInput">What the player does in a turn: a direction, a tapped cell, a command.</typeparam>
public interface IPuzzleRules<TInput>
{
    /// <summary>Plays one input through <paramref name="turn"/>. Return false to reject it; any changes made are rolled back.</summary>
    bool Play(TurnBuilder turn, TInput input);

    bool IsSolved(Board board);
}

/// <summary>
/// Runs a turn-based puzzle: plays inputs through the rules, keeps the history, and undoes or redoes
/// whole turns. Redo reapplies the recorded changes, so it never depends on the rules again.
/// </summary>
public sealed class PuzzleGame<TInput>
{
    private readonly IPuzzleRules<TInput> _rules;
    private readonly List<Turn<TInput>> _history = [];
    private readonly Stack<Turn<TInput>> _redo = new();

    public PuzzleGame(Board board, IPuzzleRules<TInput> rules)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(rules);
        Board = board;
        _rules = rules;
    }

    public Board Board { get; }

    /// <summary>Turns played so far, oldest first.</summary>
    public IReadOnlyList<Turn<TInput>> History => _history;

    public Turn<TInput>? LastTurn => _history.Count > 0 ? _history[^1] : null;

    public bool CanUndo => _history.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsSolved => _rules.IsSolved(Board);

    /// <summary>Plays <paramref name="input"/>; returns the turn, or null if the rules rejected it (the board is unchanged then).</summary>
    public Turn<TInput>? Play(TInput input)
    {
        var builder = new TurnBuilder(Board);
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

        var turn = new Turn<TInput>(input, [.. builder.Changes], new HashSet<string>(builder.Tags, StringComparer.Ordinal));
        _history.Add(turn);
        _redo.Clear();
        return turn;
    }

    /// <summary>Reverts the last turn and returns it, or null if there is nothing to undo.</summary>
    public Turn<TInput>? Undo()
    {
        if (_history.Count == 0)
        {
            return null;
        }

        var turn = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        for (var i = turn.Changes.Count - 1; i >= 0; i--)
        {
            turn.Changes[i].Revert(Board);
        }

        _redo.Push(turn);
        return turn;
    }

    /// <summary>Reapplies the last undone turn and returns it, or null if there is nothing to redo.</summary>
    public Turn<TInput>? Redo()
    {
        if (!_redo.TryPop(out var turn))
        {
            return null;
        }

        foreach (var change in turn.Changes)
        {
            change.Apply(Board);
        }

        _history.Add(turn);
        return turn;
    }

    /// <summary>Undoes every turn and forgets the redo history.</summary>
    public void Restart()
    {
        while (Undo() is not null)
        {
        }

        _redo.Clear();
    }

    /// <summary>How many played turns carry <paramref name="tag"/>.</summary>
    public int CountTagged(string tag) => _history.Count(t => t.Has(tag));
}
