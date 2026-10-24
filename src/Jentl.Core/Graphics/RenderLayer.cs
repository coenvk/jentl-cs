using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>Blend states beyond the built-in ones, for lighting and post effects.</summary>
public static class BlendModes
{
    /// <summary>Destination × source: darkens, white leaves alone. Light maps and vignettes.</summary>
    public static BlendState Multiply { get; } = new()
    {
        Name = "Multiply",
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

    /// <summary>Destination + source, ignoring alpha (tint the source to weaken it). Lights and glow.</summary>
    public static BlendState Add { get; } = new()
    {
        Name = "Add",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

    /// <summary>Destination − source, stopping at black. Keeps only what is brighter than the source.</summary>
    public static BlendState Subtract { get; } = new()
    {
        Name = "Subtract",
        ColorBlendFunction = BlendFunction.ReverseSubtract,
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

    /// <summary>1 − (1 − destination)(1 − source): brightens without blowing out.</summary>
    public static BlendState Screen { get; } = new()
    {
        Name = "Screen",
        ColorSourceBlend = Blend.InverseDestinationColor,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };
}

/// <summary>
/// An offscreen picture to draw into and then use as a texture: a light map, a minimap, a blurred copy. Its
/// render target follows the size asked for and keeps its contents when other targets are drawn to in between.
/// </summary>
public sealed class RenderLayer : IDisposable
{
    public RenderTarget2D? Target { get; private set; }

    /// <summary>The layer's target at <paramref name="size"/>, made (again) when the size changes.</summary>
    public RenderTarget2D Ensure(GraphicsDevice device, Point size)
    {
        ArgumentNullException.ThrowIfNull(device);
        size = new Point(Math.Max(1, size.X), Math.Max(1, size.Y));
        if (Target is null || Target.Width != size.X || Target.Height != size.Y || Target.GraphicsDevice != device)
        {
            Target?.Dispose();
            Target = Create(device, size);
        }

        return Target;
    }

    /// <summary>
    /// Draws into the layer: switches to its target, clears it to <paramref name="clear"/> (if given), runs
    /// <paramref name="draw"/> in a begun batch, then switches back to whatever target was in use. Call with the
    /// batch not begun, e.g. from <see cref="Scenes.Scene.DrawLayers"/>.
    /// </summary>
    public RenderTarget2D Render(
        SpriteBatch batch,
        Point size,
        Color? clear,
        Action<SpriteBatch> draw,
        BlendState? blend = null,
        SamplerState? sampler = null,
        Matrix? transform = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(draw);
        var device = batch.GraphicsDevice;
        var target = Ensure(device, size);
        var previous = device.GetRenderTargets();
        device.SetRenderTarget(target);
        if (clear is { } color)
        {
            device.Clear(color);
        }

        batch.Begin(blendState: blend ?? BlendState.NonPremultiplied, samplerState: sampler ?? SamplerState.LinearClamp, transformMatrix: transform);
        draw(batch);
        batch.End();
        device.SetRenderTargets(previous);
        return target;
    }

    public void Dispose()
    {
        Target?.Dispose();
        Target = null;
    }

    /// <summary>A plain colour target that keeps its contents (the default would clear it when switched back to).</summary>
    internal static RenderTarget2D Create(GraphicsDevice device, Point size) =>
        new(device, size.X, size.Y, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
}
