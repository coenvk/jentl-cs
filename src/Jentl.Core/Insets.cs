using Microsoft.Xna.Framework;

namespace Jentl.Core;

/// <summary>
/// Room to leave at a window's edges, in pixels: a phone's status bar, gesture bar or camera cut-out, where the system
/// draws over the game or a finger can't reach.
/// </summary>
public readonly record struct Insets(int Left, int Top, int Right, int Bottom)
{
    public static Insets None => default;

    /// <summary>What is left of <paramref name="bounds"/> inside the insets (never less than nothing).</summary>
    public Rectangle Inside(Rectangle bounds) => new(
        bounds.X + Left,
        bounds.Y + Top,
        Math.Max(0, bounds.Width - Left - Right),
        Math.Max(0, bounds.Height - Top - Bottom));
}
