using Jentl.Collect;

namespace Jentl.Net.Tests;

public sealed class ScoreServerTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"scores-{Guid.NewGuid():N}.json");
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private static readonly Dictionary<string, BoardRules> Boards = new()
    {
        ["bricks"] = new BoardRules(ScoreOrder.HigherIsBetter, MaxScore: 1_000_000),
        ["sokoban/1"] = new BoardRules(ScoreOrder.LowerIsBetter, MaxScore: 10_000, MinScore: 1),
    };

    public void Dispose() => File.Delete(_file);

    private ScoreServer Server() => new(Boards, _clock, _file);

    [Fact]
    public void Submit_PutsTheScoreOnTheBoard_AndAnswersWithTheTopAndYourPlace()
    {
        var server = Server();
        server.Handle(new SubmitScore("bricks", "Swift Otter 27", 500));

        var answer = server.Handle(new SubmitScore("bricks", "Brave Fox 11", 900)).Should().BeOfType<Scores>().Subject;

        answer.Board.Should().Be("bricks");
        answer.Top.Select(e => e.Name).Should().Equal("Brave Fox 11", "Swift Otter 27");
        answer.Rank.Should().Be(0);
        answer.Mine!.Score.Should().Be(900);
        answer.Top[0].At.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public void Get_ShowsTheTop_AndWhereANamedPlayerStands()
    {
        var server = Server();
        server.Handle(new SubmitScore("sokoban/1", "Swift Otter 27", 120));
        server.Handle(new SubmitScore("sokoban/1", "Brave Fox 11", 80));

        var answer = server.Handle(new GetScores("sokoban/1", "Swift Otter 27")).Should().BeOfType<Scores>().Subject;

        answer.Top.Select(e => e.Score).Should().Equal(80, 120);
        answer.Rank.Should().Be(1);
        server.Handle(new GetScores("sokoban/1", null)).Should().BeOfType<Scores>().Which.Rank.Should().BeNull();
    }

    [Fact]
    public void APlayersWorseScore_LeavesTheirBest()
    {
        var server = Server();
        server.Handle(new SubmitScore("bricks", "Swift Otter 27", 500));

        var answer = server.Handle(new SubmitScore("bricks", "Swift Otter 27", 100)).Should().BeOfType<Scores>().Subject;

        answer.Top.Should().ContainSingle().Which.Score.Should().Be(500);
        answer.Rank.Should().Be(0, "the answer still says where they stand");
    }

    [Theory]
    [InlineData("tetris", "Swift Otter 27", 10)]
    [InlineData("bricks", "Anything I like", 10)]
    [InlineData("bricks", "Swift Otter 27", -5)]
    [InlineData("bricks", "Swift Otter 27", 2_000_000)]
    [InlineData("sokoban/1", "Swift Otter 27", 0)]
    public void Nonsense_IsTurnedAway(string board, string name, long score)
    {
        var server = Server();

        server.Handle(new SubmitScore(board, name, score)).Should().BeOfType<ServerError>();
        if (Boards.ContainsKey(board))
        {
            server.Handle(new GetScores(board, null)).Should().BeOfType<Scores>().Which.Top.Should().BeEmpty();
        }
    }

    [Fact]
    public void TheBoardsAreKept_InTheFile()
    {
        Server().Handle(new SubmitScore("bricks", "Swift Otter 27", 500));

        var again = Server().Handle(new GetScores("bricks", null)).Should().BeOfType<Scores>().Subject;

        again.Top.Should().ContainSingle().Which.Name.Should().Be("Swift Otter 27");
    }

    [Fact]
    public void ABrokenFile_StartsEmpty_RatherThanStoppingTheServer()
    {
        File.WriteAllText(_file, "{not json");

        Server().Handle(new GetScores("bricks", null)).Should().BeOfType<Scores>().Which.Top.Should().BeEmpty();
    }

    [Fact]
    public void TheRoomServer_PassesScoreMessagesOn()
    {
        var rooms = new RoomServer(new Dictionary<string, Func<IRoomGame>>(), scores: Server());
        var player = new Connection();

        rooms.Received(player, Protocol.Write(new SubmitScore("bricks", "Swift Otter 27", 500)));
        Protocol.ReadServer(player.Sent.Single()).Should().BeOfType<Scores>().Which.Rank.Should().Be(0);

        var without = new RoomServer(new Dictionary<string, Func<IRoomGame>>());
        without.Received(player, Protocol.Write(new GetScores("bricks", null)));
        Protocol.ReadServer(player.Sent.Last()).Should().BeOfType<ServerError>();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Connection : IConnection
    {
        public List<string> Sent { get; } = [];

        public void Send(string message) => Sent.Add(message);

        public void Close()
        {
        }
    }
}
