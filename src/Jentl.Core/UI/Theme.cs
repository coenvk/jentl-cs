using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <summary>
/// How a surface (a panel, a button, a highlighted row) is drawn, in design pixels: the corners' radius, a border
/// of <see cref="Border"/> pixels in <see cref="BorderColor"/> (none when null or 0), and a drop shadow
/// <see cref="Shadow"/> pixels down and to the right in <see cref="Palette.Shadow"/>. The default is a plain
/// rectangle.
/// </summary>
public sealed record SurfaceStyle(int Radius = 0, int Border = 0, Color? BorderColor = null, int Shadow = 0)
{
    /// <summary>This style in screen pixels at <paramref name="scale"/>; a border or shadow never scales away.</summary>
    public SurfaceStyle Scaled(float scale) => this with
    {
        Radius = (int)MathF.Round(Radius * scale),
        Border = Border == 0 ? 0 : Math.Max(1, (int)MathF.Round(Border * scale)),
        Shadow = Shadow == 0 ? 0 : Math.Max(1, (int)MathF.Round(Shadow * scale)),
    };

    /// <summary>Draws a surface filling <paramref name="bounds"/> with <paramref name="fill"/>, at the UI's scale (<see cref="Theme.Scale"/>).</summary>
    public void Draw(SpriteBatch batch, Rectangle bounds, Color fill)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var style = Scaled(Theme.Scale);
        if (style.Shadow > 0)
        {
            batch.FillRoundedRectangle(bounds with { X = bounds.X + style.Shadow, Y = bounds.Y + style.Shadow }, style.Radius, Palette.Shadow);
        }

        if (style.Border > 0 && style.BorderColor is { } border)
        {
            batch.FillRoundedRectangle(bounds, style.Radius, border);
            var inner = bounds;
            inner.Inflate(-style.Border, -style.Border);
            batch.FillRoundedRectangle(inner, Math.Max(0, style.Radius - style.Border), fill);
            return;
        }

        batch.FillRoundedRectangle(bounds, style.Radius, fill);
    }
}

/// <summary>
/// The look of a game's UI beyond its colours (those are <see cref="Palette"/>): how panels and controls (buttons,
/// form rows, the selected menu item) are drawn. Widgets draw through <see cref="Current"/>, so a game restyles every
/// widget by setting it once at start-up; the default is the plain look.
/// </summary>
public sealed record Theme
{
    /// <summary>The theme widgets draw with.</summary>
    public static Theme Current { get; set; } = new();

    /// <summary>Panels: overlays, dialogs, tables of results.</summary>
    public SurfaceStyle Panel { get; init; } = new();

    /// <summary>Controls: buttons, form rows, the selected menu item.</summary>
    public SurfaceStyle Control { get; init; } = new();

    /// <summary>Whether a panel has a bar in <see cref="Palette.Accent"/> along its top.</summary>
    public bool AccentBar { get; init; } = true;

    /// <summary>The UI scale surfaces are drawn at: kept up to date by <see cref="Fonts.Fit"/>.</summary>
    public static float Scale { get; internal set; } = 1;
}
