using System.Diagnostics;
using System.Globalization;
using Jentl.Core.Content;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace Jentl.Core;

/// <summary>
/// Base game: owns the sprite batch, input and scene stack, and runs the loop.
/// Exits when the scene stack becomes empty.
/// </summary>
public abstract class JentlGame : Game
{
    private static readonly TimeSpan TouchMouseGuard = TimeSpan.FromMilliseconds(600);

    private SpriteBatch? _batch;
    private TimeSpan _lastTouch = TimeSpan.MinValue / 2;
    private PointerState _lastPointer;
    private PointerState _realPointer;
    private RenderTarget2D? _target;
    private AssetStore? _assets;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastDraw;
    private TimeSpan _updateTime;

    protected JentlGame(int width, int height)
    {
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = width,
            PreferredBackBufferHeight = height,
        };
    }

    public InputState Input { get; } = new();

    public SceneManager Scenes { get; } = new();

    /// <summary>The gamepad's stand-in for the mouse: right stick to move, right trigger to press.</summary>
    public PadCursor PadCursor { get; } = new();

    /// <summary>
    /// A fixed design resolution to render at, scaled to the window; null (the default) lays scenes out in the
    /// window itself. When set, scenes draw into a render target of <see cref="VirtualScreen.Size"/>, which is
    /// then scaled into the window, and the pointer arrives in virtual pixels.
    /// </summary>
    public VirtualScreen? VirtualScreen { get; set; }

    /// <summary>The area scenes lay themselves out in: the virtual screen, or else the window.</summary>
    public Rectangle ScreenBounds => VirtualScreen?.Bounds ?? GraphicsDevice.Viewport.Bounds;

    protected GraphicsDeviceManager Graphics { get; }

    protected virtual Color ClearColor => Color.Black;

    /// <summary>Renders the current backbuffer to a PNG file.</summary>
    public void SaveScreenshot(string path)
    {
        var parameters = GraphicsDevice.PresentationParameters;
        var (width, height) = (parameters.BackBufferWidth, parameters.BackBufferHeight);
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        MakeOpaque(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        using var file = File.Create(path);
        texture.SaveAsPng(file, width, height);
    }

    /// <summary>
    /// Sets every pixel's alpha to opaque: the screen shows the colours, but a translucent overlay leaves the back
    /// buffer's alpha below 1, and a PNG saved as is looks see-through (grey) in viewers.
    /// </summary>
    internal static void MakeOpaque(Color[] pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = 255;
        }
    }

    /// <summary>The game's assets: loaded once, and reloaded as they change when <see cref="AssetStore.Watch"/> is on.</summary>
    public AssetStore Assets => _assets ?? throw new InvalidOperationException("Assets are available from LoadContent on.");

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _assets = new AssetStore(GraphicsDevice);
    }

    /// <summary>The keyboard state for this frame. Override to feed scripted input.</summary>
    protected virtual KeyboardState ReadKeyboard() => Keyboard.GetState();

    /// <summary>
    /// The pointer for this frame: the first touch if there is one, otherwise the mouse. Mobile browsers
    /// follow every tap with emulated mouse events, so the mouse is ignored briefly after any touch.
    /// Override to feed scripted input.
    /// </summary>
    protected virtual PointerState ReadPointer(TimeSpan now)
    {
        var touches = TouchPanel.GetState();
        if (touches.Count > 0)
        {
            _lastTouch = now;
            var touch = touches[0];
            _lastPointer = new PointerState(touch.State is TouchLocationState.Pressed or TouchLocationState.Moved, touch.Position);
            return _lastPointer;
        }

        if (now - _lastTouch < TouchMouseGuard)
        {
            return _lastPointer with { IsDown = false };
        }

        var mouse = Mouse.GetState();
        _lastPointer = new PointerState(mouse.LeftButton == ButtonState.Pressed, new Vector2(mouse.X, mouse.Y));
        return _lastPointer;
    }

    /// <summary>The first connected gamepad's state, or the disconnected default. Override to feed scripted input.</summary>
    protected virtual GamePadState ReadGamePad()
    {
        for (var player = PlayerIndex.One; player <= PlayerIndex.Four; player++)
        {
            var state = GamePad.GetState(player);
            if (state.IsConnected)
            {
                return state;
            }
        }

        return default;
    }

    protected override void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        var keyboard = ReadKeyboard();
        var pointer = ReadPointer(gameTime.TotalGameTime);
        var pad = ReadGamePad();

        // The mouse or a finger moving takes the pointer back from the gamepad cursor.
        if (pointer.Position != _realPointer.Position || (pointer.IsDown && !_realPointer.IsDown))
        {
            PadCursor.Deactivate();
        }

        _realPointer = pointer;
        var window = GraphicsDevice.Viewport.Bounds;
        VirtualScreen?.Fit(window);
        PadCursor.Update(pad, gameTime.ElapsedGameTime, VirtualScreen?.Destination ?? window, pointer.Position);
        var cursor = PadCursor.IsActive ? PadCursor.State : pointer;
        if (VirtualScreen is { } screen)
        {
            cursor = cursor with { Position = screen.ToVirtual(cursor.Position) };
        }

        Input.Update(keyboard, cursor, pad);
        _assets?.Update();
        Scenes.Debug.Update(gameTime.ElapsedGameTime, Input);
        var started = _clock.Elapsed;
        Scenes.Update(gameTime.ElapsedGameTime, Input);
        _updateTime += _clock.Elapsed - started;
        if (Scenes.IsEmpty)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var started = _clock.Elapsed;
        var window = GraphicsDevice.Viewport.Bounds;
        VirtualScreen?.Fit(window);
        Scenes.DrawLayers(_batch!, ScreenBounds);
        if (VirtualScreen is not null || Scenes.Post.IsActive)
        {
            DrawOffscreen(window);
        }
        else
        {
            GraphicsDevice.Clear(ClearColor);
            Begin(SamplerState.LinearClamp);
            Scenes.Draw(_batch!, window);
            _batch!.End();
        }

        var drawTime = _clock.Elapsed - started;

        // Above everything, in window pixels: the gamepad cursor and the debug panel.
        Begin(SamplerState.LinearClamp);
        PadCursor.Draw(_batch!, window);
        Scenes.Debug.Draw(_batch!, window, Scenes, Scenes.Debug.IsVisible ? DebugInfo : null);
        _batch!.End();

        Scenes.Debug.Stats.Record(started - _lastDraw, _updateTime, drawTime);
        _lastDraw = started;
        _updateTime = TimeSpan.Zero;

        base.Draw(gameTime);
    }

    /// <summary>Draws the scenes into a target (at the virtual size, or the window's), post-processes it and puts it in the window.</summary>
    private void DrawOffscreen(Rectangle window)
    {
        var bounds = ScreenBounds;
        if (_target is null || _target.Width != bounds.Width || _target.Height != bounds.Height)
        {
            _target?.Dispose();
            _target = RenderLayer.Create(GraphicsDevice, bounds.Size);
        }

        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(ClearColor);
        Begin(SamplerState.LinearClamp);
        Scenes.Draw(_batch!, bounds);
        _batch!.End();
        var image = Scenes.Post.IsActive ? Scenes.Post.Apply(_batch, _target) : _target;
        GraphicsDevice.SetRenderTarget(null);

        // Whole-number scaling keeps pixels square and sharp; any other scale is smoothed. Copied opaque: blending
        // inside the target leaves its alpha below one in places.
        var destination = VirtualScreen?.Destination ?? window;
        var scale = VirtualScreen?.Scale ?? 1;
        GraphicsDevice.Clear(VirtualScreen?.BarColor ?? ClearColor);
        var sampler = scale == MathF.Floor(scale) ? SamplerState.PointClamp : SamplerState.LinearClamp;
        _batch.Begin(blendState: BlendState.Opaque, samplerState: sampler);
        _batch.Draw(image, destination, Color.White);
        _batch.End();
    }

    /// <summary>The game's lines on the debug panel: the window, virtual screen and input. Override to add more.</summary>
    protected virtual IEnumerable<string> DebugInfo
    {
        get
        {
            var c = CultureInfo.InvariantCulture;
            var window = GraphicsDevice.Viewport.Bounds;
            yield return VirtualScreen is { } screen
                ? string.Format(c, "Window {0}x{1}  virtual {2}x{3} {4} x{5:0.00}", window.Width, window.Height, screen.Size.X, screen.Size.Y, screen.Mode, screen.Scale)
                : string.Format(c, "Window {0}x{1}", window.Width, window.Height);
            var cursor = Input.Cursor;
            yield return string.Format(
                c,
                "Pointer {0:0},{1:0}{2}{3}  Pad {4}",
                cursor.Position.X,
                cursor.Position.Y,
                cursor.IsDown ? " down" : "",
                PadCursor.IsActive ? " (pad cursor)" : "",
                Input.Pad.IsConnected ? "connected" : "none");
        }
    }

    // Textures loaded from PNG files are not premultiplied, so blend accordingly.
    private void Begin(SamplerState sampler) => _batch!.Begin(blendState: BlendState.NonPremultiplied, samplerState: sampler);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batch?.Dispose();
            _assets?.Dispose();
            _target?.Dispose();
            Scenes.Post.Dispose();
            Graphics.Dispose();
        }

        base.Dispose(disposing);
    }
}
