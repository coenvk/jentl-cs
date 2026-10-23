namespace Jentl.Multiplayer;

public enum PlayerKind
{
    /// <summary>Someone at the device, playing through the screen.</summary>
    Human,

    /// <summary>A computer opponent, played by an <see cref="IBot{TGame, TMove}"/>.</summary>
    Bot,
}

/// <summary>Someone at the table. <see cref="Seat"/> is their place in turn order, from 0.</summary>
public sealed record Player(int Seat, string Name, PlayerKind Kind)
{
    public bool IsHuman => Kind == PlayerKind.Human;

    public override string ToString() => Name;
}

/// <summary>
/// Whose turn it is, going round the table in either direction. Seats count from 0; clockwise is
/// increasing seat numbers.
/// </summary>
public sealed class TurnOrder
{
    public TurnOrder(int count, int first = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(first, count);
        Count = count;
        Current = first;
    }

    public int Count { get; }

    public int Current { get; private set; }

    /// <summary>1 going clockwise, -1 after an odd number of reversals.</summary>
    public int Direction { get; private set; } = 1;

    /// <summary>The seat <paramref name="steps"/> turns from now, without moving.</summary>
    public int Next(int steps = 1) => Seat(Current + steps * Direction);

    /// <summary>Moves the turn on by <paramref name="steps"/> (2 skips a player).</summary>
    public void Advance(int steps = 1) => Current = Next(steps);

    public void Reverse() => Direction = -Direction;

    /// <summary>Gives the turn to <paramref name="seat"/>.</summary>
    public void GoTo(int seat)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seat);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, Count);
        Current = seat;
    }

    private int Seat(int index) => ((index % Count) + Count) % Count;
}

/// <summary>A game played in turns by several players. Moves are plain values, so they can be shown, logged or sent.</summary>
public interface ITurnBasedGame<TMove>
{
    int CurrentSeat { get; }

    bool IsOver { get; }

    /// <summary>What <paramref name="seat"/> may do now; empty when it is not their turn.</summary>
    IReadOnlyList<TMove> LegalMoves(int seat);

    /// <summary>Plays a move for <paramref name="seat"/>; throws if it is not their turn or the move is not legal.</summary>
    void Apply(int seat, TMove move);
}

/// <summary>A computer player for one kind of game.</summary>
public interface IBot<in TGame, TMove>
    where TGame : ITurnBasedGame<TMove>
{
    /// <summary>Picks one of the game's legal moves for <paramref name="seat"/>.</summary>
    TMove Choose(TGame game, int seat, Random random);
}
