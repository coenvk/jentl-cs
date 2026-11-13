namespace Jentl.Cards.Rulesets;

/// <summary>How the ranks of a card and the card under it must go, for building on a pile or for a run of cards.</summary>
public enum RankStep
{
    /// <summary>Any rank.</summary>
    Any,

    /// <summary>One higher: a 7 on a 6.</summary>
    Up,

    /// <summary>One lower: a 6 on a 7.</summary>
    Down,

    /// <summary>One higher or one lower.</summary>
    UpOrDown,

    /// <summary>The same rank.</summary>
    Same,
}

/// <summary>How the suits of a card and the card under it must go.</summary>
public enum SuitMatch
{
    Any,
    Same,

    /// <summary>The other colour group: red on black, black on red.</summary>
    Alternate,

    /// <summary>The same colour group.</summary>
    SameColour,

    /// <summary>Any suit but the same.</summary>
    Different,
}

/// <summary>How a card may be put on a pile.</summary>
public sealed record BuildRule
{
    /// <summary>A pile no card may be put on (only dealt to).</summary>
    public static BuildRule None { get; } = new() { Closed = true };

    /// <summary>Nothing may be put on it.</summary>
    public bool Closed { get; init; }

    public RankStep Rank { get; init; } = RankStep.Any;

    /// <summary>Whether ranks go round: a king on an ace building up (or an ace on a king building down).</summary>
    public bool Wrap { get; init; }

    public SuitMatch Suit { get; init; } = SuitMatch.Any;

    /// <summary>
    /// Shedding games' rule instead of <see cref="Rank"/> and <see cref="Suit"/>: the card must have the top card's
    /// suit (or the suit chosen for a wild) or its rank. Cards tagged <c>wild</c> always go.
    /// </summary>
    public bool MatchSuitOrRank { get; init; }

    /// <summary>
    /// What an empty pile takes: null for any card, the name of a rank ("K", "A") for only that rank, or "none" for
    /// nothing.
    /// </summary>
    public string? Empty { get; init; }

    /// <summary>The most cards the pile may hold (1 for FreeCell's cells), or 0 for no limit.</summary>
    public int Max { get; init; }

    /// <summary>Only cards from these piles or groups of piles may go on it, or any when empty.</summary>
    public IReadOnlyList<string> From { get; init; } = [];

    /// <summary>Whether a run of several cards may be put on it at once, rather than single cards only (goal piles: single cards unless they say).</summary>
    public bool Runs { get; init; } = true;

    /// <summary>
    /// With <see cref="MatchSuitOrRank"/>: ranks or tags of cards that go on anything (Crazy Eights' "8"); cards tagged
    /// <c>wild</c> always do.
    /// </summary>
    public IReadOnlyList<string> Wild { get; init; } = [];

    /// <summary>
    /// A function of the ruleset's <see cref="CardRuleset.Script"/> that must also say yes: called with the card, the pile
    /// and the pile it comes from (nil when dealt), as <c>fn check(card, pile, from)</c>.
    /// </summary>
    public string? Check { get; init; }

    /// <summary>Whether <paramref name="face"/> goes on anything here.</summary>
    public bool IsWild(CardFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.Has("wild") || Wild.Any(w => w == face.Rank || face.Has(w));
    }
}

/// <summary>Which cards of a pile may be picked up.</summary>
public enum TakeKind
{
    /// <summary>None: the pile is only played onto, or dealt from.</summary>
    None,

    /// <summary>The top card.</summary>
    Top,

    /// <summary>The top card, or any run on top that keeps to <see cref="TakeRule.RunRank"/> and <see cref="TakeRule.RunSuit"/>.</summary>
    Run,

    /// <summary>Any face-up card, with everything on top of it (Yukon).</summary>
    Any,

    /// <summary>Any one card, leaving the cards above it: a hand.</summary>
    One,
}

/// <summary>How many cards may move in a run at once.</summary>
public enum RunLimit
{
    None,

    /// <summary>As FreeCell has it: one more than the empty cells, doubled for each empty column the run isn't going to.</summary>
    FreeCells,
}

/// <summary>Which cards of a pile may be picked up, and in what runs.</summary>
public sealed record TakeRule(TakeKind Kind)
{
    public RankStep RunRank { get; init; } = RankStep.Down;

    public SuitMatch RunSuit { get; init; } = SuitMatch.Any;

    public RunLimit Limit { get; init; }
}

/// <summary>What tapping a pile does: deal from it, as a stock does.</summary>
public sealed record ClickRule
{
    /// <summary>How many cards one tap deals (to each pile, with <see cref="Each"/>).</summary>
    public int Deal { get; init; } = 1;

    /// <summary>The pile or group of piles they go to.</summary>
    public required string To { get; init; }

    /// <summary>Whether every pile of the group gets <see cref="Deal"/> cards (Spider), rather than the first one.</summary>
    public bool Each { get; init; }

    public bool FaceUp { get; init; } = true;

    /// <summary>Whether every pile of the group must have a card before dealing to them all (Spider).</summary>
    public bool RequireFilled { get; init; }

