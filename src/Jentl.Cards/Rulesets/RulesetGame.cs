using Jentl.Multiplayer;
using Jentl.Script;

namespace Jentl.Cards.Rulesets;

/// <summary>Names the suit to follow after a wild.</summary>
public sealed record ChooseSuit(string Suit) : CardInput;

/// <summary>Keeps the card just drawn and ends the turn.</summary>
public sealed record PassTurn : CardInput
{
    public static PassTurn Instance { get; } = new();
}

/// <summary>Makes the ruleset's call (<see cref="TurnRules.Call"/>) before playing down to its few cards.</summary>
public sealed record CallOut : CardInput
{
    public static CallOut Instance { get; } = new();
}

/// <summary>
/// A game played by a <see cref="CardRuleset"/>: it deals the table, and as the rules of the <see cref="Game"/> it
/// checks and plays every input. Alone (a solitaire), the player drags cards between piles and taps piles, with undo,
/// hints and automatic finishing as any <see cref="CardGame"/> has. With several players it takes turns: the player
/// taps a card in their hand to play it, the stock to draw, or passes; cards do what the ruleset's effects say; and it
/// is an <see cref="ITurnBasedGame{TMove}"/>, so a <see cref="Match{TGame, TMove}"/> plays the bots
/// (<see cref="RulesetBot"/>). Turn state lives in table counters, so undo takes it back with the cards.
/// </summary>
public sealed class RulesetGame : ICardRules, ITurnBasedGame<CardInput>
{
    private const string SeatCounter = "seat";
    private const string ReversedCounter = "reversed";
    private const string SuitCounter = "suit";
    private const string DrawnCounter = "drawn";
    private const string ChoosingCounter = "choosing";
    private const string SkippingCounter = "skipping";
    private const string WinnerCounter = "winner";
    private const string CalledCounter = "called";

    private readonly Dictionary<Pile, (PileRule Rule, int Index)> _piles = [];
    private readonly Dictionary<string, List<Pile>> _groups = new(StringComparer.Ordinal);
    private readonly Random _random;
    private readonly int _lowest;
    private readonly int _highest;
    private readonly RulesetScript? _script;

    /// <param name="seed">Shuffles the deck (and later reshuffles): the same seed, the same deal.</param>
    /// <param name="players">How many play; the ruleset's default when null.</param>
    public RulesetGame(CardRuleset ruleset, int seed, int? players = null)
        : this(ruleset, null, seed, players)
    {
    }

    /// <summary>A game dealt from <paramref name="order"/> as it is, unshuffled: first card first, pile by pile as the ruleset deals.</summary>
    public RulesetGame(CardRuleset ruleset, IReadOnlyList<Card> order, int? players = null)
        : this(ruleset, order ?? throw new ArgumentNullException(nameof(order)), 0, players)
    {
    }

