namespace Jentl.Cards.Tests;

public class CardKitTests
{
    [Fact]
    public void Standard52_HasEveryCardOnce()
    {
        var deck = Deck.Standard52();

        deck.Should().HaveCount(52).And.OnlyHaveUniqueItems();
        deck.Count(c => c.Color == CardColor.Red).Should().Be(26);
    }

    [Fact]
    public void Shuffled_IsAPermutation_AndRepeatsForASeed()
    {
        var first = Deck.Shuffled(Deck.Standard52(), new Random(7));
        var second = Deck.Shuffled(Deck.Standard52(), new Random(7));
        var other = Deck.Shuffled(Deck.Standard52(), new Random(8));

        first.Should().BeEquivalentTo(Deck.Standard52());
        second.Should().Equal(first);
        other.Should().NotEqual(first);
    }

    [Theory]
    [InlineData("AS", 1, Suit.Spades)]
    [InlineData("10H", 10, Suit.Hearts)]
    [InlineData("qd", 12, Suit.Diamonds)]
    [InlineData("KC", 13, Suit.Clubs)]
    public void Parse_ReadsShortCodes(string code, int rank, Suit suit)
    {
        var card = Card.Parse(code);

        card.Should().Be(new Card(rank, suit));
        card.ToString().Should().Be(code.ToUpperInvariant());
    }

    [Fact]
    public void Card_RejectsBadRanks()
    {
        var act = () => new Card(14, Suit.Hearts);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Play_MovesRunsAndFlips_AndUndoRestoresThem()
    {
        var (table, game) = TwoPiles("AS 2S 3H", "KD");
        var a = table["a"];
        var b = table["b"];
        table["a"].Mutable[0] = table["a"][0] with { FaceUp = false };

        game.Play(new DragCards(a, 1, b)).Should().NotBeNull();

        Codes(b).Should().Equal("KD", "2S", "3H");
        Codes(a).Should().Equal("AS");
        a[0].FaceUp.Should().BeTrue("the rules turn up the card left behind");

        game.Undo();
        Codes(a).Should().Equal("AS", "2S", "3H");
        a[0].FaceUp.Should().BeFalse();
        Codes(b).Should().Equal("KD");

        game.Redo();
        Codes(b).Should().Equal("KD", "2S", "3H");
        a[0].FaceUp.Should().BeTrue();
    }

    [Fact]
    public void Play_Rejected_RollsBackPartialChanges()
    {
        var (table, game) = TwoPiles("AS 2S", "KD");

        game.Play(new ClickPile(table["a"], 1)).Should().BeNull("these rules move then reject a click");

        Codes(table["a"]).Should().Equal("AS", "2S");
        Codes(table["b"]).Should().Equal("KD");
        game.History.Should().BeEmpty();
    }

    [Fact]
    public void Restart_ReturnsToTheDeal()
    {
        var (table, game) = TwoPiles("AS 2S 3H", "KD");
        game.Play(new DragCards(table["a"], 2, table["b"]));
        game.Play(new DragCards(table["a"], 1, table["b"]));

        game.Restart();

        Codes(table["a"]).Should().Equal("AS", "2S", "3H");
        game.CanUndo.Should().BeFalse();
        game.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Table_FindsCards()
    {
        var (table, _) = TwoPiles("AS 2S", "KD");

        table.Find(Card.Parse("2S")).Should().Be((table["a"], 1));
        table.Find(Card.Parse("QH")).Should().BeNull();
    }

    [Fact]
    public void Table_RejectsDuplicatePileNames()
    {
        var table = new Table();
        table.Add("x");

        var act = () => table.Add("x");

        act.Should().Throw<ArgumentException>();
    }

    private static (Table Table, CardGame Game) TwoPiles(string a, string b)
    {
        var table = new Table();
        foreach (var (name, cards) in new[] { ("a", a), ("b", b) })
        {
            var pile = table.Add(name);
            foreach (var code in cards.Split(' '))
            {
                pile.Deal(Card.Parse(code), faceUp: true);
            }
        }

        return (table, new CardGame(table, new AnyMoveRules()));
    }

    private static IEnumerable<string> Codes(Pile pile) => pile.Cards.Select(c => c.Card.ToString());

    /// <summary>Drags anything anywhere and turns up the new top card; a click moves a card and then rejects.</summary>
    private sealed class AnyMoveRules : ICardRules
    {
        public bool Play(CardTurnBuilder turn, CardInput input)
        {
            switch (input)
            {
                case DragCards drag:
                    turn.Move(drag.From, drag.Index, drag.To);
                    if (drag.From.Top is { FaceUp: false })
                    {
                        turn.Flip(drag.From, drag.From.Count - 1, faceUp: true);
                    }

                    return true;
                case ClickPile click:
                    turn.MoveTop(click.Pile, turn.Table["b"]);
                    return false;
                default:
                    return false;
            }
        }

        public bool IsWon(Table table) => false;
    }
}
