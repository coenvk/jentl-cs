namespace Jentl.Cards;

/// <summary>A suit of a deck of its own: its name, symbol, the colour group rulesets alternate, and how to draw it.</summary>
public sealed record SuitDesign(string Name)
{
    public string Symbol { get; init; } = "";

    /// <summary>The colour group; the suit's name if not given.</summary>
    public string? Colour { get; init; }

    public string Ink { get; init; } = "#1c1e28";

    public string? Fill { get; init; }
}

/// <summary>
/// A rank of a deck of its own: its name, place in the order, points, label, tags, and how many of each suit there
/// are (two of most Uno cards).
/// </summary>
public sealed record RankDesign(string Name, int Order)
{
    public string? Label { get; init; }

    public int Points { get; init; }

    public int Copies { get; init; } = 1;

    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// The cards a game is played with: the standard 52 (in any number, or of chosen suits), Uno's 108, or a deck made of
/// suits and ranks of its own plus cards of no suit (wilds, jokers). Every card is unique (copies are numbered), and
/// <see cref="Faces"/> lists each kind once.
/// </summary>
public sealed class CardDeck
{
    private CardDeck(string name, IReadOnlyList<Card> cards)
    {
        Name = name;
        Cards = cards;
        Faces = [.. cards.Select(card => card.Face).Distinct()];
    }

    public string Name { get; }

    /// <summary>Every card, in order (suit by suit, then the cards of no suit), unshuffled.</summary>
    public IReadOnlyList<Card> Cards { get; }

    /// <summary>Each kind of card once.</summary>
    public IReadOnlyList<CardFace> Faces { get; }

    /// <summary>Standard decks: <paramref name="copies"/> of the 52, or one run ace to king per entry of <paramref name="suits"/> (eight spades for one-suit Spider).</summary>
    public static CardDeck Standard(int copies = 1, IReadOnlyList<Suit>? suits = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(copies, 1);
        IReadOnlyList<Card> cards = suits is null
            ? [.. Enumerable.Range(0, copies).SelectMany(Deck.Standard52)]
            : Deck.Suits(Enumerable.Repeat(suits, copies).SelectMany(s => s));
        return new CardDeck(copies == 1 && suits is null ? "standard" : "standard decks", cards);
    }

    /// <summary>
    /// Uno's 108 cards: in red, yellow, green and blue one 0, two each of 1 to 9, "skip", "reverse" and "+2"; and four
    /// each of "wild" and "+4" (a wild that makes the next player draw four), of no colour. Numbers score their value,
    /// the coloured actions 20 and the wilds 50. Tags: <c>skip</c>, <c>reverse</c>, <c>draw</c> and <c>wild</c>.
    /// </summary>
    public static CardDeck Uno()
    {
        SuitDesign[] colours =
        [
            new("red") { Ink = "#ffffff", Fill = "#d63a3a" },
            new("yellow") { Ink = "#ffffff", Fill = "#e8b416" },
            new("green") { Ink = "#ffffff", Fill = "#3a9d4a" },
            new("blue") { Ink = "#ffffff", Fill = "#2f6fd0" },
        ];
        RankDesign[] ranks =
        [
            new("0", 0),
            .. Enumerable.Range(1, 9).Select(n => new RankDesign(n.ToString(System.Globalization.CultureInfo.InvariantCulture), n) { Points = n, Copies = 2 }),
            new("skip", 10) { Points = 20, Copies = 2, Tags = ["skip", "action"] },
            new("reverse", 11) { Label = "rev", Points = 20, Copies = 2, Tags = ["reverse", "action"] },
            new("+2", 12) { Points = 20, Copies = 2, Tags = ["draw", "action"] },
        ];
        CardFace[] wilds =
        [
            new("wild", null) { Order = 13, Points = 50, Tags = ["wild"], Ink = "#ffffff", Fill = "#24242c" },
            new("+4", null) { Order = 14, Points = 50, Tags = ["wild", "draw"], Ink = "#ffffff", Fill = "#24242c", Code = "wild +4" },
        ];
        return Custom("uno", colours, ranks, [.. wilds.Select(face => (face, 4))]);
    }

    /// <summary>
    /// A deck of its own: every rank in every suit (as many copies as the rank says), then <paramref name="extra"/>
    /// cards of no suit, each as many times as given.
    /// </summary>
    public static CardDeck Custom(string name, IReadOnlyList<SuitDesign> suits, IReadOnlyList<RankDesign> ranks, IReadOnlyList<(CardFace Face, int Count)>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(suits);
        ArgumentNullException.ThrowIfNull(ranks);
        var cards = new List<Card>();
        foreach (var suit in suits)
        {
            foreach (var rank in ranks)
            {
                var face = new CardFace(rank.Name, suit.Name)
                {
                    Order = rank.Order,
                    Points = rank.Points,
                    Label = rank.Label ?? rank.Name,
                    SuitSymbol = suit.Symbol,
                    Colour = suit.Colour ?? suit.Name,
                    Ink = suit.Ink,
                    Fill = suit.Fill,
                    Tags = rank.Tags,
                };
                for (var copy = 0; copy < rank.Copies; copy++)
                {
                    cards.Add(new Card(face, copy));
                }
            }
        }

        foreach (var (face, count) in extra ?? [])
        {
            for (var copy = 0; copy < count; copy++)
            {
                cards.Add(new Card(face, copy));
            }
        }

        if (cards.Select(card => card.Face.Code).Distinct().Count() != cards.Select(card => card.Face).Distinct().Count())
        {
            throw new ArgumentException("Two different kinds of card in the deck have the same code.", nameof(ranks));
        }

        return new CardDeck(name, cards);
    }

    /// <summary>The deck with <paramref name="extra"/> cards of no suit added, each as many times as given (jokers).</summary>
    public CardDeck With(IReadOnlyList<(CardFace Face, int Count)> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        return new CardDeck(Name, [.. Cards, .. extra.SelectMany(e => Enumerable.Range(0, e.Count).Select(copy => new Card(e.Face, copy)))]);
    }

    public override string ToString() => $"{Name} ({Cards.Count} cards)";
}
