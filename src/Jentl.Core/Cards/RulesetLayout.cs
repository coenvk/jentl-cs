using Jentl.Cards;
using Jentl.Cards.Rulesets;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Cards;

/// <summary>
/// Lays out a <see cref="RulesetGame"/> on a <see cref="TableView"/>, so a ruleset needs no screen code of its own: a
/// solitaire where its piles say (<see cref="PileRule.At"/>, <see cref="PileRule.Step"/>, <see cref="PileRule.Fan"/>),
/// and a game for several players round a table. Call it each frame before <see cref="TableView.Update"/>.
/// </summary>
public static class RulesetLayout
{
    // A pile's place is in card sizes, each with a tenth's gap.
    private const float Spacing = 1.1f;

    /// <summary>
    /// Places a solitaire's piles in <paramref name="area"/> at the largest card size that fits them (with room for
    /// fanned columns), centred; labels empty piles with the rank they take, and lets only takeable cards be dragged.
    /// </summary>
    public static void Solitaire(TableView view, RulesetGame game, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(game);
        var spots = Spots(game).ToList();
        var columns = spots.Max(s => s.X) + 1;
        var rows = spots.Max(s => s.Y) + 1;
        var fanned = spots.Any(s => s.Rule.Fan == PileFan.Down) ? 2.6f : 0;

        var width = area.Width / (((columns - 1) * Spacing) + 1);
        var height = width / CardArt.AspectRatio;
        var tall = ((rows - 1) * Spacing) + 1 + fanned;
        if (height * tall > area.Height)
        {
            height = area.Height / tall;
            width = height * CardArt.AspectRatio;
        }

        view.CardSize = new Point((int)width, (int)height);
        var left = area.X + ((area.Width - ((((columns - 1) * Spacing) + 1) * width)) / 2);
        view.Clear();
        foreach (var (pile, rule, x, y) in spots)
        {
            var at = new Point((int)(left + (x * Spacing * width)), (int)(area.Y + (y * Spacing * height)));
            view.Place(pile, at, Fan(rule.Fan), rule.Fan switch
            {
                PileFan.Down => area.Bottom - at.Y,
                PileFan.Right => area.Right - at.X,
                _ => 0,
            });
        }

        view.CanDrag = game.CanTake;
        view.EmptyLabel = pile => EmptyLabel(game, pile);
        view.Hidden = (_, _) => false;
    }

    /// <summary>
    /// Places a game for several players: <paramref name="viewer"/>'s hand fanned along the bottom, the others' along
    /// the top in turn order (drawn face down), the stock and discard in the middle. Returns where each other player's
    /// hand is, to write their name and card count beside it. Cards are played with a tap, so none are dragged.
    /// </summary>
    public static IReadOnlyDictionary<int, Rectangle> Table(TableView view, RulesetGame game, Rectangle area, int viewer)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(game);
        var height = Math.Min(area.Height / 4.2f, area.Width / 6f / CardArt.AspectRatio);
        var card = new Point((int)(height * CardArt.AspectRatio), (int)height);
        var pad = card.X / 4;
        view.CardSize = card;
        view.Clear();

        var others = Enumerable.Range(1, game.Players - 1).Select(i => (viewer + i) % game.Players).ToList();
        var seats = new Dictionary<int, Rectangle>();
        var slot = (area.Width - pad) / Math.Max(1, others.Count);
        for (var i = 0; i < others.Count; i++)
        {
            var at = new Point(area.X + pad + (i * slot), area.Y + pad);
            view.Place(game.Hand(others[i]), at, Jentl.Core.Cards.Fan.Right, slot - pad, spread: 0.12f);
            seats[others[i]] = new Rectangle(at.X, at.Y, slot - pad, card.Y);
        }

        var middle = area.Center.Y - (card.Y / 2);
        view.Place(game.Stock, new Point(area.Center.X - card.X - pad, middle));
        view.Place(game.Discard, new Point(area.Center.X + pad, middle));
        // The viewer's hand spread out to read, centred, squeezed when it grows long.
        const float Spread = 0.6f;
        var hand = game.Hand(viewer);
        var room = area.Width - (2 * pad);
        var length = (int)Math.Min(room, card.X * (1 + (Spread * Math.Max(0, hand.Count - 1))));
        view.Place(hand, new Point(area.Center.X - (length / 2), area.Bottom - card.Y - pad), Jentl.Core.Cards.Fan.Right, room, Spread);

        var hidden = others.Select(game.Hand).ToHashSet();
        view.Hidden = (pile, _) => hidden.Contains(pile);
        view.CanDrag = (_, _) => false;
        view.EmptyLabel = _ => null;
        return seats;
    }

    private static IEnumerable<(Pile Pile, PileRule Rule, float X, float Y)> Spots(RulesetGame game)
    {
        foreach (var rule in game.Ruleset.Piles)
        {
            var piles = game.Piles(rule.Name);
            for (var i = 0; i < piles.Count; i++)
            {
                // Laid where the rule places each one (from its At), or stepped along from At.
                yield return rule.Places is { } places && i < places.Count
                    ? (piles[i], rule, rule.At.X + places[i].X, rule.At.Y + places[i].Y)
                    : (piles[i], rule, rule.At.X + (rule.Step.X * i), rule.At.Y + (rule.Step.Y * i));
            }
        }
    }

    private static Fan Fan(PileFan fan) => fan switch
    {
        PileFan.Down => Jentl.Core.Cards.Fan.Down,
        PileFan.Right => Jentl.Core.Cards.Fan.Right,
        _ => Jentl.Core.Cards.Fan.Stacked,
    };

    private static string? EmptyLabel(RulesetGame game, Pile pile) =>
        game.RuleOf(pile).Build.Empty is { } rank and not "none"
            ? game.Ruleset.Deck.Faces.FirstOrDefault(f => f.Rank == rank)?.Label ?? rank
            : null;
}
