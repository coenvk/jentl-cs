using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jentl.Collect;

/// <summary>Whether a bigger score is better (points) or a smaller one (time, moves).</summary>
public enum ScoreOrder
{
    HigherIsBetter,
    LowerIsBetter,
}

/// <summary>One line of a leaderboard: who, what they scored, and when.</summary>
public sealed record LeaderboardEntry(string Name, long Score, DateTimeOffset At);

/// <summary>
/// The best scores of a game, best first, up to <see cref="Capacity"/>: a high-score table on one device, or one board
/// on a server. Equal scores keep the order they came in (the first to reach a score stays above). With
/// <see cref="BestPerName"/>, each name holds one line, their best. Keep it in the save file with
/// <see cref="ToJson"/> and <see cref="FromJson"/>; the order and capacity belong to the game, not the file.
/// </summary>
public sealed class Leaderboard
{
    private readonly List<LeaderboardEntry> _entries = [];

    public Leaderboard(ScoreOrder order, int capacity = 10, bool bestPerName = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        (Order, Capacity, BestPerName) = (order, capacity, bestPerName);
    }

    public ScoreOrder Order { get; }

    public int Capacity { get; }

    public bool BestPerName { get; }

    /// <summary>The board, best first.</summary>
    public IReadOnlyList<LeaderboardEntry> Entries => _entries;

    /// <summary>Whether <paramref name="a"/> beats <paramref name="b"/> (a tie does not).</summary>
    public bool Beats(long a, long b) => Order == ScoreOrder.HigherIsBetter ? a > b : a < b;

    /// <summary>Whether <paramref name="score"/> would make the board (by anyone not on it yet).</summary>
    public bool Qualifies(long score) => _entries.Count < Capacity || Beats(score, _entries[^1].Score);

    /// <summary>
    /// Puts <paramref name="entry"/> on the board if it makes it, and returns its place (0 is the top), or null if it
    /// did not: the board is full of scores at least as good, or (with <see cref="BestPerName"/>) its name already has
    /// a score at least as good.
    /// </summary>
    public int? Submit(LeaderboardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (BestPerName && _entries.FindIndex(e => e.Name == entry.Name) is var own and >= 0)
        {
            if (!Beats(entry.Score, _entries[own].Score))
            {
                return null;
            }

            _entries.RemoveAt(own);
        }
        else if (!Qualifies(entry.Score))
        {
            return null;
        }

        // Below every score it does not beat, so equal scores keep the order they came in.
        var place = _entries.FindIndex(e => Beats(entry.Score, e.Score));
        place = place < 0 ? _entries.Count : place;
        _entries.Insert(place, entry);
        if (_entries.Count > Capacity)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        return place;
    }

    public string ToJson() => JsonSerializer.Serialize(_entries, LeaderboardJson.Default.ListLeaderboardEntry);

    /// <summary>A board of these entries (put in order, trimmed to the capacity).</summary>
    /// <exception cref="InvalidDataException">The text is not a list of entries.</exception>
    public static Leaderboard FromJson(string json, ScoreOrder order, int capacity = 10, bool bestPerName = false)
    {
        List<LeaderboardEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize(json, LeaderboardJson.Default.ListLeaderboardEntry);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Not a leaderboard.", e);
        }

        var board = new Leaderboard(order, capacity, bestPerName);
        foreach (var entry in (entries ?? []).Where(e => e is { Name: not null }).OrderBy(e => e.At))
        {
            board.Submit(entry);
        }

        return board;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<LeaderboardEntry>))]
internal sealed partial class LeaderboardJson : JsonSerializerContext;
