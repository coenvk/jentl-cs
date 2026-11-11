using Jentl.Cards.Rulesets;

namespace Jentl.Cards.Tests;

public class RulesetTests
{
    private const string TwoSuits = """
        { "suits": [{ "name": "red", "colour": "red" }, { "name": "black", "colour": "black" }],
          "ranks": [{ "name": "A" }, { "name": "2" }, { "name": "3" }, { "name": "4" }, { "name": "5" }] }
        """;

    /// <summary>The cards named first, in that order, then the rest of the deck.</summary>
    internal static List<Card> Order(CardRuleset ruleset, params string[] codes) => Order(ruleset, codes, []);

    /// <summary>
    /// The cards named <paramref name="first"/>, the rest of the deck, then the cards named <paramref name="last"/>: dealt
    /// last, so on top of a stock that takes the rest.
    /// </summary>
    internal static List<Card> Order(CardRuleset ruleset, string[] first, string[] last)
    {
        var used = new HashSet<Card>();
        Card Pick(string code) => ruleset.Deck.Cards.First(c => c.Face.Code == code && used.Add(c));
        var front = first.Select(Pick).ToList();
        var back = last.Select(Pick).ToList();
        return [.. front, .. ruleset.Deck.Cards.Where(c => !used.Contains(c)), .. back];
    }

    internal static string Codes(Pile pile) => string.Join(" ", pile.Cards.Select(c => (c.FaceUp ? "" : "~") + c.Card.Face.Code));

    [Fact]
    public void TheExamples_AreAllThere()
    {
        RulesetExamples.Names.Should().Equal("crazyeights", "fortythieves", "freecell", "golf", "klondike", "maumau", "spider", "uno", "yukon");
    }

    [Theory]
    [InlineData("klondike")]
    [InlineData("freecell")]
    [InlineData("spider")]
    [InlineData("golf")]
    [InlineData("yukon")]
    [InlineData("fortythieves")]
    [InlineData("crazyeights")]
    [InlineData("uno")]
    [InlineData("maumau")]
    public void EveryExample_DealsTheWholeDeck(string name)
    {
        var ruleset = RulesetExamples.Load(name);

        var game = new RulesetGame(ruleset, seed: 7);

        game.Table.Piles.Sum(p => p.Count).Should().Be(ruleset.Deck.Cards.Count);
        game.Table.Piles.SelectMany(p => p.Cards).Select(c => c.Card).Should().OnlyHaveUniqueItems();
        game.IsOver.Should().BeFalse();
        game.LegalMoves(game.CurrentSeat).Should().NotBeEmpty();
    }

    [Fact]
    public void Klondike_DealsItsColumnsWithTheTopsUp()
    {
        var game = new RulesetGame(RulesetExamples.Load("klondike"), seed: 1);

        var columns = game.Piles("column");
        columns.Select(c => c.Count).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        columns.Should().OnlyContain(c => c.Top!.Value.FaceUp && c.Cards.Count(x => x.FaceUp) == 1);
        game.Table["stock"].Count.Should().Be(24);
        game.Table["stock"].Cards.Should().OnlyContain(c => !c.FaceUp);
    }

    [Fact]
    public void Yukon_And_Golf_DealAsTheySay()
    {
        var yukon = new RulesetGame(RulesetExamples.Load("yukon"), seed: 1).Piles("column");
        yukon.Select(c => c.Count).Should().Equal(1, 6, 7, 8, 9, 10, 11);
        yukon.Select(c => c.Cards.Count(x => !x.FaceUp)).Should().Equal(0, 1, 2, 3, 4, 5, 6);

        var golf = new RulesetGame(RulesetExamples.Load("golf"), seed: 1);
        golf.Piles("column").Should().OnlyContain(c => c.Count == 5);
        (golf.Table["waste"].Count, golf.Table["stock"].Count).Should().Be((1, 16));
    }