    private RulesetGame(CardRuleset ruleset, IReadOnlyList<Card>? order, int seed, int? players)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        Ruleset = ruleset;
        Players = players ?? ruleset.DefaultPlayers;
        if (Players < ruleset.MinPlayers || Players > ruleset.MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(players), Players, $"{ruleset.Name} is for {ruleset.MinPlayers} to {ruleset.MaxPlayers} players.");
        }

        _random = new Random(seed);
        Suits = [.. ruleset.Deck.Faces.Select(f => f.Suit).OfType<string>().Distinct(StringComparer.Ordinal)];
        var ranked = ruleset.Deck.Faces.Where(f => f.Suit is not null).Select(f => f.Order).DefaultIfEmpty(0).ToList();
        (_lowest, _highest) = (ranked.Min(), ranked.Max());
        Table = Deal(order ?? Deck.Shuffled(ruleset.Deck.Cards, _random));
        Game = new CardGame(Table, this);
        if (ruleset.Script is { } source)
        {
            _script = new RulesetScript(source, ruleset.Name, seed);
            _script.Attach(this);
        }
    }

    public CardRuleset Ruleset { get; }

    public int Players { get; }

    public Table Table { get; }

    /// <summary>The game itself: play inputs through it, undo and redo, and read its history.</summary>
    public CardGame Game { get; }

    /// <summary>The deck's suits, in order: what a wild may name.</summary>
    public IReadOnlyList<string> Suits { get; }

    public int CurrentSeat => Table.Counter(SeatCounter);

    /// <summary>1 round the table, -1 the other way.</summary>
    public int Direction => Table.Counter(ReversedCounter) == 0 ? 1 : -1;

    /// <summary>The suit named for the wild on the discard pile, or null.</summary>
    public string? ChosenSuit => Table.Counter(SuitCounter) is > 0 and var suit ? Suits[suit - 1] : null;

    /// <summary>Whether the current player must name a suit before play goes on.</summary>
    public bool IsChoosingSuit => Table.Counter(ChoosingCounter) != 0;

    /// <summary>Whether the current player has drawn and may now only play that card or pass.</summary>
    public bool HasDrawn => Table.Counter(DrawnCounter) != 0;

    /// <summary>Whether the player whose turn it is has made the ruleset's call this turn.</summary>
    public bool HasCalled => Table.Counter(CalledCounter) != 0;

    /// <summary>Whether <paramref name="seat"/> may make the ruleset's call now: it is their turn and a card would take them down to the count.</summary>
    public bool CanCall(int seat) => Ruleset.Turns.Call is { } call && !Ruleset.IsSolitaire && !IsOver && seat == CurrentSeat
        && !HasCalled && !IsChoosingSuit && Hand(seat).Count is var count && count > call.Cards && count <= call.Cards + 1;

    /// <summary>Who won a game for several players, or null.</summary>
    public int? Winner => Table.Counter(WinnerCounter) is > 0 and var winner ? winner - 1 : null;

    public bool IsOver => Ruleset.IsSolitaire ? Game.IsWon : Winner is not null;

    public Pile Stock => Table[Ruleset.Turns.Stock];

    public Pile Discard => Table[Ruleset.Turns.Discard];

    /// <summary>A player's hand.</summary>
    public Pile Hand(int seat) => Table[$"{Ruleset.Turns.Hand}{seat}"];

    /// <summary>The rule a pile follows.</summary>
    public PileRule RuleOf(Pile pile) => _piles[pile].Rule;

    /// <summary>The piles of a group (or the one pile of that name).</summary>
    public IReadOnlyList<Pile> Piles(string name) =>
        _groups.TryGetValue(name, out var group) ? group : [Table[name]];

    /// <summary>What a player scores for winning: the points of the cards left in the other hands.</summary>
    public int Score(int seat) => _script?.Hook("score", seat) is { } score
        ? (int)score.AsNumber
        : Enumerable.Range(0, Players).Where(s => s != seat).Sum(s => Hand(s).Cards.Sum(c => c.Card.Face.Points));

    /// <summary>A pile's group and its index in it.</summary>
    public (PileRule Rule, int Index) PlaceOf(Pile pile) => _piles[pile];

    /// <summary>Plays an input; null if the rules refuse it.</summary>
    public CardTurn? Play(CardInput input) => Game.Play(input);

    bool ICardRules.Play(CardTurnBuilder turn, CardInput input)
    {
        _script?.Begin(turn);
        try
        {
            return Ruleset.IsSolitaire ? PlaySolitaire(turn, input) : PlayTurn(turn, input);
        }
        finally
        {
            _script?.End();
        }
    }

    /// <summary>Whether the game is won: as the ruleset's win rule says, or a solitaire's script's <c>won()</c> when it answers.</summary>
    public bool IsWon(Table table) => Ruleset.IsSolitaire && _script?.Hook("won") is { } won ? won.IsTruthy : Ruleset.Win.Kind switch
    {
        WinKind.AllOnGoals => _piles.Where(p => p.Value.Rule.Goal).Sum(p => p.Key.Count) == Ruleset.Deck.Cards.Count,
        WinKind.Empty => Ruleset.Win.Piles.SelectMany(Piles).All(p => p.IsEmpty),
        _ => Winner is not null,
    };

    /// <summary>Whether the card at <paramref name="index"/> of <paramref name="pile"/>, with any above it, may be picked up.</summary>
    public bool CanTake(Pile pile, int index)
    {
        ArgumentNullException.ThrowIfNull(pile);
        if (index < 0 || index >= pile.Count || !pile[index].FaceUp)
        {
            return false;
        }

        if (IsCovered(pile))
        {
            return false;
        }

        var take = RuleOf(pile).Take;
        switch (take.Kind)
        {
            case TakeKind.Top:
                return index == pile.Count - 1;
            case TakeKind.Any or TakeKind.One:
                return true;
            case TakeKind.Run:
                for (var i = index + 1; i < pile.Count; i++)
                {
                    var (below, above) = (pile[i - 1].Card.Face, pile[i].Card.Face);
                    if (!pile[i].FaceUp || !Steps(take.RunRank, wrap: false, below, above) || !SuitsMatch(take.RunSuit, below, above))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether another pile of its group lies over <paramref name="pile"/> and still has cards (see <see cref="PileRule.CoveredBy"/>).</summary>
    public bool IsCovered(Pile pile)
    {
        ArgumentNullException.ThrowIfNull(pile);
        var (rule, index) = _piles[pile];
        if (rule.CoveredBy is not { } covers)
        {
            return false;
        }

        var group = Piles(rule.Name);
        return covers[index].Any(i => i < group.Count && !group[i].IsEmpty);
    }

    /// <summary>Turns face up the top card of every covered pile that nothing lies over any more.</summary>
    private void Uncover(CardTurnBuilder turn)
    {
        foreach (var (pile, (rule, _)) in _piles)
        {
            if (rule.CoveredBy is not null && pile.Top is { FaceUp: false } && !IsCovered(pile))
            {
                turn.Flip(pile, pile.Count - 1, faceUp: true);
                turn.Tag("reveal");
            }
        }
    }

    /// <summary>Whether <paramref name="card"/>, at the bottom of <paramref name="count"/> cards from <paramref name="from"/>, may go on <paramref name="to"/>.</summary>
    public bool Fits(Card card, Pile to, int count = 1, Pile? from = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        return FitsByRules(card, to, count, from)
            && (RuleOf(to).Build.Check is not { } check
                || _script!.Call(check, RulesetScript.CardValue(card), _script.PileValue(to), from is null ? ScriptValue.Nil : _script.PileValue(from)).IsTruthy);
    }

    private bool FitsByRules(Card card, Pile to, int count, Pile? from)
    {
        var build = RuleOf(to).Build;
        if (build.Closed || (build.Max > 0 && to.Count + count > build.Max) || (!build.Runs && count > 1)
            || (from is not null && build.From.Count > 0 && !build.From.Any(f => Piles(f).Contains(from))))
        {
            return false;
        }

        var face = card.Face;
        if (build.MatchSuitOrRank)
        {
            if (build.IsWild(face) || to.Top is not { } discard)
            {
                return true;
            }

            var top = discard.Card.Face;
            var suit = ChosenSuit ?? top.Suit;
            return (build.IsWild(top) && suit is null) || string.Equals(face.Suit, suit, StringComparison.Ordinal) || string.Equals(face.Rank, top.Rank, StringComparison.Ordinal);
        }

        if (to.Top is not { } under)
        {
            return build.Empty is null || (build.Empty != "none" && face.Rank == build.Empty);
        }

        return under.FaceUp && Steps(build.Rank, build.Wrap, under.Card.Face, face) && SuitsMatch(build.Suit, under.Card.Face, face);
    }

    /// <summary>What may be done now: for a solitaire every useful move (best first, as <see cref="Hints"/>), for several players the current one's.</summary>
    public IReadOnlyList<CardInput> LegalMoves(int seat) =>
        Ruleset.IsSolitaire ? [.. Hints(Table)] : seat == CurrentSeat && !IsOver ? TurnMoves(seat) : [];

    /// <summary>Plays <paramref name="move"/> for <paramref name="seat"/>; throws if it isn't their turn or the rules refuse it.</summary>
    public void Apply(int seat, CardInput move)
    {
        if (!Ruleset.IsSolitaire && seat != CurrentSeat)
        {
            throw new InvalidOperationException($"It is seat {CurrentSeat}'s turn, not {seat}'s.");
        }

        if (Play(move) is null)
        {
            throw new InvalidOperationException($"{move} isn't allowed now.");
        }
    }

    /// <summary>
    /// Useful moves, best first: cards to goal piles, moves that turn up a face-down card, the rest, then the stock.
    /// Moves that just shift a whole pile to an empty one, or take cards back off a goal pile, aren't suggested.
    /// </summary>
    public IEnumerable<CardInput> Hints(Table table)
    {
        if (!Ruleset.IsSolitaire)
        {
            return IsOver ? [] : TurnMoves(CurrentSeat);
        }

        if (IsWon(Table))
        {
            return [];
        }

        var moves = new List<(int Score, CardInput Move)>();
        foreach (var (from, (rule, _)) in _piles)
        {
            if (rule.Goal)
            {
                continue;
            }

            for (var index = 0; index < from.Count; index++)
            {
                if (!CanTake(from, index))
                {
                    continue;
                }

                var count = from.Count - index;
                foreach (var to in _piles.Keys)
                {
                    if (to == from || !CanMove(from, index, to))
                    {
                        continue;
                    }

                    var reveals = index > 0 && !from[index - 1].FaceUp;
                    if (to.IsEmpty && index == 0 && !RuleOf(to).Goal && RuleOf(to) == rule)
                    {
                        continue;
                    }

                    var score = (RuleOf(to).Goal ? 100 : 0) + (reveals ? 50 : 0) + (index == 0 ? 10 : 0) + (to.IsEmpty ? -5 : 0) + count;
                    moves.Add((score, new DragCards(from, index, to)));
                }
            }
        }

        // Pairs, and cards that leave alone: before any other move, as they are what clears the table.
        if (Ruleset.Pairs is { } pairing)
        {
            var free = _piles.Keys.Where(p => Free(p, pairing)).ToList();
            foreach (var pile in free.Where(p => pairing.Alone.Contains(p.Top!.Value.Card.Face.Order)))
            {
                moves.Add((300, new ClickPile(pile, pile.Count - 1)));
            }

            for (var a = 0; a < free.Count; a++)
            {
                for (var b = a + 1; b < free.Count; b++)
                {
                    if (CanPair(free[a], free[a].Count - 1, free[b]))
                    {
                        // A pair from the pyramid itself first: the waste can wait.
                        var score = 200 + (RuleOf(free[a]).Name == RuleOf(free[b]).Name ? 20 : 0);
                        moves.Add((score, new DragCards(free[a], free[a].Count - 1, free[b])));
                    }
                }
            }
        }

        var hints = moves.OrderByDescending(m => m.Score).Select(m => m.Move).ToList();
        foreach (var (pile, (rule, _)) in _piles)
        {
            if (rule.Click is { } click && (!pile.IsEmpty || (click.RedealFrom is { } from && !Table[from].IsEmpty && RedealsLeft(pile, click))))
            {
                hints.Add(new ClickPile(pile, pile.Count - 1));
            }
        }

        return hints;
    }

    /// <summary>
    /// Once nothing is hidden any more (every dealing pile empty, no face-down card left), puts cards on the goal piles
    /// one move at a time, so a won solitaire finishes by itself.
    /// </summary>
    public CardInput? AutoMove(Table table)
    {
        if (!Ruleset.IsSolitaire || IsWon(Table) || _piles.Any(p => p.Value.Rule.Click is not null && !p.Key.IsEmpty)
            || _piles.Keys.Any(p => p.Cards.Any(c => !c.FaceUp)) || !_piles.Any(p => p.Value.Rule.Goal))
        {
            return null;
        }

        foreach (var from in _piles.Keys.Where(p => !RuleOf(p).Goal && !p.IsEmpty))
        {
            var index = from.Count - 1;
            if (CanTake(from, index) && _piles.Keys.FirstOrDefault(to => RuleOf(to).Goal && CanMove(from, index, to)) is { } goal)
            {
                return new DragCards(from, index, goal);
            }
        }

        return null;
    }

    private Table Deal(IReadOnlyList<Card> order)
    {
        var table = new Table();
        foreach (var rule in Ruleset.Piles)
        {
            var group = new List<Pile>();
            var names = rule.PileNames(Players);
            for (var i = 0; i < names.Count; i++)
            {
                var pile = table.Add(names[i]);
                _piles[pile] = (rule, i);
                group.Add(pile);
            }

            _groups[rule.Name] = group;
        }

        var deck = new LinkedList<Card>(order);
        foreach (var rule in Ruleset.Piles.Where(r => !r.Deal.Rest))
        {
            var group = _groups[rule.Name];
            var counts = group.Select((_, i) => rule.Deal.CountFor(i)).ToArray();
            var dealt = new int[group.Count];

            // Round the group one card at a time, as dealers do, so hands get every n-th card.
            for (var round = 0; round < counts.DefaultIfEmpty(0).Max(); round++)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    if (round < counts[i])
                    {
                        var card = Take(deck, rule.Deal);
                        // A covered pile's card lies face down until what is over it has gone, unless all are open.
                        var faceUp = dealt[i]++ >= rule.Deal.FaceDownFor(i, counts[i]);
                        group[i].Deal(card, faceUp: faceUp && (rule.Deal.Open || rule.CoveredBy is not { } covers || covers[i].Count == 0));
                    }
                }
            }
        }

        foreach (var rule in Ruleset.Piles.Where(r => r.Deal.Rest))
        {
            var pile = _groups[rule.Name][0];
            var count = deck.Count;
            for (var i = 0; i < count; i++)
            {
                pile.Deal(Take(deck, null), faceUp: i >= rule.Deal.FaceDownFor(0, count));
            }
        }

        return table;
    }

    private Card Take(LinkedList<Card> deck, DealRule? rule)
    {
        for (var tries = deck.Count; tries > 0; tries--)
        {
            var card = deck.First?.Value ?? throw new InvalidOperationException($"{Ruleset.Name} deals more cards than the deck has.");
            deck.RemoveFirst();
            if (rule is null || !rule.Keeps(card.Face))
            {
                return card;
            }

            deck.AddLast(card);
        }

        throw deck.Count == 0
            ? new InvalidOperationException($"{Ruleset.Name} deals more cards than the deck has.")
            : new InvalidOperationException($"Every card left is a '{rule?.Not}'.");
    }

    private bool Steps(RankStep step, bool wrap, CardFace below, CardFace above)
    {
        bool Up(CardFace low, CardFace high) => high.Order == low.Order + 1 || (wrap && low.Order == _highest && high.Order == _lowest);
        return step switch
        {
            RankStep.Up => Up(below, above),
            RankStep.Down => Up(above, below),
            RankStep.UpOrDown => Up(below, above) || Up(above, below),
            RankStep.Same => below.Order == above.Order,
            _ => true,
        };
    }

    private static bool SuitsMatch(SuitMatch match, CardFace below, CardFace above) => match switch
    {
        SuitMatch.Same => below.Suit is not null && below.Suit == above.Suit,
        SuitMatch.Alternate => below.Colour is not null && above.Colour is not null && below.Colour != above.Colour,
        SuitMatch.SameColour => below.Colour is not null && below.Colour == above.Colour,
        SuitMatch.Different => below.Suit != above.Suit,
        _ => true,
    };

    private bool CanMove(Pile from, int index, Pile to)
    {
        var count = from.Count - index;
        return CanTake(from, index) && Fits(from[index].Card, to, count, from) && count <= RunLimit(from, to);
    }

    private int RunLimit(Pile from, Pile to)
    {
        if (RuleOf(from).Take.Limit != Rulesets.RunLimit.FreeCells)
        {
            return int.MaxValue;
        }

        var cells = _piles.Count(p => p.Value.Rule.Build.Max == 1 && !p.Value.Rule.Goal && p.Key.IsEmpty);
        var columns = Piles(RuleOf(from).Name).Count(p => p.IsEmpty && p != to);
        return (cells + 1) << columns;
    }

    private bool RedealsLeft(Pile pile, ClickRule click) => click.Redeals < 0 || Table.Counter($"redeals:{pile.Name}") < click.Redeals;

    private bool PlaySolitaire(CardTurnBuilder turn, CardInput input) => input switch
    {
        DragCards drag => drag.From != drag.To && (CanPair(drag.From, drag.Index, drag.To)
            ? Pair(turn, drag.From, drag.To)
            : CanMove(drag.From, drag.Index, drag.To) && Move(turn, drag.From, drag.Index, drag.To)),
        ClickPile click => Tap(turn, click.Pile, click.Index),
        _ => false,
    };

    /// <summary>Whether a free card dropped on another free one makes a pair that leaves (see <see cref="PairRule"/>).</summary>
    public bool CanPair(Pile from, int index, Pile to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return Ruleset.Pairs is { } pairs && from != to && index == from.Count - 1 && Free(from, pairs) && Free(to, pairs)
            && from[index].Card.Face.Order + to.Top!.Value.Card.Face.Order == pairs.Sum;
    }

    // The top card of a pile pairs may come from, face up and not covered.
    private bool Free(Pile pile, PairRule pairs) =>
        !pile.IsEmpty && pairs.From.Contains(RuleOf(pile).Name) && pile.Top!.Value.FaceUp && CanTake(pile, pile.Count - 1);

    private bool Pair(CardTurnBuilder turn, Pile from, Pile to)
    {
        var goal = Piles(Ruleset.Pairs!.To)[0];
        turn.Move(from, from.Count - 1, goal);
        turn.Move(to, to.Count - 1, goal);
        turn.Tag("pair");
        turn.Tag("goal");
        Uncover(turn);
        return true;
    }

    private bool Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
        var moving = RuleOf(from).Take.Kind == TakeKind.One ? 1 : from.Count - index;
        if (RuleOf(from).Take.Kind == TakeKind.One)
        {
            turn.MoveOne(from, index, to);
        }
        else
        {
            turn.Move(from, index, to);
        }

        turn.Tag(RuleOf(to).Goal ? "goal" : "move");
        if (RuleOf(from).FlipTop && from.Top is { FaceUp: false })
        {
            turn.Flip(from, from.Count - 1, faceUp: true);
            turn.Tag("reveal");
        }

        Complete(turn, to);
        Uncover(turn);
        if (_script is { } script && script.Has("moved"))
        {
            script.Call("moved", script.PileValue(from), script.PileValue(to), moving);
        }

        return true;
    }

    // Spider: a whole run on top of a pile leaves for a goal pile.
    private void Complete(CardTurnBuilder turn, Pile pile)
    {
        if (RuleOf(pile).Complete is not { } complete || pile.Count < complete.Length)
        {
            return;
        }

        var start = pile.Count - complete.Length;
        for (var i = start + 1; i < pile.Count; i++)
        {
            var (below, above) = (pile[i - 1], pile[i]);
            if (!below.FaceUp || !above.FaceUp || !Steps(complete.Rank, wrap: false, below.Card.Face, above.Card.Face) || !SuitsMatch(complete.Suit, below.Card.Face, above.Card.Face))
            {
                return;
            }
        }

        if (Piles(complete.To).FirstOrDefault(p => p.IsEmpty) is { } goal)
        {
            turn.Move(pile, start, goal);
            turn.Tag("complete");
            turn.Tag("goal");
            if (RuleOf(pile).FlipTop && pile.Top is { FaceUp: false })
            {
                turn.Flip(pile, pile.Count - 1, faceUp: true);
            }
        }
    }

    private bool Tap(CardTurnBuilder turn, Pile pile, int index)
    {
        var rule = RuleOf(pile);
        if (rule.Click is { } click)
        {
            return DealFrom(turn, pile, click);
        }

        if (index >= 0 && index == pile.Count - 1 && !pile[index].FaceUp && rule.FlipTop && !IsCovered(pile))
        {
            turn.Flip(pile, index, faceUp: true);
            turn.Tag("reveal");
            return true;
        }

        if (index < 0 || !CanTake(pile, index))
        {
            return false;
        }

        // A card that leaves by itself (Pyramid's king).
        if (Ruleset.Pairs is { } pairs && index == pile.Count - 1 && Free(pile, pairs) && pairs.Alone.Contains(pile[index].Card.Face.Order))
        {
            turn.Move(pile, index, Piles(pairs.To)[0]);
            turn.Tag("goal");
            Uncover(turn);
            return true;
        }

        // A tap sends the card where it does most: a goal pile, else a pile it builds on, else an empty one.
        var target = _piles.Keys
            .Where(to => to != pile && CanMove(pile, index, to) && !(to.IsEmpty && index == 0 && RuleOf(to) == rule))
            .OrderByDescending(to => RuleOf(to).Goal)
            .ThenBy(to => to.IsEmpty)
            .FirstOrDefault();
        return target is not null && Move(turn, pile, index, target);
    }

    private bool DealFrom(CardTurnBuilder turn, Pile pile, ClickRule click)
    {
        if (pile.IsEmpty)
        {
            if (click.RedealFrom is not { } name || Table[name] is not { IsEmpty: false } from || !RedealsLeft(pile, click))
            {
                return false;
            }

            while (!from.IsEmpty)
            {
                turn.MoveTop(from, pile);
                turn.Flip(pile, pile.Count - 1, faceUp: false);
            }

            turn.Count($"redeals:{pile.Name}");
            turn.Tag("redeal");
            return true;
        }

        var targets = Piles(click.To);
        if (click.Each)
        {
            if (click.RequireFilled && targets.Any(t => t.IsEmpty))
            {
                return false;
            }

            for (var round = 0; round < click.Deal; round++)
            {
                foreach (var target in targets)
                {
                    if (!pile.IsEmpty)
                    {
                        DealOne(turn, pile, target, click.FaceUp);
                    }
                }
            }

            foreach (var target in targets)
            {
                Complete(turn, target);
            }
        }
        else
        {
            for (var i = 0; i < click.Deal && !pile.IsEmpty; i++)
            {
                DealOne(turn, pile, targets[0], click.FaceUp);
            }
        }

        turn.Tag("deal");
        return true;
    }

    private static void DealOne(CardTurnBuilder turn, Pile from, Pile to, bool faceUp)
    {
        turn.MoveTop(from, to);
        turn.Flip(to, to.Count - 1, faceUp);
    }

    private List<CardInput> TurnMoves(int seat)
    {
        if (IsChoosingSuit)
        {
            return [.. Suits.Select(s => new ChooseSuit(s))];
        }

        var hand = Hand(seat);
        var moves = new List<CardInput>();
        var from = HasDrawn ? hand.Count - 1 : 0;
        for (var i = Math.Max(0, from); i < hand.Count; i++)
        {
            if (Fits(hand[i].Card, Discard, 1, hand))
            {
                moves.Add(new ClickPile(hand, i));
            }
        }

        if (CanCall(seat))
        {
            moves.Add(CallOut.Instance);
        }

        if (HasDrawn || !CanDraw)
        {
            moves.Add(PassTurn.Instance);
        }
        else
        {
            moves.Add(new ClickPile(Stock, Stock.Count - 1));
        }

        return moves;
    }

    /// <summary>Whether there is a card to draw, from the stock or by shuffling the discards back.</summary>
    public bool CanDraw => !Stock.IsEmpty || (Ruleset.Turns.Reshuffle && Discard.Count > 1);

    private bool PlayTurn(CardTurnBuilder turn, CardInput input)
    {
        if (IsOver)
        {
            return false;
        }

        var seat = CurrentSeat;
        var hand = Hand(seat);
        if (IsChoosingSuit)
        {
            var index = input is ChooseSuit choose ? Suits.ToList().IndexOf(choose.Suit) : -1;
            if (index < 0)
            {
                return false;
            }

            turn.Count(SuitCounter, index + 1 - Table.Counter(SuitCounter));
            turn.Count(ChoosingCounter, -Table.Counter(ChoosingCounter));
            turn.Tag($"chose:{Suits[index]}");
            var skip = Table.Counter(SkippingCounter);
            turn.Count(SkippingCounter, -skip);
            EndTurn(turn, seat, skip, playAgain: false);
            return true;
        }

        switch (input)
        {
            case ClickPile click when click.Pile == hand:
                return PlayCard(turn, seat, click.Index);
            case DragCards drag when drag.From == hand && drag.To == Discard:
                return PlayCard(turn, seat, drag.Index);
            case ClickPile click when click.Pile == Stock && !HasDrawn:
                return Draw(turn, seat);
            case DragCards drag when drag.From == Stock && drag.To == hand && !HasDrawn:
                return Draw(turn, seat);
            case CallOut when CanCall(seat):
                turn.Count(CalledCounter, 1);
                turn.Tag("call");
                return true;
            case PassTurn when HasDrawn || !CanDraw:
                turn.Tag("pass");
                EndTurn(turn, seat, 0, playAgain: false);
                return true;
            default:
                return false;
        }
    }

    private bool PlayCard(CardTurnBuilder turn, int seat, int index)
    {
        var hand = Hand(seat);
        if (index < 0 || index >= hand.Count || (HasDrawn && index != hand.Count - 1) || !Fits(hand[index].Card, Discard, 1, hand))
        {
            return false;
        }

        var face = hand[index].Card.Face;
        turn.MoveOne(hand, index, Discard);
        turn.Flip(Discard, Discard.Count - 1, faceUp: true);
        turn.Count(SuitCounter, -Table.Counter(SuitCounter));
        turn.Count(DrawnCounter, -Table.Counter(DrawnCounter));
        turn.Tag("play");
        if (hand.IsEmpty && Ruleset.Win.Kind == WinKind.EmptyHand)
        {
            turn.Count(WinnerCounter, seat + 1);
            turn.Tag("win");
            return true;
        }

        // Down to the call's count: called, or the penalty drawn for forgetting.
        if (Ruleset.Turns.Call is { } call && hand.Count == call.Cards)
        {
            if (HasCalled)
            {
                turn.Tag("called");
            }
            else
            {
                turn.Tag($"forgot:{DrawCards(turn, seat, call.Penalty)}");
            }
        }

        turn.Count(CalledCounter, -Table.Counter(CalledCounter));

        var effects = Ruleset.Effects.Where(e => e.Cards.Matches(face)).ToList();
        var skip = effects.Sum(e => e.Skip);
        var reverses = effects.Count(e => e.Reverse);
        var choose = effects.Any(e => e.ChooseSuit);
        var again = effects.Any(e => e.PlayAgain);
        if (_script is { } script)
        {
            // Effects that call the script, then its played(card, seat); what they ask for joins the rest.
            var card = RulesetScript.CardValue(Discard.Top!.Value.Card);
            foreach (var effect in effects.Where(e => e.Script is not null))
            {
                script.Call(effect.Script!, card, seat);
            }

            if (script.Has("played"))
            {
                script.Call("played", card, seat);
            }

            (skip, reverses, choose, again) = (skip + script.Skip, reverses + script.Reverses, choose || script.Choose, again || script.Again);
        }

        for (var i = 0; i < reverses; i++)
        {
            if (Players == 2)
            {
                skip++;
            }
            else
            {
                turn.Count(ReversedCounter, Table.Counter(ReversedCounter) == 0 ? 1 : -1);
            }

            turn.Tag("reverse");
        }

        var draw = effects.Sum(e => e.Draw);
        if (draw > 0)
        {
            DrawCards(turn, Next(seat, 1), draw);
            turn.Tag($"draw:{draw}");
        }

        if (skip > 0)
        {
            turn.Tag($"skip:{skip}");
        }

        if (choose)
        {
            turn.Count(ChoosingCounter, 1);
            turn.Count(SkippingCounter, skip);
            return true;
        }

        EndTurn(turn, seat, skip, again);
        return true;
    }

    private bool Draw(CardTurnBuilder turn, int seat)
    {
        var hand = Hand(seat);
        var count = _script?.Hook("drawing", seat) is { } asked ? (int)asked.AsNumber : Ruleset.Turns.DrawCount;
        var after = count != Ruleset.Turns.DrawCount ? Ruleset.Turns.AfterPenalty ?? Ruleset.Turns.AfterDraw : Ruleset.Turns.AfterDraw;
        var drawn = DrawCards(turn, seat, count);
        turn.Tag("drew");
        if (after == AfterDraw.UntilPlayable)
        {
            while (drawn > 0 && !Fits(hand[^1].Card, Discard, 1, hand))
            {
                drawn = DrawCards(turn, seat, 1);
            }
        }

        if (drawn > 0 && after != AfterDraw.Pass && Fits(hand[^1].Card, Discard, 1, hand))
        {
            turn.Count(DrawnCounter, 1);
            return true;
        }

        EndTurn(turn, seat, 0, playAgain: false);
        return true;
    }

    /// <summary>For scripts: <paramref name="seat"/> draws <paramref name="count"/> cards; how many came.</summary>
    internal int DrawFor(CardTurnBuilder turn, int seat, int count)
    {
        if (seat < 0 || seat >= Players || Ruleset.IsSolitaire)
        {
            throw new ScriptException($"draw: there is no seat {seat} to draw for");
        }

        var drawn = DrawCards(turn, seat, count);
        turn.Tag($"draw:{drawn}");
        return drawn;
    }

    private int DrawCards(CardTurnBuilder turn, int seat, int count)
    {
        var hand = Hand(seat);
        for (var i = 0; i < count; i++)
        {
            if (Stock.IsEmpty && Ruleset.Turns.Reshuffle && Discard.Count > 1)
            {
                // Everything under the top card goes back, shuffled, face down.
                while (Discard.Count > 1)
                {
                    turn.MoveOne(Discard, _random.Next(Discard.Count - 1), Stock);
                    turn.Flip(Stock, Stock.Count - 1, faceUp: false);
                }

                turn.Tag("reshuffle");
            }

            if (Stock.IsEmpty)
            {
                return i;
            }

            turn.MoveTop(Stock, hand);
            turn.Flip(hand, hand.Count - 1, faceUp: true);
        }

        return count;
    }

    private int Next(int seat, int steps) => (((seat + (steps * Direction)) % Players) + Players) % Players;

    private void EndTurn(CardTurnBuilder turn, int seat, int skip, bool playAgain)
    {
        turn.Count(DrawnCounter, -Table.Counter(DrawnCounter));
        turn.Count(CalledCounter, -Table.Counter(CalledCounter));
        if (!playAgain)
        {
            turn.Count(SeatCounter, Next(seat, 1 + skip) - seat);
        }
    }
}
