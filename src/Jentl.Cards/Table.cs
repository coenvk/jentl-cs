namespace Jentl.Cards;

/// <summary>A card on a pile, face up or down.</summary>
public readonly record struct PlacedCard(Card Card, bool FaceUp);

/// <summary>An ordered stack of cards; index 0 is the bottom, the last card is on top.</summary>
public sealed class Pile
{
    private readonly List<PlacedCard> _cards = [];

    internal Pile(string name) => Name = name;

    public string Name { get; }

    public IReadOnlyList<PlacedCard> Cards => _cards;

    public int Count => _cards.Count;

    public bool IsEmpty => _cards.Count == 0;

    public PlacedCard? Top => _cards.Count > 0 ? _cards[^1] : null;

    public PlacedCard this[int index] => _cards[index];

    /// <summary>Puts cards on top during setup (dealing). During play, change piles only through turns.</summary>
    public void Deal(Card card, bool faceUp) => _cards.Add(new PlacedCard(card, faceUp));

    public override string ToString() => $"{Name} [{string.Join(' ', _cards.Select(c => c.FaceUp ? c.Card.ToString() : "##"))}]";

    internal List<PlacedCard> Mutable => _cards;
}

/// <summary>The named piles a card game is played on.</summary>
public sealed class Table
{
    private readonly List<Pile> _piles = [];
    private readonly Dictionary<string, Pile> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<Pile> Piles => _piles;

    public Pile this[string name] =>
        _byName.TryGetValue(name, out var pile) ? pile : throw new KeyNotFoundException($"No pile named '{name}'.");

    public Pile Add(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var pile = new Pile(name);
        if (!_byName.TryAdd(name, pile))
        {
            throw new ArgumentException($"A pile named '{name}' already exists.", nameof(name));
        }

        _piles.Add(pile);
        return pile;
    }

    /// <summary>Where a card is, or null if it is not on the table.</summary>
    public (Pile Pile, int Index)? Find(Card card)
    {
        foreach (var pile in _piles)
        {
            for (var i = 0; i < pile.Count; i++)
            {
                if (pile[i].Card == card)
                {
                    return (pile, i);
                }
            }
        }

        return null;
    }
}
