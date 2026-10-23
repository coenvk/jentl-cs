using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core;

/// <summary>
/// Base game: owns the sprite batch, input and scene stack, and runs the loop.
/// Exits when the scene stack becomes empty.
/// </summary>
public abstract class JentlGame : Game
{
    private SpriteBatch? _batch;

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

    protected override void Update(GameTime gameTime)
    {
        ArgumentNullException.ThrowIfNull(gameTime);
        Input.Update(ReadKeyboard());
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

        // Textures loaded with Texture2D.FromFile are not premultiplied, so blend accordingly.
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
