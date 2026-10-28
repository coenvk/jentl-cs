namespace Jentl.Core;

/// <summary>
/// Settings for players who need the game to behave differently, read by the engine where it applies them. The rest
/// lives with what it changes: <see cref="UI.Fonts.TextScale"/> for bigger text, <see cref="UI.PaletteColors"/> for
/// stronger contrast or colours that do not lean on red and green, <see cref="Input.Rumble"/> to turn vibration down,
/// and <see cref="Input.ActionMap{TAction}.SetToggle"/> to press once instead of holding.
/// </summary>
public static class Accessibility
{
    /// <summary>
    /// Less movement on screen, for players made dizzy or sick by it: the camera does not shake and wipes between
    /// scenes become fades. A game checks it for its own motion (screen flashes, bouncing, parallax) through
    /// <see cref="Motion"/>.
    /// </summary>
    public static bool ReduceMotion { get; set; }

    /// <summary><paramref name="amount"/> of some movement, or none while <see cref="ReduceMotion"/> is on.</summary>
    public static float Motion(float amount) => ReduceMotion ? 0 : amount;
}
