using Jentl.Multiplayer;

namespace Jentl.Cards.Rulesets;

/// <summary>
/// A computer player for any ruleset game for several players. It plays rather than draws; holds its wilds back while
/// another card goes; plays skips and draws first when the next player is close to going out; otherwise sheds its
/// highest-scoring card; and names the suit it holds most of.
/// </summary>
public sealed class RulesetBot : IBot<RulesetGame, CardInput>
{
    public CardInput Choose(RulesetGame game, int seat, Random random)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(random);
        var moves = game.LegalMoves(seat);
        if (moves.Count == 0)
        {
            throw new InvalidOperationException($"Seat {seat} has nothing to do.");
        }

        var hand = game.Hand(seat);
        if (moves[0] is ChooseSuit)
        {
            // The suit held most, any if none.
            var counts = game.Suits.ToDictionary(s => s, s => hand.Cards.Count(c => c.Card.Face.Suit == s));
            var best = counts.Values.Max();
            var suits = counts.Where(c => c.Value == best).Select(c => c.Key).ToList();
            return new ChooseSuit(suits[random.Next(suits.Count)]);
        }

        var plays = moves.OfType<ClickPile>().Where(m => m.Pile == hand).ToList();
        if (plays.Count == 0)
        {
            return moves[0];
        }

        var next = game.Hand((((seat + game.Direction) % game.Players) + game.Players) % game.Players);
        var urgent = next.Count <= 2;
        return plays
            .OrderBy(p => hand[p.Index].Card.Face.Has("wild") ? 1 : 0)
            .ThenByDescending(p => urgent && Hurts(game, hand[p.Index].Card.Face) ? 1 : 0)
            .ThenByDescending(p => hand[p.Index].Card.Face.Points)
            .ThenBy(_ => random.Next())
            .First();
    }

    private static bool Hurts(RulesetGame game, CardFace face) =>
        game.Ruleset.Effects.Any(e => e.Cards.Matches(face) && (e.Skip > 0 || e.Draw > 0 || e.Reverse));
}
