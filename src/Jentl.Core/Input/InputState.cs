using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Keyboard state for the current and previous frame.</summary>
public sealed class InputState
{
    private KeyboardState _previous;
    private KeyboardState _current;

    /// <summary>Reads the keyboard. Call once per frame.</summary>
    public void Update() => Update(Keyboard.GetState());

    /// <summary>Advances to a given keyboard state; lets tests feed input without a window.</summary>
    public void Update(KeyboardState state)
    {
        _previous = _current;
        _current = state;
    }

    public bool IsDown(Keys key) => _current.IsKeyDown(key);

    public bool WasPressed(Keys key) => _current.IsKeyDown(key) && _previous.IsKeyUp(key);
}
