namespace Jentl.Cards.Tests;

public class DeckTests
{
    [Fact]
    public void StandardCards_HaveFaces()
    {
        var queen = Card.Parse("QH");

        queen.IsStandard.Should().BeTrue();
        queen.Face.Should().BeSameAs(CardFace.Standard(Card.Queen, Suit.Hearts));
        (queen.Face.Rank, queen.Face.Suit, queen.Face.Order, queen.Face.Colour, queen.Face.Points, queen.Face.Code).Should().Be(("Q", "hearts", 12, "red", 10, "QH"));
        Card.Parse("AS").Face.Points.Should().Be(1);
    }

    [Fact]
    public void Uno_HasItsHundredAndEightCards()
    {
        var deck = CardDeck.Uno();

        deck.Cards.Should().HaveCount(108).And.OnlyHaveUniqueItems();
        deck.Faces.Should().HaveCount(54);
        deck.Cards.Count(c => c.Face.Has("wild")).Should().Be(8);
        deck.Cards.Count(c => c.Face.Rank == "0").Should().Be(4);
        deck.Cards.Count(c => c.Face is { Rank: "7", Suit: "red" }).Should().Be(2);
        deck.Faces.Single(f => f.Code == "wild +4").Has("draw").Should().BeTrue();
    }

    [Fact]
    public void CustomCards_SayWhatTheyAre_ButHaveNoStandardSuit()
    {
        var skip = CardDeck.Uno().Cards.First(c => c.Face is { Rank: "skip", Suit: "blue" });

        skip.IsStandard.Should().BeFalse();
        skip.ToString().Should().Be("blue skip");
        skip.RankLabel.Should().Be("skip");
        skip.Rank.Should().Be(10);
        var suit = () => skip.Suit;
        suit.Should().Throw<InvalidOperationException>();
        skip.Should().NotBe(new Card(skip.Face, copy: 1));
        skip.Should().Be(new Card(skip.Face, copy: 0));
    }

    [Fact]
    public void StandardDecks_ComeInCopiesAndChosenSuits()
    {
        CardDeck.Standard().Cards.Should().HaveCount(52);
        CardDeck.Standard(2).Cards.Should().HaveCount(104).And.OnlyHaveUniqueItems();
        CardDeck.Standard(4, [Suit.Spades, Suit.Hearts]).Cards.Should().HaveCount(104)
            .And.OnlyContain(c => c.Suit == Suit.Spades || c.Suit == Suit.Hearts);
    }

    [Fact]
    public void TurnsMayMoveOneCardFromTheMiddle()
    {
        var table = new Table();
        var hand = table.Add("hand");
        var discard = table.Add("discard");
        foreach (var code in new[] { "2C", "3C", "4C" })
        {
            hand.Deal(Card.Parse(code), faceUp: true);
        }

        var game = new CardGame(table, new MiddleRules());
        game.Play(new ClickPile(hand, 1));

        hand.Cards.Select(c => c.Card.ToString()).Should().Equal("2C", "4C");
        discard.Top!.Value.Card.Should().Be(Card.Parse("3C"));
        game.Undo();
        hand.Cards.Select(c => c.Card.ToString()).Should().Equal("2C", "3C", "4C");
    }

    private sealed class MiddleRules : ICardRules
    {
        public bool Play(CardTurnBuilder turn, CardInput input)
        {
            var click = (ClickPile)input;
            turn.MoveOne(click.Pile, click.Index, turn.Table["discard"]);
            return true;
        }

        public bool IsWon(Table table) => false;
    }
}
