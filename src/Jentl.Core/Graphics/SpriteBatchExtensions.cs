using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

public static class SpriteBatchExtensions
{
    // One white pixel per device, stretched and tinted to draw solid rectangles.
    private static readonly ConditionalWeakTable<GraphicsDevice, Texture2D> Pixels = new();

    // A soft round dot per device, for particles and other round things.
    private static readonly ConditionalWeakTable<GraphicsDevice, Texture2D> Dots = new();

    public static void FillRectangle(this SpriteBatch batch, Rectangle rectangle, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(Pixel(batch.GraphicsDevice), rectangle, color);
    }

    /// <summary>
    /// Ends the batch and begins it again, with the engine's usual state (non-premultiplied blending, linear
    /// sampling) unless told otherwise: for switching blend mode in the middle of drawing.
    /// </summary>
    public static void Restart(this SpriteBatch batch, BlendState? blend = null, SamplerState? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.End();
        batch.Begin(blendState: blend ?? BlendState.NonPremultiplied, samplerState: sampler ?? SamplerState.LinearClamp);
    }

    /// <summary>The outline of a rectangle, <paramref name="thickness"/> pixels wide on the inside of its edges.</summary>
    public static void StrokeRectangle(this SpriteBatch batch, Rectangle rectangle, Color color, int thickness = 1)
    {
        var t = Math.Min(thickness, Math.Min(rectangle.Width, rectangle.Height) / 2 + 1);
        batch.FillRectangle(rectangle with { Height = t }, color);
        batch.FillRectangle(new Rectangle(rectangle.X, rectangle.Bottom - t, rectangle.Width, t), color);
        batch.FillRectangle(new Rectangle(rectangle.X, rectangle.Y + t, t, rectangle.Height - 2 * t), color);
        batch.FillRectangle(new Rectangle(rectangle.Right - t, rectangle.Y + t, t, rectangle.Height - 2 * t), color);
    }

    /// <summary>A straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static void DrawLine(this SpriteBatch batch, Vector2 from, Vector2 to, Color color, float thickness = 1)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var along = to - from;
        batch.Draw(Pixel(batch.GraphicsDevice), from, null, color, MathF.Atan2(along.Y, along.X), new Vector2(0, 0.5f), new Vector2(along.Length(), thickness), SpriteEffects.None, 0);
    }

    /// <summary>A rectangle of <paramref name="size"/> centred on <paramref name="center"/>, turned by <paramref name="rotation"/> radians.</summary>
    public static void FillRotated(this SpriteBatch batch, Vector2 center, Vector2 size, float rotation, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(Pixel(batch.GraphicsDevice), center, null, color, rotation, new Vector2(0.5f), size, SpriteEffects.None, 0);
    }

    /// <summary>A round dot with soft edges, <paramref name="radius"/> pixels from centre to edge.</summary>
    public static void FillCircle(this SpriteBatch batch, Vector2 center, float radius, Color color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        const int Size = 32;
        var dot = Dots.GetValue(batch.GraphicsDevice, static device =>
        {
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f) / Size * 2 - 1;
                    var dy = (y + 0.5f) / Size * 2 - 1;
                    var edge = Math.Clamp((1 - MathF.Sqrt(dx * dx + dy * dy)) * Size / 2, 0, 1);
                    pixels[y * Size + x] = new Color(Color.White, edge);
                }
            }

            var texture = new Texture2D(device, Size, Size);
            texture.SetData(pixels);
            return texture;
        });
        batch.Draw(dot, center, null, color, 0, new Vector2(Size / 2f), radius * 2 / Size, SpriteEffects.None, 0);
    }

    internal static Texture2D Pixel(GraphicsDevice device) => Pixels.GetValue(device, static d =>
    {
        var texture = new Texture2D(d, 1, 1);
        texture.SetData([Color.White]);
        return texture;
    });

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


    /// <summary>
    /// Draws only inside <paramref name="area"/> until the returned scope is disposed: for scrolling lists and
    /// panels. The batch is restarted with the engine's usual blending and <paramref name="sampler"/> (linear by
    /// default; point keeps pixel art crisp). Clips nest: an inner clip stays inside the outer one, and ending it
    /// brings the outer clip (and its sampler) back. Ending the outermost restarts the batch unclipped with the
    /// engine's usual state; a transform matrix is not kept, so clip outside transformed drawing.
    /// </summary>
    public static ClipScope Clip(this SpriteBatch batch, Rectangle area, SamplerState? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var device = batch.GraphicsDevice;
        var clips = Clips.GetOrCreateValue(batch);
        var bounds = Rectangle.Intersect(area, clips.Count > 0 ? clips.Peek().Area : device.Viewport.Bounds);
        if (clips.Count == 0)
        {
            Unclipped.AddOrUpdate(batch, new StrongBox<Rectangle>(device.ScissorRectangle));
        }

        clips.Push((bounds, sampler ?? SamplerState.LinearClamp));
        BeginClipped(batch, clips.Peek());
        return new ClipScope(batch);
    }

    internal static void EndClip(SpriteBatch batch)
    {
        var clips = Clips.GetOrCreateValue(batch);
        if (clips.Count == 0)
        {
            return;
        }

        clips.Pop();
        if (clips.Count > 0)
        {
            BeginClipped(batch, clips.Peek());
            return;
        }

        batch.End();
        if (Unclipped.TryGetValue(batch, out var scissor))
        {
            batch.GraphicsDevice.ScissorRectangle = scissor.Value;
        }

        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
    }

    private static void BeginClipped(SpriteBatch batch, (Rectangle Area, SamplerState Sampler) clip)
    {
        batch.End();
        batch.GraphicsDevice.ScissorRectangle = clip.Area;
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: clip.Sampler, rasterizerState: ScissorOn);
    }

    private static readonly ConditionalWeakTable<SpriteBatch, Stack<(Rectangle Area, SamplerState Sampler)>> Clips = new();
    private static readonly ConditionalWeakTable<SpriteBatch, StrongBox<Rectangle>> Unclipped = new();
    private static readonly RasterizerState ScissorOn = new() { ScissorTestEnable = true, CullMode = CullMode.None };
}

/// <summary>Ends a <see cref="SpriteBatchExtensions.Clip"/>: the outer clip, or none, applies again.</summary>
public readonly struct ClipScope : IDisposable
{
    private readonly SpriteBatch? _batch;

    internal ClipScope(SpriteBatch batch) => _batch = batch;

    /// <summary>Restarts the batch with the outer clip, or unclipped.</summary>
    public void Dispose()
    {
        if (_batch is not null)
        {
            SpriteBatchExtensions.EndClip(_batch);
        }
    }
}
