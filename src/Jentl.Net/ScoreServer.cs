using System.Text.Json;
using Jentl.Collect;

namespace Jentl.Net;

/// <summary>
/// What a leaderboard on the server takes: which way scores are better, the range a real score can fall in (anything
/// outside is turned away), and how many lines it keeps.
/// </summary>
public sealed record BoardRules(ScoreOrder Order, long MaxScore, long MinScore = 0, int Capacity = 100);

/// <summary>
/// The online leaderboards: boards the server knows (by name, such as "bricks" or "sokoban/12"), one line per player
/// name, kept in a JSON file so they outlive a restart. Only names from <see cref="PlayerNames"/> and scores within a
/// board's rules are taken. The <see cref="RoomServer"/> passes it the score messages; thread-safe.
/// </summary>
/// <remarks>
/// The server cannot see the game played, so a determined player can still send a made-up score within the rules:
/// keep the range tight. A broken or missing file starts the boards empty rather than stopping the server.
/// </remarks>
public sealed class ScoreServer
{
    /// <summary>How many lines of the top a <see cref="Scores"/> answer carries.</summary>
    public const int Shown = 10;

    private readonly object _lock = new();
    private readonly IReadOnlyDictionary<string, BoardRules> _rules;
    private readonly Dictionary<string, Leaderboard> _boards = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly string? _file;

    /// <param name="boards">The boards there are, by name, and their rules.</param>
    /// <param name="clock">The time, for when each score was set.</param>
    /// <param name="file">Where the boards are kept, or null to keep them in memory only.</param>
    public ScoreServer(IReadOnlyDictionary<string, BoardRules> boards, TimeProvider clock, string? file = null)
    {
        _rules = boards ?? throw new ArgumentNullException(nameof(boards));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _file = file;
        var saved = Load(file);
        foreach (var (name, rules) in boards)
        {
            var board = new Leaderboard(rules.Order, rules.Capacity, bestPerName: true);
            foreach (var entry in saved.GetValueOrDefault(name, []).OrderBy(e => e.At))
            {
                board.Submit(entry);
            }

            _boards[name] = board;
        }
    }

    /// <summary>Answers a <see cref="SubmitScore"/> or <see cref="GetScores"/> with the board, or an error.</summary>
    public ServerMessage Handle(ClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_lock)
        {
            switch (message)
            {
                case SubmitScore submit:
                    if (!_boards.TryGetValue(submit.Board, out var board))
                    {
                        return new ServerError($"There is no leaderboard {submit.Board}.");
                    }

                    var rules = _rules[submit.Board];
                    if (!PlayerNames.IsValid(submit.Name))
                    {
                        return new ServerError("That name can't go on a leaderboard.");
                    }

                    if (submit.Score < rules.MinScore || submit.Score > rules.MaxScore)
                    {
                        return new ServerError("That score can't be right.");
                    }

                    if (board.Submit(new LeaderboardEntry(submit.Name, submit.Score, _clock.GetUtcNow())) is not null)
                    {
                        Save();
                    }

                    return Answer(submit.Board, board, submit.Name);
                case GetScores get:
                    return _boards.TryGetValue(get.Board, out var asked)
                        ? Answer(get.Board, asked, get.Name)
                        : new ServerError($"There is no leaderboard {get.Board}.");
                default:
                    return new ServerError("That is not a leaderboard message.");
            }
        }
    }

    private static Scores Answer(string name, Leaderboard board, string? player)
    {
        var rank = player is null ? -1 : board.Entries.ToList().FindIndex(e => e.Name == player);
        return new Scores(name, [.. board.Entries.Take(Shown)], rank >= 0 ? rank : null, rank >= 0 ? board.Entries[rank] : null);
    }

    private static Dictionary<string, List<LeaderboardEntry>> Load(string? file)
    {
        try
        {
            return file is not null && File.Exists(file)
                ? JsonSerializer.Deserialize(File.ReadAllText(file), ProtocolJson.Default.DictionaryStringListLeaderboardEntry) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Written aside and moved into place, so a crash halfway never leaves half a file.
    private void Save()
    {
        if (_file is null)
        {
            return;
        }

        var all = _boards.ToDictionary(b => b.Key, b => b.Value.Entries.ToList());
        var temporary = _file + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all, ProtocolJson.Default.DictionaryStringListLeaderboardEntry));
        File.Move(temporary, _file, overwrite: true);
    }
}
