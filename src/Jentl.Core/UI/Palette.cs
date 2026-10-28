using Microsoft.Xna.Framework;

namespace Jentl.Core.UI;

/// <summary>
/// The UI's colours. Widgets read them when they draw, so a game can restyle the whole UI by setting them
/// once at start-up.
/// </summary>
public static class Palette
{
    public static Color Background { get; set; } = new(28, 33, 48);

    public static Color Panel { get; set; } = new(41, 48, 69);

    public static Color PanelLight { get; set; } = new(58, 67, 94);

    public static Color Text { get; set; } = new(242, 238, 228);

    public static Color Muted { get; set; } = new(150, 160, 186);

    public static Color Accent { get; set; } = new(255, 199, 44);

    /// <summary>Success: a solved level, a switch that is on.</summary>
    public static Color Solved { get; set; } = new(56, 128, 84);

    /// <summary>Text on an accent background.</summary>
    public static Color Ink { get; set; } = new(28, 33, 48);

    public static Color Shadow { get; set; } = new(0, 0, 0, 110);

    /// <summary>Dims the screen behind a panel.</summary>
    public static Color Dim { get; set; } = new(10, 12, 20, 170);

    /// <summary>The colours as they are now, to change and <see cref="PaletteColors.Apply"/>, or to go back to.</summary>
    public static PaletteColors Capture() => new(Background, Panel, PanelLight, Text, Muted, Accent, Solved, Ink, Shadow, Dim);
}

/// <summary>
/// All of <see cref="Palette"/>'s colours at once. Capture a game's palette at start-up, then apply it, or a variant
/// for players who need one (<see cref="WithHighContrast"/>, <see cref="WithColorBlindSafe"/>), when they change the
/// setting.
/// </summary>
public sealed record PaletteColors(
    Color Background,
    Color Panel,
    Color PanelLight,
    Color Text,
    Color Muted,
    Color Accent,
    Color Solved,
    Color Ink,
    Color Shadow,
    Color Dim)
{
    /// <summary>A blue that reads as "done" to players who cannot tell green from red (Okabe and Ito's).</summary>
    private static readonly Color SafeBlue = new(0, 114, 178);

    /// <summary>Makes these the colours widgets draw with.</summary>
    public void Apply()
    {
        Palette.Background = Background;
        Palette.Panel = Panel;
        Palette.PanelLight = PanelLight;
        Palette.Text = Text;
        Palette.Muted = Muted;
        Palette.Accent = Accent;
        Palette.Solved = Solved;
        Palette.Ink = Ink;
        Palette.Shadow = Shadow;
        Palette.Dim = Dim;
    }

    /// <summary>
    /// The same colours pushed apart, for players who see contrast poorly: text white or black (whichever stands out
    /// more from the background), muted text close to it, surfaces further from the text, and a darker dim.
    /// </summary>
    public PaletteColors WithHighContrast()
    {
        var darkText = Luminance(Background) > 0.5;
        var text = darkText ? Color.Black : Color.White;
        var away = darkText ? Color.White : Color.Black;
        return this with
        {
            Text = text,
            Muted = Color.Lerp(Muted, text, 0.6f),
            Background = Color.Lerp(Background, away, 0.5f),
            Panel = Color.Lerp(Panel, away, 0.45f),
            PanelLight = Color.Lerp(PanelLight, away, 0.3f),
            Ink = Luminance(Accent) > 0.5 ? Color.Black : Color.White,
            Dim = new Color(Dim.R, Dim.G, Dim.B, (byte)Math.Max(Dim.A, (byte)220)),
        };
    }

    /// <summary>The same colours with success shown in blue rather than green, which red-green colour blindness hides.</summary>
    public PaletteColors WithColorBlindSafe() => this with { Solved = SafeBlue };

    /// <summary>The WCAG contrast ratio of two colours, from 1 (none) to 21 (black on white); 4.5 is enough for text, 7 good.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>A colour's relative luminance, as WCAG defines it.</summary>
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
