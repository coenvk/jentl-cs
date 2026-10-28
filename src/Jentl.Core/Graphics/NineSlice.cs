using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>
/// A frame from a picture that stretches to any size without stretching its corners: <see cref="Source"/> in
/// <see cref="Texture"/> is cut into nine parts by <see cref="Left"/>, <see cref="Top"/>, <see cref="Right"/> and
/// <see cref="Bottom"/> pixels from its edges; the corners keep their size, the edges stretch along their length
/// and the middle both ways. A <see cref="UI.SurfaceStyle"/> with one as its art draws panels and controls with it.
/// </summary>
public sealed record NineSlice(Texture2D Texture, Rectangle Source, int Left, int Top, int Right, int Bottom)
{
    /// <summary>Draws the frame filling <paramref name="bounds"/>, tinted <paramref name="color"/>, its corners <paramref name="scale"/> times their size.</summary>
    public void Draw(SpriteBatch batch, Rectangle bounds, Color color, float scale = 1)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var from = Slices(Source, Left, Top, Right, Bottom);
        var to = Slices(bounds, (int)MathF.Round(Left * scale), (int)MathF.Round(Top * scale), (int)MathF.Round(Right * scale), (int)MathF.Round(Bottom * scale));
        for (var i = 0; i < 9; i++)
        {
            if (to[i].Width > 0 && to[i].Height > 0 && from[i].Width > 0 && from[i].Height > 0)
            {
                batch.Draw(Texture, to[i], from[i], color);
            }
        }
    }

    /// <summary>
    /// <paramref name="area"/> cut into nine, row by row from the top left. Borders wider than the area together are
    /// shrunk in proportion, so a frame smaller than its corners still fits.
    /// </summary>
    public static Rectangle[] Slices(Rectangle area, int left, int top, int right, int bottom)
    {
        (left, right) = Fit(left, right, area.Width);
        (top, bottom) = Fit(top, bottom, area.Height);
        int[] xs = [area.Left, area.Left + left, area.Right - right, area.Right];
        int[] ys = [area.Top, area.Top + top, area.Bottom - bottom, area.Bottom];
        var slices = new Rectangle[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                slices[row * 3 + column] = new Rectangle(xs[column], ys[row], xs[column + 1] - xs[column], ys[row + 1] - ys[row]);
            }
        }

        return slices;
    }

    private static (int First, int Second) Fit(int first, int second, int length)
    {
        if (first + second <= length)
        {
            return (first, second);
        }

        var shrunk = (int)((long)first * length / (first + second));
        return (shrunk, length - shrunk);
    }
}
