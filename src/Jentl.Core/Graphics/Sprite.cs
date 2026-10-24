using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>A picture to draw: part of a texture (one sprite of an atlas), or all of it.</summary>
public readonly record struct Sprite(Texture2D Texture, Rectangle Source)
{
    public int Width => Source.Width;

    public int Height => Source.Height;

    /// <summary>The whole of <paramref name="texture"/>.</summary>
    public static Sprite Whole(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new Sprite(texture, texture.Bounds);
    }
}

public static class SpriteDrawing
{
    /// <summary>Draws <paramref name="sprite"/> stretched over <paramref name="destination"/>.</summary>
    public static void Draw(this SpriteBatch batch, Sprite sprite, Rectangle destination, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(sprite.Texture, destination, sprite.Source, color);
    }

    public static void Draw(this SpriteBatch batch, Sprite sprite, Rectangle destination, Color color, float rotation, Vector2 origin, SpriteEffects effects)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(sprite.Texture, destination, sprite.Source, color, rotation, origin, effects, 0);
    }
}
