using Jentl.Cards.Rulesets;
using Jentl.Script;
using static Jentl.Cards.Tests.RulesetTests;

namespace Jentl.Cards.Tests;

public class RulesetScriptTests
{
    private const string MiniDeck = """
        { "suits": [{ "name": "red" }, { "name": "blue" }],
          "ranks": [{ "name": "1", "points": 1 }, { "name": "2", "points": 2 }, { "name": "3", "points": 3 }, { "name": "4", "points": 4 }] }
        """;

    /// <summary>A shedding game for three on the mini deck, with <paramref name="script"/> and these effects.</summary>
    private static CardRuleset Shedding(string script, string effects = "[]") => CardRuleset.Parse($$"""
        {
          "name": "Scripted", "deck": {{MiniDeck}},
          "players": { "min": 2, "max": 4, "default": 3 },
          "piles": [
            { "name": "stock", "deal": { "rest": true, "faceUp": false } },
            { "name": "discard", "deal": { "cards": 1 }, "build": { "match": "suitOrRank" } },
            { "name": "hand", "perPlayer": true, "deal": { "cards": 2 }, "take": "one" }
          ],
          "effects": {{effects}},
          "script": {{System.Text.Json.JsonSerializer.Serialize(script)}}
        }
        """);

    // Discard red 1; seat 0 gets red 2 and blue 1, seat 1 red 3 and blue 2, seat 2 red 4 and blue 3.
    private static RulesetGame Deal(CardRuleset ruleset) =>
        new(ruleset, Order(ruleset, "red 1", "red 2", "red 3", "red 4", "blue 1", "blue 2", "blue 3"));

    [Fact]
    public void Played_ActsThroughTheTurn_AndUndoTakesItBack()
    {
        var game = Deal(Shedding("""
            fn played(card, seat)
              if card.rank == "2" then
                skip(1)
                draw(seat + 1, 2)
                count("twos")
                say("two!")
              end
            end
            """));

        var turn = game.Play(new ClickPile(game.Hand(0), 0));

        game.CurrentSeat.Should().Be(2, "the script skipped seat 1");
        game.Hand(1).Count.Should().Be(4);
        game.Table.Counter("script:twos").Should().Be(1);
        turn!.Tags.Should().Contain(["say:two!", "draw:2"]);
        game.Game.Undo();
        (game.CurrentSeat, game.Hand(1).Count, game.Table.Counter("script:twos")).Should().Be((0, 2, 0));
    }

    [Fact]
    public void AnEffect_MayCallTheScript_ToReverseOrGoAgain()
    {
        var ruleset = Shedding(
            """
            fn turnRound(card, seat)
              reverse()
            end

            fn more(card, seat)
              again()
            end
            """,
            """[{ "rank": "2", "script": "turnRound" }, { "rank": "4", "script": "more" }]""");
        var game = Deal(ruleset);

        game.Play(new ClickPile(game.Hand(0), 0));
        (game.Direction, game.CurrentSeat).Should().Be((-1, 2));

        game.Play(new ClickPile(game.Hand(2), 0));
        game.CurrentSeat.Should().Be(2, "red 4 lets its player go again");
    }

    [Fact]
    public void ACheck_MustAlsoSayYes()
    {
        var ruleset = CardRuleset.Parse($$"""
            {
              "name": "Checked", "deck": {{MiniDeck}},
              "players": 2,
              "piles": [
                { "name": "stock", "deal": { "rest": true, "faceUp": false } },
                { "name": "discard", "deal": { "cards": 1 }, "build": { "match": "suitOrRank", "check": "noBlue" } },
                { "name": "hand", "perPlayer": true, "deal": { "cards": 2 }, "take": "one" }
              ],
              "script": "fn noBlue(card, pile, from)\n  return card.suit != \"blue\" and from.group == \"hand\"\nend"
            }
            """);
        var game = new RulesetGame(ruleset, Order(ruleset, "red 1", "blue 1", "red 2"));

        game.Fits(game.Hand(0)[0].Card, game.Discard, 1, game.Hand(0)).Should().BeFalse("blue 1 matches by rank, but the check says no");
        game.LegalMoves(0).OfType<ClickPile>().Where(m => m.Pile == game.Hand(0)).Select(m => game.Hand(0)[m.Index].Card.Face.Code)
            .Should().NotContain("blue 1");
    }

    [Fact]
    public void Drawing_SaysHowManyToDraw()
    {
        var ruleset = Shedding("fn drawing(seat)\n  return 3\nend");
        var game = new RulesetGame(ruleset, seed: 1, players: 2);

        game.Play(new ClickPile(game.Stock, game.Stock.Count - 1));

        game.Hand(0).Count.Should().Be(5);
    }

    [Fact]
    public void Score_AndWon_AnswerForTheRules()
    {
        var game = Deal(Shedding("fn score(seat)\n  return 100 + seat\nend"));
        game.Score(2).Should().Be(102);

        var solitaire = CardRuleset.Parse($$"""
            {
              "name": "Quick", "deck": {{MiniDeck}},
              "piles": [
                { "name": "foundation", "goal": true },
                { "name": "column", "count": 2, "deal": { "cards": 1 }, "take": "top", "build": { "rank": "down" } },
                { "name": "stock", "deal": { "rest": true, "faceUp": false } }
              ],
              "script": [
                "fn moved(from, to, n)",
                "  count(\"moves\", n)",
                "  if to.top.rank == \"1\" then move(to, \"foundation\") end",
                "end",
                "fn won()",
                "  return counter(\"moves\") >= 1",
                "end"
              ]
            }
            """);
        var quick = new RulesetGame(solitaire, Order(solitaire, "red 2", "blue 1"));
        quick.Game.IsWon.Should().BeFalse();

        quick.Play(new DragCards(quick.Table["column1"], 0, quick.Table["column0"]))!.Should().NotBeNull();

        Codes(quick.Table["foundation"]).Should().Be("blue 1");
        quick.Game.IsWon.Should().BeTrue();
    }

