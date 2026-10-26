using Jentl.Cards;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Cards;

/// <summary>
/// Draws an amount of money as stacks of chips seen from a little above: each chip a short cylinder in its
/// denomination's colour, with light stripes round its edge (the classic dashed edge), a lighter rim, an inlay and a
/// highlight on the top chip's face, and a shadow under every chip so a stack reads as chips. Nothing is allocated
/// per call.
/// </summary>
public static class ChipRenderer
{
    /// <summary>How many stripes go round a chip's edge.</summary>
    public const int EdgeStripes = 8;

    private const float GapFraction = 0.15f;

    // A chip's face is an ellipse this much as tall as wide; each chip in a stack adds this much of its width.
    private const float FaceFraction = 0.42f;
    private const float EdgeFraction = 0.13f;

    // A stripe covers this share of its slice of the edge.
    private const float StripeShare = 0.34f;

    /// <summary>The colour of a chip worth <paramref name="value"/>; values that are not a default denomination are grey.</summary>
    public static Color ColorOf(int value) => value switch
    {
        1 => new Color(236, 236, 230),
        5 => new Color(200, 48, 52),
        25 => new Color(48, 156, 76),
        100 => new Color(34, 34, 42),
        500 => new Color(128, 62, 160),
        1000 => new Color(236, 200, 52),
        5000 => new Color(228, 128, 36),
        25000 => new Color(98, 176, 230),
        _ => new Color(140, 140, 148),
    };

    /// <summary>The colour of the stripes round the edge of a chip worth <paramref name="value"/>: one that stands out from the chip.</summary>
    public static Color StripeColorOf(int value) => value switch
    {
        1 => new Color(46, 92, 190),
        1000 or 25000 => new Color(30, 34, 52),
        _ => new Color(244, 242, 232),
    };

    /// <summary>How wide <see cref="DrawAmount"/> draws <paramref name="amount"/>: one chip width per stack plus a small gap between stacks.</summary>
    public static float Width(int amount, float chipWidth, int maxPerStack = 10)
    {
        var stacks = Chips.StackCount(amount, maxPerStack);
        return stacks == 0 ? 0 : (stacks * chipWidth) + ((stacks - 1) * chipWidth * GapFraction);
    }

    /// <summary>How tall a stack of <paramref name="chips"/> chips is drawn: every chip's edge and the top chip's face.</summary>
    public static float StackHeight(float chipWidth, int chips) =>
        chips <= 0 ? 0 : FaceHeight(chipWidth) + (chips * EdgeHeight(chipWidth));

    /// <summary>
    /// Where stripe <paramref name="stripe"/> (0 to <see cref="EdgeStripes"/> - 1) round the edge of chip
    /// <paramref name="chip"/> of a stack shows, from the chip's centre: its left and right in pixels, narrower towards
    /// the sides as the edge turns away; null when it is round the back. Each chip in a stack is turned its own way.
    /// </summary>
    public static (float Left, float Right)? EdgeStripe(int stripe, int chip, float chipWidth)
    {
        const float Slice = MathF.Tau / EdgeStripes;
        var turn = (chip * 0.618034f) % 1;
        var centre = ((stripe + turn) * Slice) - MathF.PI;
        var from = MathF.Max(centre - (Slice * StripeShare / 2), -MathF.PI / 2);
        var to = MathF.Min(centre + (Slice * StripeShare / 2), MathF.PI / 2);
        if (to <= from)
        {
            return null;
        }

        var radius = chipWidth / 2;
        var (left, right) = (radius * MathF.Sin(from), radius * MathF.Sin(to));
        return right - left > 0.01f ? (left, right) : null;
    }

    /// <summary>Draws <paramref name="amount"/> as stacks side by side, centred on the bottom of the pile.</summary>
    public static void DrawAmount(SpriteBatch batch, Vector2 bottomCentre, int amount, float chipWidth, int maxPerStack = 10)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var x = bottomCentre.X - (Width(amount, chipWidth, maxPerStack) / 2) + (chipWidth / 2);
        var turn = 0;
        foreach (var stack in Chips.StacksOf(amount, maxPerStack))
        {
            for (var i = 0; i < stack.Count; i++)
            {
                DrawChip(batch, new Vector2(x, bottomCentre.Y - (i * EdgeHeight(chipWidth))), chipWidth, stack.Value, turn + i, i == stack.Count - 1);
            }

            x += chipWidth * (1 + GapFraction);
            turn += 3;
        }
    }

    private static float FaceHeight(float chipWidth) => MathF.Max(3, chipWidth * FaceFraction);

    private static float EdgeHeight(float chipWidth) => MathF.Max(2, chipWidth * EdgeFraction);

    private static void DrawChip(SpriteBatch batch, Vector2 bottomCentre, float width, int value, int turn, bool top)
    {
        var color = ColorOf(value);
        var stripe = StripeColorOf(value);
        var side = Color.Lerp(color, Color.Black, 0.3f);
        var radius = width / 2;
        var face = FaceHeight(width) / 2;
        var edge = EdgeHeight(width);
        var below = new Vector2(bottomCentre.X, bottomCentre.Y - face);
        var above = below with { Y = below.Y - edge };

        // A shadow just under the chip (on the chip below, or the table), then the edge: the lower half of the bottom
        // ellipse and the band up to the face, with its stripes following the curve of the edge.
        batch.FillEllipse(below with { Y = below.Y + MathF.Max(1, edge * 0.3f) }, new Vector2(radius, face), new Color(0, 0, 0, 120));
        batch.FillEllipse(below, new Vector2(radius, face), side);
        FillArea(batch, bottomCentre.X - radius, above.Y, width, edge, side);
        for (var s = 0; s < EdgeStripes; s++)
        {
            if (EdgeStripe(s, turn, width) is var (left, right))
            {
                var middle = (left + right) / 2 / radius;
                var curve = face * MathF.Sqrt(MathF.Max(0, 1 - (middle * middle)));
                FillArea(batch, bottomCentre.X + left, above.Y + curve, right - left, edge, Color.Lerp(stripe, side, 0.2f));
            }
        }

        // The face: only the top chip's shows, the next chip covers the rest. A lighter rim, the face, an inlay and a highlight.
        if (!top)
        {
            return;
        }

        var rim = Color.Lerp(color, Color.White, 0.4f);
        var border = MathF.Max(1, width * 0.06f);
        batch.FillEllipse(above, new Vector2(radius, face), rim);
        batch.FillEllipse(above, new Vector2(radius - border, face - (border * FaceFraction)), color);
        batch.FillEllipse(above, new Vector2(radius * 0.66f, face * 0.66f), Color.Lerp(color, stripe, 0.55f));
        batch.FillEllipse(above, new Vector2(radius * 0.54f, face * 0.54f), color);
        batch.FillEllipse(above - new Vector2(radius * 0.25f, face * 0.3f), new Vector2(radius * 0.4f, face * 0.32f), new Color(255, 255, 255, 46));
    }

    private static void FillArea(SpriteBatch batch, float x, float y, float width, float height, Color color) =>
        batch.Draw(SpriteBatchExtensions.Pixel(batch.GraphicsDevice), new Vector2(x, y), null, color, 0, Vector2.Zero, new Vector2(width, height), SpriteEffects.None, 0);
}
