using Jentl.Cards.Rulesets;
using static Jentl.Cards.Tests.RulesetTests;

namespace Jentl.Cards.Tests;

public class RulesetTurnTests
{
    private static readonly CardRuleset Mini = CardRuleset.Parse("""
        {
          "name": "Mini",
          "deck": {
            "suits": [{ "name": "red" }, { "name": "blue" }],
            "ranks": [{ "name": "1", "points": 1 }, { "name": "2", "points": 2 }, { "name": "3", "points": 3 },
                      { "name": "skip", "points": 20, "tags": ["skip"] }, { "name": "rev", "points": 20, "tags": ["reverse"] },
                      { "name": "+2", "points": 20 }],
            "extra": [{ "rank": "wild", "count": 2, "points": 50, "tags": ["wild"] }]
          },
          "players": { "min": 2, "max": 4, "default": 3 },
          "piles": [
            { "name": "stock", "deal": { "rest": true, "faceUp": false } },
            { "name": "discard", "deal": { "cards": 1, "not": "wild" }, "build": { "match": "suitOrRank" } },
            { "name": "hand", "perPlayer": true, "deal": { "cards": 3 }, "take": "one" }
          ],
          "effects": [
            { "tag": "skip", "skip": 1 }, { "tag": "reverse", "reverse": true },
            { "rank": "+2", "draw": 2, "skip": 1 }, { "rank": "wild", "chooseSuit": true }
          ],
          "win": "emptyHand"
        }
        """);

    private static void Play(RulesetGame game, int seat, int index)
    {
        game.CurrentSeat.Should().Be(seat);
        game.Apply(seat, new ClickPile(game.Hand(seat), index));
    }

    [Fact]
    public void Cards_SkipReverseAndGoWild_UntilAHandIsEmpty()
    {
        // The discard gets the first card; then the hands a card each in turn.
        var game = new RulesetGame(Mini, Order(Mini, "red 1", "red skip", "red 2", "red rev", "blue 1", "blue 3", "blue rev", "wild", "blue +2", "red +2"));
        Codes(game.Hand(0)).Should().Be("red skip blue 1 wild");

        Play(game, 0, 0);
        game.CurrentSeat.Should().Be(2, "the skip passes over seat 1");
        game.LegalMoves(1).Should().BeEmpty();

        Play(game, 2, 0);
        game.Direction.Should().Be(-1);
        Play(game, 1, 0);

        Play(game, 0, 1);
        game.IsChoosingSuit.Should().BeTrue();
        game.LegalMoves(0).Should().Equal(new ChooseSuit("red"), new ChooseSuit("blue"));
        game.Apply(0, new ChooseSuit("blue"));
        game.ChosenSuit.Should().Be("blue");
        game.CurrentSeat.Should().Be(2);

        game.LegalMoves(2).OfType<ClickPile>().Where(m => m.Pile == game.Hand(2)).Select(m => m.Index).Should().Equal([0], "red +2 doesn't follow blue");
        Play(game, 2, 0);
        (game.Direction, game.ChosenSuit).Should().Be((1, null));

        Play(game, 0, 0);
        game.Winner.Should().Be(0);
        game.IsOver.Should().BeTrue();
        game.Score(0).Should().Be(3 + 20 + 20);
        game.LegalMoves(1).Should().BeEmpty();
    }

