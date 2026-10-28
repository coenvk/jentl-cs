using FontStashSharp;
using FontStashSharp.Interfaces;
using FontStashSharp.Rasterizers.StbTrueTypeSharp;
using Microsoft.Xna.Framework;

namespace Jentl.Core.UI;

/// <summary>
/// A font in the sizes the UI uses, scaled to the screen. Glyphs are rasterised on demand,
/// so a new scale (resizing the window, rotating a phone) just renders new sizes. The card suits ♠ ♥ ♦ ♣ come
/// from <see cref="SuitGlyphSource"/> when the font has none of its own.
/// </summary>
public sealed class Fonts : IDisposable
{
    // The layout was designed at this size; the UI scales from it.
    private const float DesignWidth = 1280;
    private const float DesignHeight = 800;
    private const float MinScale = 0.6f;
    private const float MaxScale = 1.4f;

    // Small print stays readable on phones however far the rest scales down.
    private const int MinSmallSize = 15;

    private readonly FontSystem _system;
    private float _textScale = 1;

    /// <param name="font">A TrueType or OpenType font file's bytes.</param>
    public Fonts(byte[] font)
    {
        // The sprite batch blends non-premultiplied (see JentlGame), so the glyph atlas must be too.
        var settings = new FontSystemSettings { GlyphRenderResult = GlyphRenderResult.NonPremultiplied };
        settings.FontLoader = new Loader(settings);
        _system = new FontSystem(settings);
        _system.AddFont(font);
        _system.AddFont(SuitGlyphSource.Marker);
    }

    /// <summary>UI scale: 1 at 1280x800, smaller on small screens.</summary>
    public float Scale { get; private set; } = 1;

    /// <summary>A screen noticeably taller than wide, such as a phone held upright.</summary>
    public bool IsPortrait { get; private set; }

    public SpriteFontBase Title => _system.GetFont(Text(112));

    public SpriteFontBase Heading => _system.GetFont(Text(52));

    public SpriteFontBase Body => _system.GetFont(Text(34));

    public SpriteFontBase Small => _system.GetFont(Math.Max((int)MathF.Round(MinSmallSize * TextScale), Text(22)));

    /// <summary>The font at any pixel size, for text that scales with something other than the UI (such as cards).</summary>
    public SpriteFontBase Sized(float pixels) => _system.GetFont(Math.Max(6, (int)MathF.Round(pixels)));

    /// <summary>Recomputes the scale for the current screen; call once per frame.</summary>
    public void Fit(Rectangle viewport)
    {
        IsPortrait = viewport.Height > viewport.Width * 1.15f;

        // In portrait the width is what runs out, so scale by it alone.
        var fit = IsPortrait
            ? viewport.Width / (DesignHeight * 0.75f)
            : MathF.Min(viewport.Width / DesignWidth, viewport.Height / DesignHeight);
        Scale = Math.Clamp(fit, MinScale, MaxScale);
        Theme.Scale = Scale;
    }

    /// <summary>A design-size length in pixels at the current scale.</summary>
    public int Px(float designPixels) => (int)MathF.Round(designPixels * Scale);

    /// <summary>
    /// How much bigger the UI's text is than designed, from 1 to 2, for players who need larger text: the title,
    /// heading, body and small fonts grow by it, and widgets laid out by their font (menus, forms, panels) with them.
    /// <see cref="Px"/> lengths and <see cref="Sized"/> fonts do not change.
    /// </summary>
    public float TextScale
    {
        get => _textScale;
        set => _textScale = Math.Clamp(value, 1, 2);
    }

    private int Text(float designPixels) => (int)MathF.Round(designPixels * Scale * TextScale);

    public void Dispose() => _system.Dispose();

    /// <summary>Loads font files as FontStashSharp does by default, and the suits' marker as <see cref="SuitGlyphSource"/>.</summary>
    private sealed class Loader(FontSystemSettings settings) : IFontLoader
    {
        private readonly StbTrueTypeSharpLoader _files = new(new StbTrueTypeSharpSettings
        {
            KernelWidth = settings.KernelWidth,
            KernelHeight = settings.KernelHeight,
            UseOldRasterizer = settings.StbTrueTypeUseOldRasterizer,
            UseEmToPixelsScale = settings.UseEmToPixelsScale,
        });

        public IFontSource Load(byte[] data) => ReferenceEquals(data, SuitGlyphSource.Marker) ? new SuitGlyphSource() : _files.Load(data);
    }
}
