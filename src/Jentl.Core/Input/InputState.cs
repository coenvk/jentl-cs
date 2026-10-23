using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Keyboard and pointer state for the current frame, with the previous frame for edges.</summary>
public sealed class InputState
{
    private KeyboardState _previous;
    private KeyboardState _current;
    private PointerState _pointer;
    private PointerState _previousPointer;

    public GestureTracker Gestures { get; } = new();

    /// <summary>Where the pointer is, and whether it is held down.</summary>
    public PointerState Cursor => _pointer;

    /// <summary>The pointer moved since the previous frame (mouse hover, or a drag).</summary>
    public bool PointerMoved => _pointer.Position != _previousPointer.Position;

    /// <summary>A tap or click released this frame, and where.</summary>
    public Vector2? Tap => Gestures.Tap;

    /// <summary>A swipe step completed this frame.</summary>
    public SwipeDirection? Swipe => Gestures.Swipe;

    /// <summary>Advances to a keyboard state with the pointer released; lets tests feed input without a window.</summary>
    public void Update(KeyboardState state) => Update(state, _pointer with { IsDown = false });

    /// <summary>Advances one frame. Call once per frame.</summary>
    public void Update(KeyboardState keyboard, PointerState cursor)
    {
        _previous = _current;
        _current = keyboard;
        _previousPointer = _pointer;
        _pointer = cursor;
        Gestures.Update(cursor);
    }

    public bool IsDown(Keys key) => _current.IsKeyDown(key);

    public bool WasPressed(Keys key) => _current.IsKeyDown(key) && _previous.IsKeyUp(key);

    /// <summary>A tap landed inside <paramref name="bounds"/> this frame.</summary>
    public bool Tapped(Rectangle bounds) => Tap is { } at && bounds.Contains(at);
}
