using FontStashSharp;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <param name="Label">Text on the button.</param>
/// <param name="Press">What it does when tapped or clicked.</param>
/// <param name="Highlight">Draws it in the accent colour, e.g. the main action on a screen.</param>
public sealed record Button(string Label, Action Press, bool Highlight = false);

/// <summary>A centred row of tappable buttons. Like <see cref="Menu"/>, hit areas are where they were last drawn.</summary>
public sealed class ButtonBar
{
    private const int MinGap = 4;

    private Button[] _buttons;
    private Rectangle[] _bounds;

    public ButtonBar(params Button[] buttons)
    {
        _buttons = buttons;
        _bounds = new Rectangle[buttons.Length];
    }

    /// <summary>Swaps the buttons, e.g. when a screen's state changes what is on offer.</summary>
    public void Set(params Button[] buttons)
    {
        _buttons = buttons;
        _bounds = new Rectangle[buttons.Length];
    }

    public static int Height(SpriteFontBase font) => (int)font.LineHeight + Padding(font);

    /// <summary>Width of the bar with normal gaps, to pick a font that fits.</summary>
    public int Width(SpriteFontBase font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var padding = Padding(font);
        return _buttons.Sum(b => (int)font.MeasureString(b.Label).X + 2 * padding) + padding / 2 * (_buttons.Length - 1);
    }

    // Spacing follows the font, so the bar shrinks with the UI on small screens.
    private static int Padding(SpriteFontBase font) => (int)(font.LineHeight * 0.6f);

    /// <summary>Presses the tapped button, if any; returns whether a button took the tap.</summary>
    public bool Update(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        for (var i = 0; i < _buttons.Length; i++)
        {
            if (input.Tapped(_bounds[i]))
            {
                _buttons[i].Press();
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="point"/> is on one of the buttons, so a tap there is not meant for what lies below.</summary>
    public bool Contains(Vector2 point) => _bounds.Any(b => b.Contains(point));

    /// <summary>Draws the buttons centred in <paramref name="area"/>, shrinking the gaps if space is short.</summary>
    public void Draw(SpriteBatch batch, SpriteFontBase font, Rectangle area)
    {
        ArgumentNullException.ThrowIfNull(font);
        var height = Height(font);
        var padding = Padding(font);
        var widths = _buttons.Select(b => (int)font.MeasureString(b.Label).X + 2 * padding).ToArray();
        var gap = padding / 2;
        var total = widths.Sum() + gap * (widths.Length - 1);
        if (total > area.Width && widths.Length > 1)
        {
            gap = Math.Max(MinGap, (area.Width - widths.Sum()) / (widths.Length - 1));
            total = widths.Sum() + gap * (widths.Length - 1);
        }

        var x = area.Center.X - total / 2;
        var y = area.Center.Y - height / 2;
        for (var i = 0; i < _buttons.Length; i++)
        {
            var bounds = new Rectangle(x, y, widths[i], height);
            _bounds[i] = bounds;
            var highlight = _buttons[i].Highlight;
            batch.FillRectangle(bounds, highlight ? Palette.Accent : Palette.PanelLight);
            batch.DrawText(font, _buttons[i].Label, bounds, highlight ? Palette.Ink : Palette.Text, shadow: false);
            x += widths[i] + gap;
        }
    }
}
