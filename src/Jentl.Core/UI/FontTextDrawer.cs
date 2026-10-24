using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <summary>The engine's text hook, drawn with the game's font.</summary>
public sealed class FontTextDrawer(Fonts fonts) : ITextDrawer
{
    public Vector2 Measure(string text, float size) => fonts.Sized(size).MeasureString(text);

    public void Draw(SpriteBatch batch, string text, Vector2 position, float size, Color color) =>
        fonts.Sized(size).DrawText(batch, text, position, color);
}
