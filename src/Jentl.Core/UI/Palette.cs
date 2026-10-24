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
}
