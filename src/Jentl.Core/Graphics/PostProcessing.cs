using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>
/// One step of whole-screen post-processing: takes the finished picture and changes it (glow, vignette,
/// scanlines, a shader). Add effects to <see cref="PostProcessor.Effects"/>; they run in order.
/// </summary>
public abstract class PostEffect : IDisposable
{
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the effect does anything this frame; inactive effects are skipped (and cost nothing).</summary>
    public virtual bool IsActive => Enabled;

    /// <summary>Advances anything animated. Called every frame.</summary>
    public virtual void Update(TimeSpan elapsed)
    {
    }

    /// <summary>
    /// Changes the picture in <paramref name="image"/> and returns the target holding the result: the same one
    /// after drawing onto it, or one of the effect's own.
    /// </summary>
    public abstract RenderTarget2D Apply(PostContext context, RenderTarget2D image);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>What a <see cref="PostEffect"/> draws with.</summary>
public sealed class PostContext
{
    internal PostContext(SpriteBatch batch) => Batch = batch;

    /// <summary>The sprite batch, not begun.</summary>
    public SpriteBatch Batch { get; }

    public GraphicsDevice Device => Batch.GraphicsDevice;

    /// <summary>Stretches <paramref name="texture"/> (or part of it) over the whole of <paramref name="target"/> with <paramref name="blend"/>.</summary>
    public void Draw(Texture2D texture, RenderTarget2D target, BlendState blend, SamplerState? sampler = null, Color? tint = null, Rectangle? source = null)
    {
        Device.SetRenderTarget(target);
        Batch.Begin(blendState: blend, samplerState: sampler ?? SamplerState.LinearClamp);
        Batch.Draw(texture, new Rectangle(0, 0, target.Width, target.Height), source, tint ?? Color.White);
        Batch.End();
    }

    /// <summary>Covers <paramref name="target"/> with <paramref name="color"/> using <paramref name="blend"/>.</summary>
    public void Fill(RenderTarget2D target, Color color, BlendState blend) =>
        Draw(SpriteBatchExtensions.Pixel(Device), target, blend, SamplerState.PointClamp, color);
}

/// <summary>Runs the post effects over the finished frame. The scene manager has one; the game applies it.</summary>
public sealed class PostProcessor : IDisposable
{
    private readonly ScreenFlash _flash = new();

    public IList<PostEffect> Effects { get; } = new List<PostEffect>();

    /// <summary>Whether any effect (or a flash) needs the frame drawn offscreen first.</summary>
    public bool IsActive => _flash.IsActive || Effects.Any(e => e.IsActive);

    /// <summary>Washes the screen with <paramref name="color"/>, fading out over <paramref name="duration"/>: a hit, a lightning strike.</summary>
    public void Flash(Color color, TimeSpan duration) => _flash.Start(color, duration);

    public void Update(TimeSpan elapsed)
    {
        foreach (var effect in Effects)
        {
            effect.Update(elapsed);
        }

        _flash.Update(elapsed);
    }

    /// <summary>Applies every active effect, then the flash; returns the target holding the result.</summary>
    public RenderTarget2D Apply(SpriteBatch batch, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var context = new PostContext(batch);
        foreach (var effect in Effects.Where(e => e.IsActive).Append(_flash).Where(e => e.IsActive))
        {
            image = effect.Apply(context, image);
        }

        return image;
    }

    public void Dispose()
    {
        foreach (var effect in Effects)
        {
            effect.Dispose();
        }

        _flash.Dispose();
    }
}

/// <summary>Bright parts bleed light into their surroundings.</summary>
public sealed class Bloom : PostEffect
{
    private readonly RenderLayer _half = new();
    private readonly RenderLayer _quarter = new();
    private readonly RenderLayer _eighth = new();

    /// <summary>How bright (0 to 1) a colour channel must be to glow.</summary>
    public float Threshold { get; set; } = 0.6f;

    /// <summary>How strongly the glow is added (0 to 1).</summary>
    public float Intensity { get; set; } = 0.7f;

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        var size = new Point(image.Width, image.Height);
        var half = _half.Ensure(context.Device, new Point(size.X / 2, size.Y / 2));
        var quarter = _quarter.Ensure(context.Device, new Point(size.X / 4, size.Y / 4));
        var eighth = _eighth.Ensure(context.Device, new Point(size.X / 8, size.Y / 8));

        // Keep only what is above the threshold, then blur by shrinking: each halving smears it further.
        context.Draw(image, half, BlendState.Opaque);
        context.Fill(half, new Color(Threshold, Threshold, Threshold), BlendModes.Subtract);
        context.Draw(half, quarter, BlendState.Opaque);
        context.Draw(quarter, eighth, BlendState.Opaque);

