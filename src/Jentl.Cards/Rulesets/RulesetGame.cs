using Jentl.Multiplayer;

namespace Jentl.Cards.Rulesets;

/// <summary>Names the suit to follow after a wild.</summary>
public sealed record ChooseSuit(string Suit) : CardInput;

/// <summary>Keeps the card just drawn and ends the turn.</summary>
public sealed record PassTurn : CardInput
{
    public static PassTurn Instance { get; } = new();
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

    private readonly Dictionary<Pile, (PileRule Rule, int Index)> _piles = [];
    private readonly Dictionary<string, List<Pile>> _groups = new(StringComparer.Ordinal);
    private readonly Random _random;
    private readonly int _lowest;
    private readonly int _highest;

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
    public int Score(int seat) => Enumerable.Range(0, Players).Where(s => s != seat).Sum(s => Hand(s).Cards.Sum(c => c.Card.Face.Points));

    /// <summary>Plays an input; null if the rules refuse it.</summary>
    public CardTurn? Play(CardInput input) => Game.Play(input);

    bool ICardRules.Play(CardTurnBuilder turn, CardInput input) => Ruleset.IsSolitaire ? PlaySolitaire(turn, input) : PlayTurn(turn, input);

    public bool IsWon(Table table) => Ruleset.Win.Kind switch
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

    /// <summary>Whether <paramref name="card"/>, at the bottom of <paramref name="count"/> cards from <paramref name="from"/>, may go on <paramref name="to"/>.</summary>
    public bool Fits(Card card, Pile to, int count = 1, Pile? from = null)
    {
        ArgumentNullException.ThrowIfNull(to);
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
                        group[i].Deal(card, faceUp: dealt[i]++ >= rule.Deal.FaceDownFor(i, counts[i]));
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
        DragCards drag => drag.From != drag.To && CanMove(drag.From, drag.Index, drag.To) && Move(turn, drag.From, drag.Index, drag.To),
        ClickPile click => Tap(turn, click.Pile, click.Index),
        _ => false,
    };

    private bool Move(CardTurnBuilder turn, Pile from, int index, Pile to)
    {
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

        if (index >= 0 && index == pile.Count - 1 && !pile[index].FaceUp && rule.FlipTop)
        {
            turn.Flip(pile, index, faceUp: true);
            turn.Tag("reveal");
            return true;
        }

        if (index < 0 || !CanTake(pile, index))
        {
            return false;
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

        var effects = Ruleset.Effects.Where(e => e.Cards.Matches(face)).ToList();
        var skip = effects.Sum(e => e.Skip);
        foreach (var effect in effects.Where(e => e.Reverse))
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

        if (effects.Any(e => e.ChooseSuit))
        {
            turn.Count(ChoosingCounter, 1);
            turn.Count(SkippingCounter, skip);
            return true;
        }

        EndTurn(turn, seat, skip, effects.Any(e => e.PlayAgain));
        return true;
    }

    private bool Draw(CardTurnBuilder turn, int seat)
    {
        var hand = Hand(seat);
        var drawn = DrawCards(turn, seat, Ruleset.Turns.DrawCount);
        turn.Tag("drew");
        if (Ruleset.Turns.AfterDraw == AfterDraw.UntilPlayable)
        {
            while (drawn > 0 && !Fits(hand[^1].Card, Discard, 1, hand))
            {
                drawn = DrawCards(turn, seat, 1);
            }
        }

        if (drawn > 0 && Ruleset.Turns.AfterDraw != AfterDraw.Pass && Fits(hand[^1].Card, Discard, 1, hand))
        {
            turn.Count(DrawnCounter, 1);
            return true;
        }

        EndTurn(turn, seat, 0, playAgain: false);
        return true;
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
        if (!playAgain)
        {
            turn.Count(SeatCounter, Next(seat, 1 + skip) - seat);
        }
    }
}
