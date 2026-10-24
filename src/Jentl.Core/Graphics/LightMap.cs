using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>A round light: where (screen pixels), how far it reaches, and its colour (scaled by <see cref="Intensity"/>).</summary>
public readonly record struct Light(Vector2 Position, float Radius, Color Color, float Intensity = 1);

/// <summary>
/// 2D lighting: a picture of the light falling on an area, laid over it so unlit parts sink into
/// <see cref="Ambient"/> and lit ones keep (and tint) their colours. <see cref="Render"/> the lights before the
/// scene draws (in <see cref="Scenes.Scene.DrawLayers"/>), then <see cref="Apply"/> over what was drawn.
/// </summary>
public sealed class LightMap : IDisposable
{
    private readonly RenderLayer _layer = new();
    private Rectangle _area;

    /// <summary>The light everything gets anyway: black hides the unlit, a dim blue reads as night.</summary>
    public Color Ambient { get; set; } = new(26, 28, 48);

    /// <summary>Lights are soft, so the map is drawn this many times smaller than the area, which is cheaper.</summary>
    public int Downscale { get; set; } = 2;

    /// <summary>Draws <paramref name="lights"/> (added up, over <see cref="Ambient"/>) for <paramref name="area"/>. Call with the batch not begun.</summary>
    public void Render(SpriteBatch batch, Rectangle area, IEnumerable<Light> lights)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lights);
        _area = area;
        var scale = 1f / Math.Max(1, Downscale);
        var size = new Point((int)MathF.Ceiling(area.Width * scale), (int)MathF.Ceiling(area.Height * scale));
        var glow = Gradients.GlowTexture(batch.GraphicsDevice);
        _layer.Render(batch, size, Ambient, b =>
        {
            foreach (var light in lights)
            {
                var centre = (light.Position - area.Location.ToVector2()) * scale;
                var radius = light.Radius * scale;
                var bounds = new Rectangle((int)(centre.X - radius), (int)(centre.Y - radius), (int)(radius * 2), (int)(radius * 2));
                b.Draw(glow, bounds, Scale(light.Color, light.Intensity));
            }
        }, BlendModes.Add);
    }

    /// <summary>Darkens and tints what is drawn in the rendered area by the light map. Call inside the begun batch, after drawing the area.</summary>
    public void Apply(SpriteBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_layer.Target is not { } target || _area.IsEmpty)
        {
            return;
        }

        batch.Restart(BlendModes.Multiply);
        batch.Draw(target, _area, Color.White);
        batch.Restart();
    }

    public void Dispose() => _layer.Dispose();

    private static Color Scale(Color color, float intensity) =>
        new(color.R / 255f * intensity, color.G / 255f * intensity, color.B / 255f * intensity, 1f);
}
