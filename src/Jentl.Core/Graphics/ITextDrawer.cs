using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>
/// Draws text at a pixel size. The engine's kits (cards) need a little text but should not depend on
/// a particular font library; the game supplies an implementation with whatever font it uses.
/// </summary>
public interface ITextDrawer
{
    Vector2 Measure(string text, float size);

    /// <summary>Draws <paramref name="text"/> with its top-left corner at <paramref name="position"/>.</summary>
    void Draw(SpriteBatch batch, string text, Vector2 position, float size, Color color);
}
