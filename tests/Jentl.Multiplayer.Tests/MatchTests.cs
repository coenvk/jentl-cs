namespace Jentl.Multiplayer.Tests;

public class MatchTests
{
    [Fact]
    public void TurnOrder_GoesRound_BothWays_AndSkips()
    {
        var order = new TurnOrder(4);

        order.Next().Should().Be(1);
        order.Advance(2);
        order.Current.Should().Be(2);
        order.Reverse();
        order.Next().Should().Be(1);
        order.Advance(3);
        order.Current.Should().Be(3, "2, 1, 0, then round to 3");
    }

    [Fact]
    public void Bots_MoveAfterTheirDelay_AndHumansThroughPlay()
    {
        var game = new CountingGame(2);
        var match = new Match<CountingGame, int>(game, [new Player(0, "You", PlayerKind.Human), new Player(1, "Bot", PlayerKind.Bot)], _ => new FirstMoveBot());

        match.IsHumansTurn.Should().BeTrue();
        match.Update(TimeSpan.FromSeconds(5)).Should().BeNull("the human moves themselves");
        match.Play(1);

        match.Current.Name.Should().Be("Bot");
        var act = () => match.Play(1);
        act.Should().Throw<InvalidOperationException>();
        match.Update(TimeSpan.FromMilliseconds(300)).Should().BeNull();
        match.Update(TimeSpan.FromMilliseconds(400)).Should().Be((1, 1));
        game.Total.Should().Be(2);
        match.IsHumansTurn.Should().BeTrue();
    }

    [Fact]
    public void WithOneHuman_TheirHandIsAlwaysShown()
    {
        var match = new Match<CountingGame, int>(new CountingGame(3), Seats("B", "H", "B"), _ => new FirstMoveBot());

        match.Viewer.Should().Be(1);
        match.AwaitingHandover.Should().BeFalse();
    }

    [Fact]
    public void WithSeveralHumans_TheDeviceIsHandedOverBetweenThem()
    {
        var match = new Match<CountingGame, int>(new CountingGame(3), Seats("H", "B", "H"), _ => new FirstMoveBot()) { BotDelay = TimeSpan.Zero };

        match.Viewer.Should().BeNull();
        match.AwaitingHandover.Should().BeTrue();
        match.IsHumansTurn.Should().BeFalse();
        match.ConfirmHandover();
        match.Viewer.Should().Be(0);

        match.Play(1);
        match.HideHands();
        match.Update(TimeSpan.Zero).Should().NotBeNull("the bot between them plays");

        match.AwaitingHandover.Should().BeTrue();
        match.ConfirmHandover();
        match.Viewer.Should().Be(2);
        match.IsHumansTurn.Should().BeTrue();
    }

    [Fact]
    public void Moved_ReportsEveryMove()
    {
        var moves = new List<(int, int)>();
        var match = new Match<CountingGame, int>(new CountingGame(2), Seats("B", "B"), _ => new FirstMoveBot()) { BotDelay = TimeSpan.Zero };
        match.Moved += (seat, move) => moves.Add((seat, move));

        while (!match.Game.IsOver)
        {
            match.Update(TimeSpan.Zero);
        }

        moves.Should().HaveCount(5).And.StartWith((0, 1));
    }

    private static Player[] Seats(params string[] kinds) =>
        [.. kinds.Select((k, i) => new Player(i, $"{k}{i}", k == "H" ? PlayerKind.Human : PlayerKind.Bot))];

    /// <summary>Players take turns adding 1 or 2; the game ends at 5 or more.</summary>
    private sealed class CountingGame(int players) : ITurnBasedGame<int>
    {
        private readonly TurnOrder _order = new(players);

        public int Total { get; private set; }

        public int CurrentSeat => _order.Current;

        public bool IsOver => Total >= 5;

        public IReadOnlyList<int> LegalMoves(int seat) => seat == CurrentSeat && !IsOver ? [1, 2] : [];

        public void Apply(int seat, int move)
        {
            if (!LegalMoves(seat).Contains(move))
            {
                throw new InvalidOperationException("Not a legal move.");
            }

            Total += move;
            _order.Advance();
        }
    }

    private sealed class FirstMoveBot : IBot<CountingGame, int>
    {
        public int Choose(CountingGame game, int seat, Random random) => game.LegalMoves(seat)[0];
    }
}
