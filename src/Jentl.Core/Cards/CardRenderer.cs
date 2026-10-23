using Jentl.Cards;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Cards;

/// <summary>Draws single cards: the back, or a face with the rank and suit in the corners and a large suit in the middle.</summary>
public sealed class CardRenderer(CardArt art, ITextDrawer text)
{
    public static readonly Color Red = new(206, 32, 48);
    public static readonly Color Black = new(28, 30, 40);

    public CardArt Art { get; } = art;

    public void Draw(SpriteBatch batch, PlacedCard card, Rectangle bounds, Color? tint = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var shade = tint ?? Color.White;
        if (!card.FaceUp)
        {
            batch.Draw(Art.Back, bounds, shade);
            return;
        }

        batch.Draw(Art.Face, bounds, shade);
        var ink = card.Card.Color == CardColor.Red ? Red : Black;
        var size = bounds.Height * 0.2f;
        var pad = bounds.Width * 0.07f;

        // Top-left corner: rank above a small suit.
        var label = card.Card.RankLabel;
        text.Draw(batch, label, new Vector2(bounds.X + pad, bounds.Y + pad * 0.6f), size, ink);
        var labelHeight = text.Measure(label, size).Y;
        var small = (int)(size * 0.75f);
        var smallAt = new Rectangle((int)(bounds.X + pad), (int)(bounds.Y + pad * 0.6f + labelHeight), small, small);
        batch.Draw(Art.SuitSymbol(card.Card.Suit), smallAt, ink);

        // A large suit in the lower middle, clear of the corner so small cards stay readable.
        var big = (int)(bounds.Width * 0.5f);
        var bigAt = new Rectangle(bounds.Center.X - big / 2 + (int)(bounds.Width * 0.08f), bounds.Bottom - big - (int)(bounds.Height * 0.1f), big, big);
        batch.Draw(Art.SuitSymbol(card.Card.Suit), bigAt, ink);
    }

    /// <summary>The outline of an empty pile, optionally with a hint such as "A" or "K" inside.</summary>
    public void DrawSlot(SpriteBatch batch, Rectangle bounds, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(Art.Slot, bounds, Color.White);
        if (label is not null)
        {
            var size = bounds.Height * 0.3f;
            var measured = text.Measure(label, size);
            text.Draw(batch, label, new Vector2(bounds.Center.X - measured.X / 2, bounds.Center.Y - measured.Y / 2), size, new Color(255, 255, 255, 90));
        }
    }
}