    [Fact]
    public void MauMau_StacksItsSevens()
    {
        var ruleset = RulesetExamples.Load("maumau");
        var game = new RulesetGame(ruleset, Order(ruleset, "hearts 9", "hearts 7", "spades 7", "clubs K", "clubs 9", "spades K", "diamonds 10"), players: 2);

        game.Apply(0, new ClickPile(game.Hand(0), 0));
        game.Table.Counter("script:penalty").Should().Be(2);
        game.LegalMoves(1).OfType<ClickPile>().Where(m => m.Pile == game.Hand(1)).Select(m => game.Hand(1)[m.Index].Card.Face.Code)
            .Should().Equal(["spades 7"], "with a penalty waiting only a seven answers");

        game.Apply(1, new ClickPile(game.Hand(1), 0));
        game.Table.Counter("script:penalty").Should().Be(4);
        var before = game.Hand(0).Count;
        game.Apply(0, new ClickPile(game.Stock, game.Stock.Count - 1));
        game.Hand(0).Count.Should().Be(before + 4);
        game.Table.Counter("script:penalty").Should().Be(0);
    }

    [Theory]
    [InlineData("maumau", 2)]
    [InlineData("maumau", 5)]
    public void Bots_PlayMauMau(string name, int players)
    {
        var ruleset = RulesetExamples.Load(name);
        var bot = new RulesetBot();
        for (var seed = 1; seed <= 20; seed++)
        {
            var game = new RulesetGame(ruleset, seed, players);
            var random = new Random(seed);
            for (var moves = 0; !game.IsOver && moves < 5000; moves++)
            {
                game.Apply(game.CurrentSeat, bot.Choose(game, game.CurrentSeat, random));
            }

            game.IsOver.Should().BeTrue($"seed {seed}");
        }
    }

    [Fact]
    public void Golf_PutsNothingOnAKing()
    {
        var ruleset = RulesetExamples.Load("golf");

        // The columns get a card each in turn, five rounds (column0's top is the 29th); then the waste gets the 36th.
        var codes = ruleset.Deck.Cards.Select(c => c.Face.Code).Where(c => c is not ("QH" or "KS")).Take(34).ToList();
        codes.Insert(28, "QH");
        codes.Add("KS");
        var game = new RulesetGame(ruleset, Order(ruleset, [.. codes]));

        game.Table["column0"].Top!.Value.Card.Should().Be(Card.Parse("QH"));
        game.Table["waste"].Top!.Value.Card.Should().Be(Card.Parse("KS"));
        game.Fits(Card.Parse("QH"), game.Table["waste"], 1, game.Table["column0"]).Should().BeFalse("a queen is next to a king, but nothing goes on one");
        game.Play(new ClickPile(game.Table["column0"], 4)).Should().BeNull();
    }

    [Theory]
    [InlineData("""fn played(card, seat)\n  return card +\nend""", "ruleset.script: Bad:3: expected a value, got 'end'")]
    [InlineData("""fn other(card)\n  return true\nend""", "ruleset.piles[1].build.check: the script has no function 'missing'.")]
    public void ScriptMistakes_AreFoundWhenReading(string script, string message)
    {
        var json = $$"""
            {
              "name": "Bad", "deck": {{MiniDeck}}, "players": 2,
              "piles": [
                { "name": "stock", "deal": { "rest": true } },
                { "name": "discard", "deal": { "cards": 1 }, "build": { "match": "suitOrRank", "check": "missing" } },
                { "name": "hand", "perPlayer": true, "deal": { "cards": 2 }, "take": "one" }
              ],
              "script": "{{script}}"
            }
            """;

        var act = () => CardRuleset.Parse(json);

        act.Should().Throw<InvalidDataException>().WithMessage(message);
    }

    [Fact]
    public void ScriptMistakesWhilePlaying_SayTheLine_AndLeaveTheTableAsItWas()
    {
        var game = Deal(Shedding("fn played(card, seat)\n  say(\"ok\")\n  return card.points + nil\nend"));

        var act = () => game.Play(new ClickPile(game.Hand(0), 0));

        act.Should().Throw<ScriptException>().WithMessage("Scripted:3: can't add number 2 and nil");
        (game.Hand(0).Count, game.Discard.Count, game.CurrentSeat).Should().Be((2, 1, 0));
    }

    [Fact]
    public void Acting_OutsideAMove_IsAMistake()
    {
        var ruleset = CardRuleset.Parse($$"""
            {
              "name": "Sneaky", "deck": {{MiniDeck}}, "players": 2,
              "piles": [
                { "name": "stock", "deal": { "rest": true } },
                { "name": "discard", "deal": { "cards": 1 }, "build": { "match": "suitOrRank", "check": "sneaky" } },
                { "name": "hand", "perPlayer": true, "deal": { "cards": 2 }, "take": "one" }
              ],
              "script": "fn sneaky(card, pile, from)\n  skip(1)\n  return true\nend"
            }
            """);
        var game = new RulesetGame(ruleset, seed: 1);

        var act = () => game.Fits(game.Hand(0)[0].Card, game.Discard);

        act.Should().Throw<ScriptException>().WithMessage("*only works while a move is played*");
    }
}
