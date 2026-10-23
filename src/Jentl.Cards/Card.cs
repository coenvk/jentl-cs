namespace Jentl.Cards;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

public enum CardColor
{
    Black,
    Red,
}

/// <summary>A playing card. Ranks run from 1 (ace) to 13 (king).</summary>
public readonly record struct Card
{
    public const int Ace = 1;
    public const int Jack = 11;
    public const int Queen = 12;
    public const int King = 13;

    /// <param name="copy">Which copy of this card, for games played with several decks (0 for the first).</param>
    public Card(int rank, Suit suit, int copy = 0)
    {
        if (rank is < Ace or > King)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank must be 1 (ace) to 13 (king).");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(copy);
        Rank = rank;
        Suit = suit;
        Copy = copy;
    }

    public int Rank { get; }

    public Suit Suit { get; }

    /// <summary>
    /// Tells identical cards from different decks apart, so every card on a table is unique (the screen
    /// tracks cards by identity to animate them). Rules should compare <see cref="Rank"/> and <see cref="Suit"/>.
    /// </summary>
    public int Copy { get; }

    public CardColor Color => Suit is Suit.Hearts or Suit.Diamonds ? CardColor.Red : CardColor.Black;

    /// <summary>"A", "2" ... "10", "J", "Q", "K".</summary>
    public string RankLabel => Rank switch
    {
        Ace => "A",
        Jack => "J",
        Queen => "Q",
        King => "K",
        _ => Rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Short code such as "QH" or "10S", handy in tests and logs.</summary>
    public override string ToString() => RankLabel + Suit.ToString()[0];

    /// <summary>Parses a short code such as "QH", "10S" or "AD".</summary>
    public static Card Parse(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var suit = char.ToUpperInvariant(code[^1]) switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            'S' => Suit.Spades,
            _ => throw new FormatException($"Unknown suit in '{code}'."),
        };
        var rank = code[..^1].ToUpperInvariant() switch
        {
            "A" => Ace,
            "J" => Jack,
            "Q" => Queen,
            "K" => King,
            var n when int.TryParse(n, System.Globalization.CultureInfo.InvariantCulture, out var value) => value,
            _ => throw new FormatException($"Unknown rank in '{code}'."),
        };
        return new Card(rank, suit);
    }
}

public static class Deck
{
    /// <summary>The 52 cards of a standard deck, ordered by suit then rank.</summary>
    /// <param name="copy">Marks the deck's cards as this copy, when several decks are combined.</param>
    public static IReadOnlyList<Card> Standard52(int copy = 0) =>
        Enum.GetValues<Suit>().SelectMany(suit => Enumerable.Range(Card.Ace, Card.King).Select(rank => new Card(rank, suit, copy))).ToArray();

    /// <summary>
    /// One suit run ace to king for each entry of <paramref name="suits"/>, each a distinct copy:
    /// e.g. eight times spades for one-suit Spider.
    /// </summary>
    public static IReadOnlyList<Card> Suits(IEnumerable<Suit> suits)
    {
        ArgumentNullException.ThrowIfNull(suits);
        var copies = new Dictionary<Suit, int>();
        var cards = new List<Card>();
        foreach (var suit in suits)
        {
            var copy = copies.GetValueOrDefault(suit);
            copies[suit] = copy + 1;
            cards.AddRange(Enumerable.Range(Card.Ace, Card.King).Select(rank => new Card(rank, suit, copy)));
        }

        return cards;
    }

    /// <summary>
    /// A Fisher-Yates shuffle driven by <paramref name="random"/>, so a seeded generator gives the same
    /// deal every time (deal numbers, replays, tests).
    /// </summary>
    public static IReadOnlyList<Card> Shuffled(IEnumerable<Card> cards, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var deck = cards.ToArray();
        for (var i = deck.Length - 1; i > 0; i--)
        {
#pragma warning disable CA5394 // Card shuffling, not security relevant.
            var j = random.Next(i + 1);
#pragma warning restore CA5394
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        return deck;
    }
}
