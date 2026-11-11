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

        if (!card.Card.IsStandard)
        {
            DrawFace(batch, card.Card.Face, bounds, shade);
            return;
        }

        batch.Draw(Art.Face, bounds, shade);
        var ink = card.Card.Color == CardColor.Red ? Red : Black;
        var size = bounds.Height * 0.2f;
        var pad = bounds.Width * 0.07f;

        // Top-left corner: rank with a small suit beside it, so both stay visible when cards fan over each other.
        var label = card.Card.RankLabel;
        var labelAt = new Vector2(bounds.X + pad, bounds.Y + pad * 0.6f);
        text.Draw(batch, label, labelAt, size, ink);
        var labelSize = text.Measure(label, size);
        var small = (int)(size * 0.7f);
        var smallAt = new Rectangle((int)(labelAt.X + labelSize.X + pad * 0.3f), (int)(labelAt.Y + (labelSize.Y - small) / 2), small, small);
        batch.Draw(Art.SuitSymbol(card.Card.Suit), smallAt, ink);

        // A large suit in the lower middle, clear of the corner so small cards stay readable.
        var big = (int)(bounds.Width * 0.5f);
        var bigAt = new Rectangle(bounds.Center.X - big / 2 + (int)(bounds.Width * 0.08f), bounds.Bottom - big - (int)(bounds.Height * 0.1f), big, big);
        batch.Draw(Art.SuitSymbol(card.Card.Suit), bigAt, ink);
    }

    /// <summary>
    /// A card of a deck of its own (Uno's, a ruleset's): the card in its face's fill colour, its label and suit symbol in
    /// the top-left corner, and the label large in the middle, all in its ink.
    /// </summary>
    public void DrawFace(SpriteBatch batch, CardFace face, Rectangle bounds, Color? tint = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(face);
        var shade = tint ?? Color.White;
        var fill = face.Fill is { } background ? Parse(background) : Color.White;
        batch.Draw(Art.Face, bounds, Multiply(fill, shade));
        var ink = Multiply(Parse(face.Ink), shade);
        var pad = bounds.Width * 0.07f;

        var corner = face.Label + face.SuitSymbol;
        var size = Fit(corner, bounds.Height * 0.16f, bounds.Width * 0.5f);
        text.Draw(batch, corner, new Vector2(bounds.X + pad, bounds.Y + (pad * 0.6f)), size, ink);

        // The label large in the middle, shrunk to fit long ones ("skip", "wild").
        var big = Fit(face.Label, bounds.Height * 0.36f, bounds.Width * 0.78f);
        var measured = text.Measure(face.Label, big);
        text.Draw(batch, face.Label, new Vector2(bounds.Center.X - (measured.X / 2), bounds.Center.Y - (measured.Y / 2) + (bounds.Height * 0.05f)), big, ink);
    }

    /// <summary>A colour written as <c>#rrggbb</c>.</summary>
    public static Color Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var value = uint.Parse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        return new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static Color Multiply(Color a, Color b) => new(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, a.A * b.A / 255);

    private float Fit(string label, float size, float width)
    {
        var measured = text.Measure(label, size).X;
        return measured > width ? size * width / measured : size;
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
