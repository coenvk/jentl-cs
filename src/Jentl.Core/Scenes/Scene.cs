using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Scenes;

/// <summary>
/// One screen of the game (menu, level, pause overlay, ...). Scenes live on a <see cref="SceneManager"/>;
/// only the top scene is updated.
/// </summary>
/// <remarks>A scene is disposed when it is removed from the stack, and is not pushed again after that.</remarks>
public abstract class Scene : IDisposable
{
    private SceneManager? _manager;

    /// <summary>When true, the scene below is drawn first (e.g. a pause menu over the level).</summary>
    public virtual bool IsOverlay => false;

    /// <summary>The manager this scene is on. Only available between <see cref="OnEnter"/> and <see cref="OnExit"/>.</summary>
    protected SceneManager Scenes => _manager ?? throw new InvalidOperationException("Scene is not on a scene manager.");

    public abstract void Update(TimeSpan elapsed, InputState input);

    /// <summary>Draws into an already begun sprite batch.</summary>
    public abstract void Draw(SpriteBatch batch, Rectangle viewport);

    /// <summary>Called when the scene is put on the stack.</summary>
    protected internal virtual void OnEnter()
    {
    }

    /// <summary>Called when the scene is removed from the stack.</summary>
    protected internal virtual void OnExit()
    {
    }

    /// <summary>Called when another scene is pushed on top of this one.</summary>
    protected internal virtual void OnCovered()
    {
    }

    /// <summary>Called when the scene on top of this one is popped.</summary>
    protected internal virtual void OnUncovered()
    {
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    internal void Attach(SceneManager? manager) => _manager = manager;
}
