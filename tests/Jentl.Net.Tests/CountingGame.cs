using System.Text.Json;

namespace Jentl.Net.Tests;

/// <summary>
/// A tiny game to host in tests: players take turns adding 1 to 3 to a shared total, first to 21 wins. Each seat
/// also holds a secret number that only it may see, so the tests can check nobody is sent another's.
/// Bots add 1 whenever time passes.
/// </summary>
internal sealed class CountingGame : IRoomGame
{
    public const string Name = "counting";

    private readonly List<(int Version, string Event)> _events = [];
    private SeatInfo[] _seats = [];
    private int[] _secrets = [];

    public int MinSeats => 2;

    public int MaxSeats => 4;

    public int Version { get; private set; }

    public int Turn { get; private set; }

    public int Total { get; private set; }

    public void Start(IReadOnlyList<SeatInfo> seats, int seed)
    {
        _seats = [.. seats];
        _secrets = [.. seats.Select(s => 1000 + seed % 100 * 10 + s.Seat)];
        Changed("started");
    }

    public string? Act(int seat, JsonElement action)
    {
        if (seat != Turn)
        {
            return "It's not your turn.";
        }

        if (action.ValueKind != JsonValueKind.Number || action.GetInt32() is < 1 or > 3)
        {
            return "Add 1, 2 or 3.";
        }

        Add(seat, action.GetInt32());
        return null;
    }

    public void Update(TimeSpan elapsed)
    {
        if (Total < 21 && _seats[Turn].Kind == SeatKind.Bot)
        {
            Add(Turn, 1);
        }
    }

    public void Replace(int seat)
    {
        _seats[seat] = _seats[seat] with { Kind = SeatKind.Bot, Name = $"{_seats[seat].Name} (bot)" };
        Changed($"{seat} left");
    }

    public JsonElement StateFor(int seat, int since) => JsonSerializer.SerializeToElement(new State(
        seat,
        Turn,
        Total,
        _secrets[seat],
        [.. _seats.Select(s => s.Name)],
        [.. _events.Where(e => e.Version > since).Select(e => e.Event)]));

    public static State Read(GameState state) => state.State.Deserialize<State>()!;

    private void Add(int seat, int amount)
    {
        Total += amount;
        Turn = (Turn + 1) % _seats.Length;
        Changed($"{seat}+{amount}");
    }

    private void Changed(string what)
    {
        Version++;
        _events.Add((Version, what));
    }

    internal sealed record State(int Seat, int Turn, int Total, int Secret, IReadOnlyList<string> Names, IReadOnlyList<string> Events);
}
