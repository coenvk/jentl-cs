namespace Jentl.Multiplayer;

/// <summary>
/// Runs a turn-based game between humans and bots on one device. Bots move by themselves, one turn per
/// <see cref="BotDelay"/> so people can follow; humans move through <see cref="Play"/>. With more than one
/// human at the table, hands must stay hidden from the others: when the turn reaches a different person
/// the match waits for <see cref="ConfirmHandover"/> (the screen says "pass the device to ...").
/// </summary>
/// <remarks>
/// Online play would replace a seat's local controller with a remote one; the game itself only ever sees
/// seats and moves.
/// </remarks>
public sealed class Match<TGame, TMove>
    where TGame : ITurnBasedGame<TMove>
{
    private readonly Func<int, IBot<TGame, TMove>> _bots;
    private TimeSpan _waited;

    /// <param name="bots">The bot for a bot seat.</param>
    /// <param name="seed">Drives the bots' choices.</param>
    public Match(TGame game, IReadOnlyList<Player> players, Func<int, IBot<TGame, TMove>> bots, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(bots);
        if (players.Select((p, i) => p.Seat == i).Any(ok => !ok))
        {
            throw new ArgumentException("Players must be listed by seat, from 0.", nameof(players));
        }

        Game = game;
        Players = players;
        _bots = bots;
        Random = new Random(seed);
        // With one human, their hand is always theirs to see.
        if (Humans == 1)
        {
            Viewer = players.First(p => p.IsHuman).Seat;
        }
    }

    /// <summary>Raised after every move, by bots and humans alike.</summary>
    public event Action<int, TMove>? Moved;

    public TGame Game { get; }

    public IReadOnlyList<Player> Players { get; }

    public Random Random { get; }

    /// <summary>How long a bot "thinks" before moving.</summary>
    public TimeSpan BotDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    public Player Current => Players[Game.CurrentSeat];

    public int Humans => Players.Count(p => p.IsHuman);

    /// <summary>The seat whose secrets the screen may show (their hand), or null while nobody's may.</summary>
    public int? Viewer { get; private set; }

    /// <summary>A human's turn has come, but the device is still in someone else's hands.</summary>
    public bool AwaitingHandover => !Game.IsOver && Current.IsHuman && Humans > 1 && Viewer != Current.Seat;

    /// <summary>It is a human's turn and they hold the device.</summary>
    public bool IsHumansTurn => !Game.IsOver && Current.IsHuman && !AwaitingHandover;

    /// <summary>The next person has taken the device: their hand may be shown.</summary>
    public void ConfirmHandover()
    {
        if (AwaitingHandover)
        {
            Viewer = Current.Seat;
        }
    }

    /// <summary>Hides every hand, e.g. at the end of a turn before passing the device on.</summary>
    public void HideHands()
    {
        if (Humans > 1)
        {
            Viewer = null;
        }
    }

    /// <summary>Plays a move for the human whose turn it is.</summary>
    public void Play(TMove move)
    {
        if (!IsHumansTurn)
        {
            throw new InvalidOperationException("It is not a human's turn.");
        }

        Apply(Current.Seat, move);
    }

    /// <summary>Lets time pass; plays a bot's turn when it is due. Returns the bot's move, if one was made.</summary>
    public (int Seat, TMove Move)? Update(TimeSpan elapsed)
    {
        if (Game.IsOver || Current.IsHuman)
        {
            _waited = TimeSpan.Zero;
            return null;
        }

        _waited += elapsed;
        if (_waited < BotDelay)
        {
            return null;
        }

        _waited = TimeSpan.Zero;
        var seat = Current.Seat;
        var move = _bots(seat).Choose(Game, seat, Random);
        Apply(seat, move);
        return (seat, move);
    }

    private void Apply(int seat, TMove move)
    {
        Game.Apply(seat, move);
        Moved?.Invoke(seat, move);
    }
}
