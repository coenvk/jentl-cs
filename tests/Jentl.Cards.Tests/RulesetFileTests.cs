using Jentl.Cards.Rulesets;

namespace Jentl.Cards.Tests;

public class RulesetFileTests
{
    private static string Fails(string json)
    {
        var act = () => CardRuleset.Parse(json);
        return act.Should().Throw<InvalidDataException>().Which.Message;
    }

    private const string Piles = """
        [{ "name": "foundation", "goal": true, "build": { "rank": "up", "suit": "same", "empty": "A" } },
         { "name": "stock", "deal": { "rest": true } }]
        """;

    [Fact]
    public void AGoodRuleset_ReadsBack()
    {
        var ruleset = CardRuleset.Parse($$"""{ "name": "Patience", "deck": "standard", "piles": {{Piles}} }""");

        ruleset.Name.Should().Be("Patience");
        ruleset.IsSolitaire.Should().BeTrue();
        ruleset.Piles[0].Build.Should().Match<BuildRule>(b => b.Rank == RankStep.Up && b.Suit == SuitMatch.Same && b.Empty == "A" && !b.Runs);
        ruleset.Win.Kind.Should().Be(WinKind.AllOnGoals);
    }

    [Theory]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [], "bonus": 1 }""", "ruleset.bonus: isn't a field here*")]
    [InlineData("""{ "name": "X", "deck": "tarot", "piles": [] }""", "ruleset.deck: 'tarot' isn't a deck Jentl knows*")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a", "build": { "rank": "upp" } }] }""", "ruleset.piles[0].build.rank: 'upp' isn't a rank (use any, up, down, updown, same).")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a", "click": { "to": "waste" } }] }""", "ruleset.piles[0].click.to: there is no pile 'waste'*")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a1" }] }""", "ruleset.piles[0].name: 'a1' should be letters and digits, not ending in a digit.")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a", "count": 99 }] }""", "ruleset.piles[0].count: should be a whole number from 1 to 52.")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a" }] }""", "ruleset.win: to win with every card on a goal pile*")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [{ "name": "a", "goal": true, "build": { "empty": "Z" } }] }""", "ruleset.piles[0].build.empty: the deck has no rank 'Z'.")]
    [InlineData("""{ "name": "X", "deck": "standard", "players": 3, "piles": [{ "name": "stock" }, { "name": "discard" }] }""", "ruleset.turns.hand: a game for several players needs a pile 'hand'*")]
    [InlineData("""{ "name": "X", "deck": "uno", "players": 2, "piles": [{ "name": "stock" }, { "name": "discard" }, { "name": "hand", "perPlayer": true }], "effects": [{ "rank": "draw4", "draw": 4 }] }""", "ruleset.effects[0]: no card in the deck is rank 'draw4'.")]
    [InlineData("""{ "name": "X", "deck": "standard", "piles": [ }""", "The ruleset isn't valid JSON*")]
    public void Mistakes_SayWhereTheyAre(string json, string message) =>
        Fails(json).Should().Match(message);

    [Fact]
    public void ADeckOfItsOwn_IsReadWithJokers()
    {
        var ruleset = CardRuleset.Parse($$"""
            {
              "name": "Stars", "jokers": 2,
              "deck": {
                "name": "stars",
                "suits": [{ "name": "sun", "symbol": "*", "colour": "warm", "ink": "#ffffff", "fill": "#ff8800" }, { "name": "moon" }],
                "ranks": [{ "name": "one", "label": "I", "points": 1, "copies": 2 }, { "name": "two", "label": "II", "tags": ["x"] }]
              },
              "piles": [{ "name": "foundation", "goal": true, "build": { "empty": "one" } }, { "name": "stock", "deal": { "rest": true } }]
            }
            """);

        ruleset.Deck.Cards.Should().HaveCount(((2 + 1) * 2) + 2);
        var one = ruleset.Deck.Faces.First(f => f.Code == "sun one");
        (one.Label, one.Order, one.Points, one.Colour, one.Ink, one.Fill, one.SuitSymbol).Should().Be(("I", 1, 1, "warm", "#ffffff", "#ff8800", "*"));
        ruleset.Deck.Faces.First(f => f.Code == "moon two").Order.Should().Be(2);
        ruleset.Deck.Faces.Should().Contain(f => f.Rank == "joker" && f.Has("wild"));
    }
}