    [Fact]
    public void DrawCards_DrawAndSkip_ReverseSkipsForTwo_AndADrawnCardMayBePlayedOrKept()
    {
        var game = new RulesetGame(
            Mini,
            Order(Mini, ["red 1", "red +2", "red 2", "red rev", "blue 2", "blue 3", "blue skip"], ["red 3", "blue 1", "blue rev"]),
            players: 2);

        Play(game, 0, 0);
        game.Hand(1).Count.Should().Be(5);
        game.CurrentSeat.Should().Be(0, "a +2 also skips");
        Play(game, 0, 0);
        game.CurrentSeat.Should().Be(0, "with two players a reverse is a skip");

        game.LegalMoves(0).Should().Equal(new ClickPile(game.Stock, game.Stock.Count - 1));
        game.Apply(0, game.LegalMoves(0)[0]);
        game.HasDrawn.Should().BeTrue();
        Codes(game.Hand(0)).Should().Be("blue 3 red 3");
        game.LegalMoves(0).Should().Equal(new ClickPile(game.Hand(0), 1), PassTurn.Instance);

        game.Apply(0, PassTurn.Instance);
        (game.CurrentSeat, game.HasDrawn).Should().Be((1, false));
        game.Game.Undo();
        (game.CurrentSeat, game.HasDrawn).Should().Be((0, true));
        game.Play(new ClickPile(game.Hand(1), 0)).Should().BeNull("it isn't seat 1's turn");
        var wrongSeat = () => game.Apply(1, PassTurn.Instance);
        wrongSeat.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CrazyEights_DrawsUntilACardGoes()
    {
        var ruleset = RulesetExamples.Load("crazyeights");
        var game = new RulesetGame(
            ruleset,
            Order(ruleset, ["5H", "KS", "3D", "QS", "4D", "JS", "6D", "9S", "7D", "2C", "9D"], ["7H", "3C", "4C"]),
            players: 2);

        game.LegalMoves(0).Should().ContainSingle();
        game.Apply(0, game.LegalMoves(0)[0]);

        Codes(game.Hand(0)).Should().Be("KS QS JS 9S 2C 4C 3C 7H");
        game.LegalMoves(0).Should().Equal(new ClickPile(game.Hand(0), 7), PassTurn.Instance);
    }

    [Fact]
    public void AnEight_GoesOnAnything()
    {
        var ruleset = RulesetExamples.Load("crazyeights");
        var game = new RulesetGame(ruleset, Order(ruleset, "5H", "8S"), players: 2);

        game.Apply(0, new ClickPile(game.Hand(0), 0));

        game.IsChoosingSuit.Should().BeTrue();
        game.Apply(0, new ChooseSuit("clubs"));
        game.Fits(Card.Parse("2C"), game.Discard).Should().BeTrue();
        game.Fits(Card.Parse("8D"), game.Discard).Should().BeTrue();
        game.Fits(Card.Parse("2H"), game.Discard).Should().BeFalse();
    }

    [Theory]
    [InlineData("uno", 2)]
    [InlineData("uno", 4)]
    [InlineData("uno", 8)]
    [InlineData("crazyeights", 2)]
    [InlineData("crazyeights", 5)]
    public void Bots_PlayWholeGames(string name, int players)
    {
        var ruleset = RulesetExamples.Load(name);
        var bot = new RulesetBot();
        for (var seed = 1; seed <= 20; seed++)
        {
            var game = new RulesetGame(ruleset, seed, players);
            var random = new Random(seed);
            var moves = 0;
            while (!game.IsOver && moves++ < 5000)
            {
                var seat = game.CurrentSeat;
                game.Apply(seat, bot.Choose(game, seat, random));
            }

            game.IsOver.Should().BeTrue($"{name} with {players} players, seed {seed}");
            game.Table.Piles.Sum(p => p.Count).Should().Be(ruleset.Deck.Cards.Count);
            game.Hand(game.Winner!.Value).IsEmpty.Should().BeTrue();
        }
    }

    [Fact]
    public void AMatch_PlaysTheBots()
    {
        var game = new RulesetGame(RulesetExamples.Load("uno"), seed: 3, players: 3);
        var players = Enumerable.Range(0, 3).Select(i => new Jentl.Multiplayer.Player(i, $"Bot {i}", Jentl.Multiplayer.PlayerKind.Bot)).ToList();
        var match = new Jentl.Multiplayer.Match<RulesetGame, CardInput>(game, players, _ => new RulesetBot(), seed: 3) { BotDelay = TimeSpan.Zero };

        for (var i = 0; i < 5000 && !game.IsOver; i++)
        {
            match.Update(TimeSpan.FromMilliseconds(10));
        }

        game.IsOver.Should().BeTrue();
    }
}
