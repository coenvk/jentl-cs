using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>Brightness ramps for lights and vignettes, as numbers (testable) and as textures.</summary>
public static class Gradients
{
    private const int GlowSize = 128;

    private static readonly ConditionalWeakTable<GraphicsDevice, Texture2D> Glows = new();

    /// <summary>
    /// A light's brightness at <paramref name="distance"/> from its centre, as a share of its radius: 1 in the
    /// middle, easing smoothly to 0 at the edge and beyond.
    /// </summary>
    public static float Glow(float distance)
    {
        var t = Math.Clamp(1 - distance, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A vignette's brightness at <paramref name="distance"/> from the centre, where 1 is a corner: untouched in
    /// the middle, darkening towards the corners by up to <paramref name="strength"/>.
    /// </summary>
    public static float Vignette(float distance, float strength)
    {
        var t = Math.Clamp((distance - 0.4f) / 0.6f, 0, 1);
        return 1 - strength * t * t * (3 - 2 * t);
    }

    /// <summary>A round white glow, brightness in the colour (for additive light maps), shared per device.</summary>
    public static Texture2D GlowTexture(GraphicsDevice device) => Glows.GetValue(device, static d =>
        Radial(d, GlowSize, distance => Glow(distance)));

    /// <summary>A square texture whose grey level at each pixel is <paramref name="brightness"/> of its distance from the centre (1 at the edge's middle, √2 in the corners).</summary>
    public static Texture2D Radial(GraphicsDevice device, int size, Func<float, float> brightness)
    {
        ArgumentNullException.ThrowIfNull(brightness);
        var pixels = new Color[size * size];
        var centre = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = new Vector2(x - centre, y - centre).Length() / (size / 2f);
                var value = Math.Clamp(brightness(distance), 0, 1);
                pixels[y * size + x] = new Color(value, value, value, 1f);
            }
        }

        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }
}
