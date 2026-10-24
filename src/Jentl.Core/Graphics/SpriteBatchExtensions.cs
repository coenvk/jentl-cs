using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

public static class SpriteBatchExtensions
{
    // One white pixel per device, stretched and tinted to draw solid rectangles.
    private static readonly ConditionalWeakTable<GraphicsDevice, Texture2D> Pixels = new();

    public static void FillRectangle(this SpriteBatch batch, Rectangle rectangle, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var pixel = Pixels.GetValue(batch.GraphicsDevice, static device =>
        {
            var texture = new Texture2D(device, 1, 1);
            texture.SetData([Color.White]);
            return texture;
        });
        batch.Draw(pixel, rectangle, color);
    }

    /// <summary>An upward- or downward-pointing triangle filling <paramref name="area"/>, drawn as a stack of one-pixel lines.</summary>
    public static void FillTriangle(this SpriteBatch batch, Rectangle area, bool pointsUp, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        for (var y = 0; y < area.Height; y++)
        {
            var row = pointsUp ? y : area.Height - 1 - y;
            var width = Math.Max(1, (row + 1) * area.Width / area.Height);
            batch.FillRectangle(new Rectangle(area.Center.X - width / 2, area.Y + y, width, 1), color);
        }
    }
}
