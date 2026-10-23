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
}
