using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

public readonly record struct PanelLine(string Text, Color Color);

/// <summary>
/// Dims the scene below and draws a centred panel with a title, some lines of text and a menu or a form.
/// A menu that does not fit on the screen scrolls.
/// </summary>
public static class OverlayPanel
{
    private const int DesignPadding = 40;
    private const int LineGap = 4;
    private const int ContentGap = 24;

    /// <summary>How wide a line of text may be inside the panel drawn for <paramref name="viewport"/>.</summary>
    public static int TextWidth(Rectangle viewport, Fonts fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        var padding = fonts.Px(DesignPadding);
        return Math.Min(viewport.Width - 2 * padding, fonts.Px(600)) - 2 * padding;
    }

    public static void Draw(SpriteBatch batch, Rectangle viewport, Fonts fonts, string title, IReadOnlyList<PanelLine> lines, Menu menu)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(menu);
        var room = viewport.Height - Chrome(fonts, lines.Count) - 2 * fonts.Px(12);
        var height = menu.Height(fonts.Body, room);
        var (panel, top) = Frame(batch, viewport, fonts, title, lines, height);
        menu.Draw(batch, fonts.Body, panel.Center.X, top, height);
    }

    public static void Draw(SpriteBatch batch, Rectangle viewport, Fonts fonts, string title, IReadOnlyList<PanelLine> lines, Form form)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(form);
        var (panel, top) = Frame(batch, viewport, fonts, title, lines, form.Height(fonts.Body));
        form.Draw(batch, fonts.Body, panel.Center.X, top, panel.Width - 2 * fonts.Px(DesignPadding) / 2);
    }

    /// <summary>The panel's height without its content.</summary>
    private static int Chrome(Fonts fonts, int lines)
    {
        var padding = fonts.Px(DesignPadding);

        // Menu rows carry their own padding, so the bottom needs less than the top.
        return padding + (int)fonts.Heading.LineHeight + 8 + lines * ((int)fonts.Body.LineHeight + LineGap) + ContentGap + padding / 2;
    }

    /// <summary>Draws the dimmer, the panel, its title and lines; returns the panel and where the content starts.</summary>
    private static (Rectangle Panel, int ContentTop) Frame(SpriteBatch batch, Rectangle viewport, Fonts fonts, string title, IReadOnlyList<PanelLine> lines, int contentHeight)
    {
        batch.FillRectangle(viewport, Palette.Dim);
        var padding = fonts.Px(DesignPadding);
        var height = Chrome(fonts, lines.Count) + contentHeight;
        var width = Math.Min(viewport.Width - 2 * padding, fonts.Px(600));
        var panel = new Rectangle(viewport.Center.X - width / 2, viewport.Center.Y - height / 2, width, height);
        Theme.DrawPanel(batch, panel);

        var y = panel.Y + padding;
        batch.DrawText(fonts.Heading, title, new Vector2(panel.Center.X, y), Palette.Text, Align.Center);
        y += (int)fonts.Heading.LineHeight + 8;
        foreach (var line in lines)
        {
            batch.DrawText(fonts.Body, line.Text, new Vector2(panel.Center.X, y), line.Color, Align.Center, shadow: false);
            y += (int)fonts.Body.LineHeight + LineGap;
        }

        return (panel, y + ContentGap);
    }
}