        var strength = new Color(Intensity, Intensity, Intensity);
        context.Draw(quarter, image, BlendModes.Add, tint: strength);
        context.Draw(eighth, image, BlendModes.Add, tint: strength);
        return image;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _half.Dispose();
            _quarter.Dispose();
            _eighth.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Darkens towards the corners, drawing the eye to the middle.</summary>
public sealed class Vignette : PostEffect
{
    private Texture2D? _texture;
    private float _built = -1;

    /// <summary>How dark the corners get (0 to 1).</summary>
    public float Strength { get; set; } = 0.45f;

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        if (_texture is null || _built != Strength)
        {
            _texture?.Dispose();
            var strength = Strength;
            _texture = Gradients.Radial(context.Device, 128, distance => Gradients.Vignette(distance / MathF.Sqrt(2), strength));
            _built = Strength;
        }

        context.Draw(_texture, image, BlendModes.Multiply);
        return image;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _texture?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Dark lines across the picture, like an old monitor.</summary>
public sealed class Scanlines : PostEffect
{
    private Texture2D? _texture;
    private float _built = -1;

    /// <summary>How dark the lines are (0 to 1).</summary>
    public float Strength { get; set; } = 0.22f;

    /// <summary>How tall each line and gap is, in pixels; 0 picks a size from the picture's height (about 180 lines).</summary>
    public int LineHeight { get; set; }

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        if (_texture is null || _built != Strength)
        {
            _texture?.Dispose();
            _texture = new Texture2D(context.Device, 1, 2);
            _texture.SetData([Color.White, new Color(1 - Strength, 1 - Strength, 1 - Strength)]);
            _built = Strength;
        }

        // The two-pixel pattern repeats down the picture, each pixel stretched to a line.
        var line = LineHeight > 0 ? LineHeight : Math.Max(1, image.Height / 360);
        context.Draw(_texture, image, BlendModes.Multiply, SamplerState.PointWrap, source: new Rectangle(0, 0, 1, Math.Max(2, image.Height / line)));
        return image;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _texture?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Big square pixels: the picture shrunk and blown up again.</summary>
public sealed class Pixelate : PostEffect
{
    private readonly RenderLayer _small = new();

    /// <summary>Screen pixels per big pixel.</summary>
    public int Size { get; set; } = 4;

    public override bool IsActive => Enabled && Size > 1;

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        var small = _small.Ensure(context.Device, new Point(image.Width / Size, image.Height / Size));
        context.Draw(image, small, BlendState.Opaque, SamplerState.PointClamp);
        context.Draw(small, image, BlendState.Opaque, SamplerState.PointClamp);
        return image;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _small.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Draws the picture through a compiled shader (<see cref="Microsoft.Xna.Framework.Graphics.Effect"/>, loaded
/// from the content pipeline or <c>new Effect(device, bytes)</c>). Set its parameters in <see cref="Prepare"/>.
/// </summary>
public sealed class ShaderPass(Effect effect) : PostEffect
{
    private readonly RenderLayer _output = new();

    public Effect Shader { get; } = effect ?? throw new ArgumentNullException(nameof(effect));

    /// <summary>Called before drawing with the picture's target, to set shader parameters (time, size...).</summary>
    public Action<Effect, RenderTarget2D>? Prepare { get; set; }

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        Prepare?.Invoke(Shader, image);
        var output = _output.Ensure(context.Device, new Point(image.Width, image.Height));
        context.Device.SetRenderTarget(output);
        context.Batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: Shader);
        context.Batch.Draw(image, Vector2.Zero, Color.White);
        context.Batch.End();
        return output;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _output.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>A colour washed over the screen that fades out. <see cref="PostProcessor.Flash"/> drives one.</summary>
public sealed class ScreenFlash : PostEffect
{
    private Color _color;
    private TimeSpan _duration;
    private TimeSpan _left;

    public override bool IsActive => Enabled && _left > TimeSpan.Zero;

    /// <summary>How strong the flash is now, 0 to 1.</summary>
    public float Amount => _duration <= TimeSpan.Zero ? 0 : (float)(_left / _duration) * (_color.A / 255f);

    public void Start(Color color, TimeSpan duration)
    {
        _color = color;
        _duration = _left = duration;
    }

    public override void Update(TimeSpan elapsed) => _left = _left > elapsed ? _left - elapsed : TimeSpan.Zero;

    public override RenderTarget2D Apply(PostContext context, RenderTarget2D image)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(image);
        context.Fill(image, new Color(_color.R, _color.G, _color.B, (byte)(Amount * 255)), BlendState.NonPremultiplied);
        return image;
    }
}
