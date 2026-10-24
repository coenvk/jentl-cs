using Microsoft.Xna.Framework;

namespace Jentl.Core.Graphics;

public enum ScaleMode
{
    /// <summary>As large as fits, keeping the shape; bars fill the rest of the window.</summary>
    Fit,

    /// <summary>As large as fits in whole-number steps, so every pixel becomes a crisp square block; bars around it.</summary>
    PixelPerfect,

    /// <summary>Scaled as for <see cref="Fit"/>, then widened or heightened to fill the window: no bars, more world shows.</summary>
    Expand,
}

/// <summary>
/// A game designed for one resolution, shown at any window size. Lay the game out in <see cref="Bounds"/> (the
/// design size, or larger for <see cref="ScaleMode.Expand"/>); <see cref="Destination"/> is where that lands in
/// the window and <see cref="ToVirtual"/> maps the mouse back. Give it to <see cref="JentlGame.VirtualScreen"/>
/// and the game renders through it, input included.
/// </summary>
public sealed class VirtualScreen
{
    public VirtualScreen(int width, int height, ScaleMode mode = ScaleMode.Fit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Design = new Point(width, height);
        Mode = mode;
        Size = Design;
        Destination = new Rectangle(0, 0, width, height);
    }

    /// <summary>The resolution the game is designed for.</summary>
    public Point Design { get; }

    public ScaleMode Mode { get; }

    /// <summary>The colour of the bars beside or above the game.</summary>
    public Color BarColor { get; set; } = Color.Black;

    /// <summary>The virtual size, as of the last <see cref="Fit"/>: the design size, except when expanded.</summary>
    public Point Size { get; private set; }

    /// <summary>The virtual screen in its own pixels: the viewport to lay the game out in.</summary>
    public Rectangle Bounds => new(Point.Zero, Size);

    /// <summary>Window pixels per virtual pixel.</summary>
    public float Scale { get; private set; } = 1;

    /// <summary>Where the virtual screen shows in the window.</summary>
    public Rectangle Destination { get; private set; }

    /// <summary>Virtual to window coordinates, for <c>SpriteBatch.Begin(transformMatrix: ...)</c>.</summary>
    public Matrix Transform => Matrix.CreateScale(Scale, Scale, 1) * Matrix.CreateTranslation(Destination.X, Destination.Y, 0);

    /// <summary>Works out the scale and placement for a window. Call when the window may have changed size.</summary>
    public void Fit(Rectangle window)
    {
        var fit = MathF.Min(window.Width / (float)Design.X, window.Height / (float)Design.Y);
        Scale = Mode == ScaleMode.PixelPerfect ? MathF.Max(1, MathF.Floor(fit)) : MathF.Max(fit, float.Epsilon);
        Size = Mode == ScaleMode.Expand
            ? new Point(Math.Max(Design.X, (int)(window.Width / Scale)), Math.Max(Design.Y, (int)(window.Height / Scale)))
            : Design;
        var width = (int)MathF.Round(Size.X * Scale);
        var height = (int)MathF.Round(Size.Y * Scale);
        Destination = new Rectangle(window.X + (window.Width - width) / 2, window.Y + (window.Height - height) / 2, width, height);
    }

    /// <summary>A window point (the mouse, a touch) in virtual pixels.</summary>
    public Vector2 ToVirtual(Vector2 window) => (window - Destination.Location.ToVector2()) / Scale;

    /// <summary>A virtual point in window pixels.</summary>
    public Vector2 ToWindow(Vector2 virtualPoint) => virtualPoint * Scale + Destination.Location.ToVector2();
}
