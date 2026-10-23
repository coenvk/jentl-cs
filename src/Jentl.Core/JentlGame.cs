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

    protected GraphicsDeviceManager Graphics { get; }

    protected virtual Color ClearColor => Color.Black;

    /// <summary>Renders the current backbuffer to a PNG file.</summary>
    public void SaveScreenshot(string path)
    {
        var parameters = GraphicsDevice.PresentationParameters;
        var (width, height) = (parameters.BackBufferWidth, parameters.BackBufferHeight);
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        using var file = File.Create(path);
        texture.SaveAsPng(file, width, height);
    }

    protected override void LoadContent() => _batch = new SpriteBatch(GraphicsDevice);

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

    protected override void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        Input.Update(ReadKeyboard(), ReadPointer(gameTime.TotalGameTime));
        Scenes.Update(gameTime.ElapsedGameTime, Input);
        if (Scenes.IsEmpty)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(ClearColor);

        // Textures loaded from PNG files are not premultiplied, so blend accordingly.
        _batch!.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
        Scenes.Draw(_batch, GraphicsDevice.Viewport.Bounds);
        _batch.End();

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batch?.Dispose();
            Graphics.Dispose();
        }

        base.Dispose(disposing);
    }
}
