using Jentl.Cards;
using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Cards;

/// <summary>Draws an amount of money as stacks of flat, colour-coded chips.</summary>
public static class ChipRenderer
{
    private const float GapFraction = 0.15f;
    private const float HeightFraction = 0.45f;
    private const float StepFraction = 0.2f;

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

    /// <summary>How wide <see cref="DrawAmount"/> draws <paramref name="amount"/>: one chip width per stack plus a small gap between stacks.</summary>
    public static float Width(int amount, float chipWidth, int maxPerStack = 10)
    {
        var stacks = Chips.Stacks(Chips.Break(amount), maxPerStack).Count;
        return stacks == 0 ? 0 : (stacks * chipWidth) + ((stacks - 1) * chipWidth * GapFraction);
    }

    /// <summary>Draws <paramref name="amount"/> as stacks side by side, centred on the bottom of the pile.</summary>
    public static void DrawAmount(SpriteBatch batch, Vector2 bottomCentre, int amount, float chipWidth, int maxPerStack = 10)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var stacks = Chips.Stacks(Chips.Break(amount), maxPerStack);
        var left = bottomCentre.X - (Width(amount, chipWidth, maxPerStack) / 2);
        foreach (var stack in stacks)
        {
            for (var i = 0; i < stack.Count; i++)
            {
                DrawChip(batch, new Vector2(left + (chipWidth / 2), bottomCentre.Y - (i * chipWidth * StepFraction)), chipWidth, ColorOf(stack.Value));
            }

            left += chipWidth * (1 + GapFraction);
        }
    }

    private static void DrawChip(SpriteBatch batch, Vector2 bottomCentre, float width, Color color)
    {
        var height = MathF.Max(3, width * HeightFraction);
        var edge = MathF.Max(1, height / 3);
        var x = bottomCentre.X - (width / 2);
        var top = bottomCentre.Y - height;
        var inset = MathF.Max(1, width * 0.12f);
        var dark = Color.Lerp(color, Color.Black, 0.45f);
        var light = Color.Lerp(color, Color.White, 0.5f);

        // The rim below the face, then the face as a flat ellipse of three rows, and a light stripe across the middle.
        batch.FillRectangle(Rect(x, top + height - edge, width, edge), dark);
        batch.FillRectangle(Rect(x + inset, top, width - (2 * inset), height - edge), color);
        batch.FillRectangle(Rect(x, top + (inset / 2), width, MathF.Max(1, height - edge - inset)), color);
        batch.FillRectangle(Rect(x + (width * 0.3f), top + ((height - edge) / 2), width * 0.4f, 1), light);
    }

    private static Rectangle Rect(float x, float y, float width, float height) =>
        new((int)MathF.Round(x), (int)MathF.Round(y), Math.Max(1, (int)MathF.Round(width)), Math.Max(1, (int)MathF.Round(height)));
}