    /// <summary>The pile turned back over into this one when it is empty and tapped, or null for none.</summary>
    public string? RedealFrom { get; init; }

    /// <summary>How often it may be turned back over: -1 for always.</summary>
    public int Redeals { get; init; } = -1;
}

/// <summary>A run that leaves the table once complete: Spider's king to ace in one suit, off to a foundation.</summary>
public sealed record CompleteRule(int Length, string To)
{
    public RankStep Rank { get; init; } = RankStep.Down;

    public SuitMatch Suit { get; init; } = SuitMatch.Same;
}

/// <summary>How a pile's cards are spread out on screen.</summary>
public enum PileFan
{
    Stacked,
    Down,
    Right,
}

/// <summary>How many cards a pile gets at the deal, and which face up.</summary>
public sealed record DealRule
{
    /// <summary>Cards per pile of the group, by index; the last number holds for the rest. Empty with <see cref="Rest"/>.</summary>
    public IReadOnlyList<int> Counts { get; init; } = [0];

    /// <summary>Whether the pile takes every card left after the others are dealt (a stock).</summary>
    public bool Rest { get; init; }

    /// <summary>Face down cards at the bottom of each pile, by index like <see cref="Counts"/>; the rest face up.</summary>
    public IReadOnlyList<int>? FaceDown { get; init; }

    /// <summary>Whether cards are face up, when <see cref="FaceDown"/> doesn't say.</summary>
    public bool FaceUp { get; init; } = true;

    /// <summary>Whether only the top card is face up.</summary>
    public bool TopOnly { get; init; }

    /// <summary>
    /// Whether covered piles are dealt face up too (<c>"faceUp": "all"</c>: Pyramid, where every card shows); otherwise
    /// a covered card lies face down until what is over it has gone (TriPeaks).
    /// </summary>
    public bool Open { get; init; }

    /// <summary>Cards of this rank or with this tag aren't dealt here: put to the back of the deck and the next one taken (no wild to start the discard).</summary>
    public string? Not { get; init; }

    /// <summary>Whether <paramref name="face"/> is kept from this pile by <see cref="Not"/>.</summary>
    public bool Keeps(CardFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return Not is not null && (face.Rank == Not || face.Has(Not));
    }

    public int CountFor(int index) => Counts.Count == 0 ? 0 : Counts[Math.Min(index, Counts.Count - 1)];

    public int FaceDownFor(int index, int count) =>
        FaceDown is { Count: > 0 } down ? Math.Min(count, down[Math.Min(index, down.Count - 1)])
        : TopOnly ? Math.Max(0, count - 1)
        : FaceUp ? 0 : count;
}

/// <summary>
/// A pile, or a group of alike piles (<see cref="Count"/> of them, named <c>tableau0</c>, <c>tableau1</c> ...; or one per
/// player with <see cref="PerPlayer"/>): what it gets at the deal, what may be put on it and taken from it, what a tap
/// does, and where it goes on screen.
/// </summary>
public sealed record PileRule(string Name)
{
    public int Count { get; init; } = 1;

    /// <summary>One for each player: their hand.</summary>
    public bool PerPlayer { get; init; }

    /// <summary>Whether cards here count as done: foundations, which a won solitaire has every card on.</summary>
    public bool Goal { get; init; }

    public DealRule Deal { get; init; } = new();

    public BuildRule Build { get; init; } = BuildRule.None;

    public TakeRule Take { get; init; } = new(TakeKind.None);

    public ClickRule? Click { get; init; }

    public CompleteRule? Complete { get; init; }

    /// <summary>Whether a face-down card left on top turns face up by itself.</summary>
    public bool FlipTop { get; init; } = true;

    /// <summary>
    /// For a layout of overlapping cards (TriPeaks, Pyramid): for each pile of the group, the piles of the same group
    /// lying over it, by number from 0. A covered pile can't be taken from, and its card turns face up once every pile
    /// over it is empty.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>>? CoveredBy { get; init; }

    /// <summary>Where each pile of the group lies, in card widths and heights, instead of <see cref="At"/> stepped by <see cref="Step"/>: for layouts that aren't rows.</summary>
    public IReadOnlyList<(float X, float Y)>? Places { get; init; }

    /// <summary>Where the first pile goes, in card widths and heights from the top left.</summary>
    public (float X, float Y) At { get; init; }

    /// <summary>From one pile of the group to the next, in card widths and heights.</summary>
    public (float X, float Y) Step { get; init; } = (1, 0);

    public PileFan Fan { get; init; }

    /// <summary>The names of the piles it makes for a game of <paramref name="players"/>.</summary>
    public IReadOnlyList<string> PileNames(int players)
    {
        var count = PerPlayer ? players : Count;
        return count == 1 && !PerPlayer ? [Name] : [.. Enumerable.Range(0, count).Select(i => $"{Name}{i}")];
    }
}

/// <summary>Which cards an effect is for: by rank, suit or tag (each optional; all given must match).</summary>
public sealed record CardSelector
{
    public string? Rank { get; init; }

    public string? Suit { get; init; }

    public string? Tag { get; init; }