    [Fact]
    public void Uno_DealsSevenEach_AndNeverStartsOnAWild()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var game = new RulesetGame(RulesetExamples.Load("uno"), seed, players: 3);

            Enumerable.Range(0, 3).Select(s => game.Hand(s).Count).Should().Equal(7, 7, 7);
            game.Discard.Should().Match<Pile>(d => d.Count == 1 && !d.Top!.Value.Card.Face.Has("wild"));
            game.Stock.Count.Should().Be(108 - 22);
        }
    }

    private static CardRuleset Tiny() => CardRuleset.Parse($$"""
        {
          "name": "Tiny", "deck": {{TwoSuits}},
          "piles": [
            { "name": "stock", "deal": { "rest": true, "faceUp": false }, "click": { "to": "waste", "redealFrom": "waste", "redeals": 1 } },
            { "name": "waste", "take": "top" },
            { "name": "foundation", "count": 2, "goal": true, "build": { "rank": "up", "suit": "same", "empty": "A" }, "take": "top" },
            { "name": "column", "count": 2, "fan": "down", "deal": { "cards": [3, 2], "faceUp": "top" },
              "build": { "rank": "down", "suit": "alternate", "empty": "5" }, "take": "run" }
          ]
        }
        """);

    [Fact]
    public void ASolitaire_IsPlayedByDragsAndTaps_UntilWon()
    {
        var ruleset = Tiny();

        // Dealt a card at a time round the columns: column0 gets the 1st, 3rd and 5th, column1 the 2nd and 4th.
        var order = Order(ruleset, "red 3", "black 4", "black 5", "red 2", "black 2", "red 4", "black A", "red 5", "black 3", "red A");
        var game = new RulesetGame(ruleset, order);
        var (column0, column1) = (game.Table["column0"], game.Table["column1"]);
        var (foundation0, waste, stock) = (game.Table["foundation0"], game.Table["waste"], game.Table["stock"]);
        Codes(column0).Should().Be("~red 3 ~black 5 black 2");
        Codes(column1).Should().Be("~black 4 red 2");
        Codes(stock).Should().Be("~red 4 ~black A ~red 5 ~black 3 ~red A");

        game.Play(new ClickPile(stock, 4))!.Has("deal").Should().BeTrue();
        Codes(waste).Should().Be("red A");
        game.Play(new DragCards(waste, 0, foundation0))!.Has("goal").Should().BeTrue();
        game.Play(new DragCards(column1, 1, column0)).Should().BeNull("red 2 doesn't go on black 2");
        game.Play(new ClickPile(column1, 1))!.Has("reveal").Should().BeTrue("a tap sends red 2 to its foundation");
        Codes(foundation0).Should().Be("red A red 2");
        Codes(column1).Should().Be("black 4");

        game.Play(new DragCards(column0, 2, column1)).Should().BeNull("black 2 doesn't go on black 4");
        game.Hints(game.Table).Should().ContainEquivalentOf(new ClickPile(stock, 3));
        game.Game.IsWon.Should().BeFalse();
    }

    [Fact]
    public void TheStock_TurnsBackOver_AsOftenAsAllowed()
    {
        var game = new RulesetGame(Tiny(), seed: 3);
        var (stock, waste) = (game.Table["stock"], game.Table["waste"]);

        while (!stock.IsEmpty)
        {
            game.Play(new ClickPile(stock, stock.Count - 1));
        }

        game.Play(new ClickPile(stock, -1))!.Has("redeal").Should().BeTrue();
        (stock.Count, waste.Count).Should().Be((5, 0));
        stock.Cards.Should().OnlyContain(c => !c.FaceUp);
        while (!stock.IsEmpty)
        {
            game.Play(new ClickPile(stock, stock.Count - 1));
        }

        game.Play(new ClickPile(stock, -1)).Should().BeNull("one redeal only");
        game.Game.Undo();
        stock.Count.Should().Be(1);
    }

    [Fact]
    public void Runs_MoveTogether_WithinTheFreeCellLimit()
    {
        var ruleset = CardRuleset.Parse($$"""
            {
              "name": "Cells", "deck": {{TwoSuits}},
              "piles": [
                { "name": "cell", "build": { "max": 1 }, "take": "top" },
                { "name": "foundation", "goal": true, "build": { "rank": "up", "suit": "same", "empty": "A" } },
                { "name": "column", "count": 4, "fan": "down", "deal": { "cards": [3, 1, 0, 2] },
                  "build": { "rank": "down", "suit": "alternate" }, "take": { "kind": "run", "limit": "freecells" } },
                { "name": "rest", "deal": { "rest": true } }
              ]
            }
            """);
        var game = new RulesetGame(ruleset, Order(ruleset, "red 4", "black 5", "red A", "black 3", "black A", "red 2"));
        var columns = game.Piles("column");
        Codes(columns[0]).Should().Be("red 4 black 3 red 2");
        game.CanTake(columns[0], 0).Should().BeTrue();

        // A free cell and an empty column: (1 + 1) × 2 = 4 cards may move.
        game.Play(new DragCards(columns[0], 0, columns[1])).Should().NotBeNull();
        game.Game.Undo();

        // With the cell taken, (0 + 1) × 2 = 2 only.
        game.Play(new DragCards(columns[3], 1, game.Table["cell"])).Should().NotBeNull();
        game.Play(new DragCards(columns[0], 0, columns[1])).Should().BeNull();
        game.Play(new DragCards(columns[0], 1, columns[2])).Should().BeNull("going to the empty column, that column doesn't count: one card only");
        game.Play(new DragCards(columns[0], 2, columns[2])).Should().NotBeNull();
    }

    [Fact]
    public void ACompleteRun_LeavesTheTable()
    {
        var ruleset = CardRuleset.Parse("""
            {
              "name": "Little spider",
              "deck": { "suits": [{ "name": "spades" }], "ranks": [{ "name": "A" }, { "name": "2" }, { "name": "3" }] },
              "piles": [
                { "name": "foundation", "goal": true },
                { "name": "column", "count": 2, "fan": "down", "deal": { "cards": [2, 1] },
                  "build": { "rank": "down" }, "take": { "kind": "run", "suit": "same" }, "complete": { "length": 3, "to": "foundation" } }
              ]
            }
            """);
        var game = new RulesetGame(ruleset, Order(ruleset, "spades 3", "spades A", "spades 2"));

        var turn = game.Play(new DragCards(game.Table["column1"], 0, game.Table["column0"]));

        turn!.Has("complete").Should().BeTrue();
        Codes(game.Table["foundation"]).Should().Be("spades 3 spades 2 spades A");
        game.Game.IsWon.Should().BeTrue();
    }

    [Fact]
    public void AWonSolitaire_FinishesByItself()
    {
        var ruleset = Tiny();
        var game = new RulesetGame(ruleset, Order(ruleset, "red 3", "black 4", "black 5", "red 2", "black 2", "red 4", "black A", "red 5", "black 3", "red A"));
        var played = 0;

        // Deal everything out, then let hints and the automatic moves do the rest.
        while (!game.Game.IsWon && played++ < 200)
        {
            var move = game.Game.PlayAutoMove() is null ? game.Hints(game.Table).FirstOrDefault() : null;
            if (move is not null)
            {
                game.Play(move);
            }
        }

        game.Game.IsWon.Should().BeTrue();
        game.Table["foundation0"].Count.Should().Be(5);
    }

    [Fact]
    public void Hints_AreAllPlayable()
    {
        foreach (var name in new[] { "klondike", "freecell", "spider", "golf", "yukon", "fortythieves" })
        {
            var game = new RulesetGame(RulesetExamples.Load(name), seed: 11);
            foreach (var hint in game.Hints(game.Table).ToList())
            {
                game.Play(hint).Should().NotBeNull($"{name}: {hint} was suggested");
                game.Game.Undo();
            }
        }
    }
}
