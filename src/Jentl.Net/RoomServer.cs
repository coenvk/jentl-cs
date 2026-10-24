using System.Text.Json;

namespace Jentl.Net;

/// <summary>One player's connection, as the room server sees it: something to send text to.</summary>
public interface IConnection
{
    /// <summary>Queues a message for the player; never blocks.</summary>
    void Send(string message);

    /// <summary>Ends the connection (the player broke the rules of the protocol).</summary>
    void Close();
}

/// <summary>
/// A game the room server can host. The server keeps it authoritative: players send actions, the game checks
/// them, and each seat gets back only what it may see. Called under the server's lock, one thing at a time.
/// </summary>
public interface IRoomGame
{
    int MinSeats { get; }

    int MaxSeats { get; }

    /// <summary>Goes up whenever anything changes, so the server knows when to send fresh states.</summary>
    int Version { get; }

    /// <summary>Starts with these players (by seat); bots play the <see cref="SeatKind.Bot"/> seats.</summary>
    void Start(IReadOnlyList<SeatInfo> seats, int seed);

    /// <summary>Applies a seat's action (a move, or a command such as "next round"); returns why not, if it can't.</summary>
    string? Act(int seat, JsonElement action);

    /// <summary>Lets time pass: bots move.</summary>
    void Update(TimeSpan elapsed);

    /// <summary>A person left: a bot plays their seat from now on.</summary>
    void Replace(int seat);

    /// <summary>The game as <paramref name="seat"/> may see it, with what happened after version <paramref name="since"/>.</summary>
    JsonElement StateFor(int seat, int since);
}

/// <summary>
/// Rooms of players on one server. A player creates a room (and hosts it) or joins one by its four-digit code;
/// the host starts the game, bots filling the empty seats, and the room passes actions to the game and states
/// back to each player. A player who leaves during a game is replaced by a bot; a room nobody is in any more
/// closes. Thread-safe: the network calls in from many threads.
/// </summary>
public sealed class RoomServer
{
    private const int MaxRooms = 2000;

    private readonly object _lock = new();
    private readonly IReadOnlyDictionary<string, Func<IRoomGame>> _games;
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<IConnection, Room> _where = [];
    private readonly Random _random;

    /// <param name="games">How to make each kind of game, by name ("uno").</param>
    public RoomServer(IReadOnlyDictionary<string, Func<IRoomGame>> games, int? seed = null)
    {
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _random = seed is { } s ? new Random(s) : new Random();
    }

    public int RoomCount
    {
        get
        {
            lock (_lock)
            {
                return _rooms.Count;
            }
        }
    }