    public bool Matches(CardFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return (Rank is null || string.Equals(face.Rank, Rank, StringComparison.OrdinalIgnoreCase))
            && (Suit is null || string.Equals(face.Suit, Suit, StringComparison.OrdinalIgnoreCase))
            && (Tag is null || face.Has(Tag));
    }
}

/// <summary>What playing a card does in a game for several players.</summary>
public sealed record CardEffect(CardSelector Cards)
{
    /// <summary>How many players after this one miss their turn.</summary>
    public int Skip { get; init; }

    /// <summary>Whether play turns round; with two players that is a skip.</summary>
    public bool Reverse { get; init; }

    /// <summary>How many cards the next player draws.</summary>
    public int Draw { get; init; }

    /// <summary>Whether the player names the suit to follow (a wild, Crazy Eights' eight).</summary>
    public bool ChooseSuit { get; init; }

    /// <summary>Whether the same player goes again.</summary>
    public bool PlayAgain { get; init; }

    /// <summary>A function of the ruleset's <see cref="CardRuleset.Script"/> to call as well, <c>fn name(card, seat)</c>, which may act through the turn.</summary>
    public string? Script { get; init; }
}

/// <summary>What a player may do after drawing on their turn.</summary>
public enum AfterDraw
{
    /// <summary>Play the card drawn if it goes, or keep it and pass.</summary>
    MayPlay,

    /// <summary>The turn ends.</summary>
    Pass,

    /// <summary>Keep drawing until a card goes, then play it or pass.</summary>
    UntilPlayable,
}

/// <summary>How turns go in a game for several players.</summary>
public sealed record TurnRules
{
    /// <summary>The pile players draw from.</summary>
    public string Stock { get; init; } = "stock";

    /// <summary>The pile players play onto.</summary>
    public string Discard { get; init; } = "discard";

    /// <summary>The group of per-player piles that are their hands.</summary>
    public string Hand { get; init; } = "hand";

    public int DrawCount { get; init; } = 1;

    public AfterDraw AfterDraw { get; init; } = AfterDraw.MayPlay;

    /// <summary>
    /// What follows a penalty: a draw whose count the script's <c>drawing</c> hook changed (Mau-Mau's sevens). Many
    /// play that the turn ends there; when not set, it is as <see cref="AfterDraw"/>.
    /// </summary>
    public AfterDraw? AfterPenalty { get; init; }

    /// <summary>Whether the discards (but the top) are shuffled back into an empty stock.</summary>
    public bool Reshuffle { get; init; } = true;
}

/// <summary>How a game is won.</summary>
public enum WinKind
{
    /// <summary>Every card on a goal pile (foundations).</summary>
    AllOnGoals,

    /// <summary>The piles named in <see cref="WinRule.Piles"/> all empty (Golf's columns).</summary>
    Empty,

    /// <summary>A player's hand empty: they win.</summary>
    EmptyHand,
}

public sealed record WinRule(WinKind Kind)
{
    public IReadOnlyList<string> Piles { get; init; } = [];
}

/// <summary>
/// Cards that leave in twos (Pyramid's pairs): two free cards from the <see cref="From"/> piles whose ranks add up to
/// <see cref="Sum"/> go together to the <see cref="To"/> pile, dragged one onto the other; a card whose rank is in
/// <see cref="Alone"/> (Pyramid's king) goes by itself, with a tap.
/// </summary>
public sealed record PairRule(int Sum, IReadOnlyList<string> From, string To)
{
    public IReadOnlyList<int> Alone { get; init; } = [];
}

/// <summary>
/// A card game written down: the deck, how many play, the piles with their deal and rules, what cards do, how turns go
/// and how it is won. <see cref="RulesetGame"/> plays it. Read one from JSON with <see cref="Parse"/>; the guide lists
/// every field.
/// </summary>
public sealed record CardRuleset(string Name, CardDeck Deck, IReadOnlyList<PileRule> Piles, WinRule Win)
{
    /// <summary>Cards leaving in twos, or none.</summary>
    public PairRule? Pairs { get; init; }

    public string Description { get; init; } = "";

    public int MinPlayers { get; init; } = 1;

    public int MaxPlayers { get; init; } = 1;

    public int DefaultPlayers { get; init; } = 1;

    /// <summary>Whether it is played alone (a solitaire), rather than in turns by several players.</summary>
    public bool IsSolitaire => MaxPlayers == 1;

    public IReadOnlyList<CardEffect> Effects { get; init; } = [];

    public TurnRules Turns { get; init; } = new();

    /// <summary>
    /// Jentl.Script source for what the fields can't say: functions that piles' <see cref="BuildRule.Check"/>s and
    /// effects' <see cref="CardEffect.Script"/>s name, and hooks called at their moments (<c>played</c>, <c>moved</c>,
    /// <c>drawing</c>, <c>won</c>, <c>score</c>). The guide lists what scripts can read and do.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>Reads a ruleset written as JSON; throws <see cref="InvalidDataException"/> saying where a mistake is.</summary>
    public static CardRuleset Parse(string json) => RulesetReader.Read(json);
}
