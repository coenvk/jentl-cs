using System.Globalization;

namespace Jentl.Cards;

/// <summary>
/// What a card is, the same way for every deck: its <see cref="Rank"/> and <see cref="Suit"/> by name, its place in the
/// rank order (<see cref="Order"/>, for building up and down), what it scores, the colour group rulesets alternate
/// (<see cref="Colour"/>), how to draw it, and tags for what it does (<c>wild</c>, <c>skip</c>). Standard cards have
/// one each (<see cref="Standard"/>); other decks make their own (<see cref="CardDeck"/>).
/// </summary>
/// <param name="Rank">The rank's name: "A", "7", "Q", "skip".</param>
/// <param name="Suit">The suit's name ("hearts", "red"), or null for a card of no suit (a wild).</param>
public sealed record CardFace(string Rank, string? Suit)
{
    private static readonly CardFace[] StandardFaces = MakeStandard();

    /// <summary>Its place in the rank order: 1 (ace) to 13 (king) for standard cards.</summary>
    public int Order { get; init; }

    /// <summary>What it scores left in a hand or won.</summary>
    public int Points { get; init; }

    /// <summary>What is written on it: "Q", "10", "+2".</summary>
    public string Label { get; init; } = Rank;

    /// <summary>The suit as one character to write beside the label ("♠"), or empty.</summary>
    public string SuitSymbol { get; init; } = "";

    /// <summary>The colour group: "red" or "black" for standard cards, which rulesets alternate; for other decks the suit's own.</summary>
    public string? Colour { get; init; }

    /// <summary>The colour to write and draw in, as <c>#rrggbb</c>.</summary>
    public string Ink { get; init; } = "#1c1e28";

    /// <summary>The card's background as <c>#rrggbb</c>, or null for plain white.</summary>
    public string? Fill { get; init; }

    /// <summary>What it does or is, for rulesets to match: "wild", "skip", "draw".</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Short, unique within its deck, for logs and tests: "QH", "red 7", "wild".</summary>
    public string Code { get; init; } = Suit is null ? Rank : $"{Suit} {Rank}";

    public bool Has(string tag) => Tags.Contains(tag, StringComparer.Ordinal);

    /// <summary>The face of a standard card.</summary>
    public static CardFace Standard(int rank, Cards.Suit suit) => StandardFaces[((int)suit * 13) + rank - 1];

    public override string ToString() => Code;

    private static CardFace[] MakeStandard()
    {
        var faces = new CardFace[52];
        foreach (var suit in Enum.GetValues<Cards.Suit>())
        {
            var red = suit is Cards.Suit.Hearts or Cards.Suit.Diamonds;
            for (var rank = Card.Ace; rank <= Card.King; rank++)
            {
                var label = rank switch
                {
                    Card.Ace => "A",
                    Card.Jack => "J",
                    Card.Queen => "Q",
                    Card.King => "K",
                    _ => rank.ToString(CultureInfo.InvariantCulture),
                };
                faces[((int)suit * 13) + rank - 1] = new CardFace(label, suit.ToString().ToLowerInvariant())
                {
                    Order = rank,
                    Points = Math.Min(rank, 10),
                    SuitSymbol = suit switch
                    {
                        Cards.Suit.Spades => "♠",
                        Cards.Suit.Hearts => "♥",
                        Cards.Suit.Diamonds => "♦",
                        _ => "♣",
                    },
                    Colour = red ? "red" : "black",
                    Ink = red ? "#ce2030" : "#1c1e28",
                    Code = label + suit.ToString()[0],
                };
            }
        }

        return faces;
    }
}