    public void Received(IConnection connection, string text)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Protocol.ReadClient(text) is not { } message)
        {
            Send(connection, new ServerError("That message makes no sense."));
            return;
        }

        lock (_lock)
        {
            switch (message)
            {
                case CreateRoom create:
                    Create(connection, create);
                    break;
                case JoinRoom join:
                    Join(connection, join.Code);
                    break;
                case StartGame:
                    Start(connection);
                    break;
                case PlayerAction action:
                    Act(connection, action.Action);
                    break;
                case LeaveRoom:
                    Leave(connection);
                    break;
            }
        }
    }

    public void Disconnected(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_lock)
        {
            Leave(connection);
        }
    }

    /// <summary>Lets time pass in every running game (bots move). Call a few times a second.</summary>
    public void Update(TimeSpan elapsed)
    {
        lock (_lock)
        {
            foreach (var room in _rooms.Values.Where(r => r.Game is not null))
            {
                room.Game!.Update(elapsed);
                SendStates(room);
            }
        }
    }

    private static void Send(IConnection connection, ServerMessage message) => connection.Send(Protocol.Write(message));

    private void Create(IConnection connection, CreateRoom create)
    {
        if (_where.ContainsKey(connection))
        {
            Send(connection, new ServerError("You are already in a room."));
            return;
        }

        if (!_games.TryGetValue(create.Game, out var make))
        {
            Send(connection, new ServerError($"This server doesn't host {create.Game}."));
            return;
        }

        var probe = make();
        if (create.Seats < probe.MinSeats || create.Seats > probe.MaxSeats)
        {
            Send(connection, new ServerError($"{create.Game} takes {probe.MinSeats} to {probe.MaxSeats} players."));
            return;
        }

        if (_rooms.Count >= MaxRooms)
        {
            Send(connection, new ServerError("The server is full; try again later."));
            return;
        }

        string code;
        do
        {
            code = _random.Next(10_000).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        }
        while (_rooms.ContainsKey(code));

        var room = new Room(code, create.Game, make, create.Seats);
        _rooms[code] = room;
        Seat(room, connection);
    }

    private void Join(IConnection connection, string code)
    {
        if (_where.ContainsKey(connection))
        {
            Send(connection, new ServerError("You are already in a room."));
        }
        else if (!_rooms.TryGetValue(code, out var room))
        {
            Send(connection, new ServerError($"There is no room {code}."));
        }
        else if (room.Game is not null)
        {
            Send(connection, new ServerError($"Room {code} has already started."));
        }
        else if (room.Players.All(p => p is not null))
        {
            Send(connection, new ServerError($"Room {code} is full."));
        }
        else
        {
            Seat(room, connection);
        }
    }

    private void Seat(Room room, IConnection connection)
    {
        var seat = Array.IndexOf(room.Players, null);
        room.Players[seat] = connection;
        _where[connection] = room;
        Send(connection, new RoomJoined(room.Code, room.GameName, seat, IsHost: seat == Room.Host));
        SendLobby(room);
    }

    private void Start(IConnection connection)
    {
        if (!_where.TryGetValue(connection, out var room))
        {
            Send(connection, new ServerError("You are not in a room."));
            return;
        }

        if (room.Players[Room.Host] != connection)
        {
            Send(connection, new ServerError("Only the host can start the game."));
            return;
        }

        if (room.Game is not null)
        {
            return;
        }

        room.Game = room.Make();
        room.Game.Start(Seats(room), _random.Next());
        foreach (var player in room.Players.OfType<IConnection>())
        {
            Send(player, new GameStarted());
        }

        SendLobby(room);
        SendStates(room);
    }

    private void Act(IConnection connection, JsonElement action)
    {
        if (!_where.TryGetValue(connection, out var room) || room.Game is null)
        {
            Send(connection, new ServerError("No game is running."));
            return;
        }

        var seat = Array.IndexOf(room.Players, connection);
        if (room.Game.Act(seat, action) is { } problem)
        {
            Send(connection, new ServerError(problem));
        }

        SendStates(room);
    }

    private void Leave(IConnection connection)
    {
        if (!_where.Remove(connection, out var room))
        {
            return;
        }

        var seat = Array.IndexOf(room.Players, connection);
        room.Players[seat] = null;
        if (room.Players.All(p => p is null))
        {
            _rooms.Remove(room.Code);
            return;
        }

        if (room.Game is null)
        {
            // Before the start, the host leaving closes the room; anyone else just frees their seat.
            if (seat == Room.Host)
            {
                foreach (var player in room.Players.OfType<IConnection>())
                {
                    Send(player, new RoomClosed("The host left."));
                    _where.Remove(player);
                }

                _rooms.Remove(room.Code);
                return;
            }
        }
        else
        {
            room.Left.Add(seat);
            room.Game.Replace(seat);
        }

        SendLobby(room);
        SendStates(room);
    }

    private static IReadOnlyList<SeatInfo> Seats(Room room) =>
    [
        .. room.Players.Select((player, seat) =>
            player is not null ? new SeatInfo(seat, $"Player {seat + 1}", SeatKind.Human, Connected: true)
            : room.Game is null ? new SeatInfo(seat, "Open", SeatKind.Open, Connected: false)
            : new SeatInfo(seat, room.Left.Contains(seat) ? $"Player {seat + 1} (bot)" : BotName(seat), SeatKind.Bot, Connected: false)),
    ];

    private static string BotName(int seat) => seat switch
    {
        1 => "Ruby",
        2 => "Otto",
        3 => "Mila",
        _ => $"Bot {seat + 1}",
    };

    private static void SendLobby(Room room)
    {
        var message = Protocol.Write(new LobbyChanged(Seats(room)));
        foreach (var player in room.Players.OfType<IConnection>())
        {
            player.Send(message);
        }
    }

    private static void SendStates(Room room)
    {
        if (room.Game is not { } game || game.Version == room.SentVersion)
        {
            return;
        }

        for (var seat = 0; seat < room.Players.Length; seat++)
        {
            if (room.Players[seat] is { } player)
            {
                Send(player, new GameState(game.StateFor(seat, room.SentVersion)));
            }
        }

        room.SentVersion = game.Version;
    }

    private sealed class Room(string code, string game, Func<IRoomGame> make, int seats)
    {
        public string Code { get; } = code;

        public string GameName { get; } = game;

        public Func<IRoomGame> Make { get; } = make;

        public IConnection?[] Players { get; } = new IConnection?[seats];

        /// <summary>Whoever made the room hosts it, in the first seat.</summary>
        public const int Host = 0;

        public IRoomGame? Game { get; set; }

        public HashSet<int> Left { get; } = [];

        public int SentVersion { get; set; } = -1;
    }
}
