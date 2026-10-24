using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Keyboard, pointer and gamepad state for the current frame, with the previous frame for edges.</summary>
public sealed class InputState
{
    private KeyboardState _previous;
    private KeyboardState _current;
    private PointerState _pointer;
    private PointerState _previousPointer;
    private GamePadState _pad;
    private GamePadState _previousPad;

    public GestureTracker Gestures { get; } = new();

    /// <summary>Where the pointer is, and whether it is held down.</summary>
    public PointerState Cursor => _pointer;

    /// <summary>The pointer moved since the previous frame (mouse hover, or a drag).</summary>
    public bool PointerMoved => _pointer.Position != _previousPointer.Position;

    /// <summary>A tap or click released this frame, and where.</summary>
    public Vector2? Tap => Gestures.Tap;

    /// <summary>A swipe step completed this frame.</summary>
    public SwipeDirection? Swipe => Gestures.Swipe;

    /// <summary>The gamepad this frame (the default, disconnected state when there is none).</summary>
    public GamePadState Pad => _pad;

    /// <summary>Advances to a keyboard state with the pointer released; lets tests feed input without a window.</summary>
    public void Update(KeyboardState state) => Update(state, _pointer with { IsDown = false });

    /// <summary>Advances to a gamepad state with no keys and the pointer released; lets tests feed input without a window.</summary>
    public void Update(GamePadState pad) => Update(default, _pointer with { IsDown = false }, pad);

    /// <summary>Advances one frame. Call once per frame.</summary>
    public void Update(KeyboardState keyboard, PointerState cursor, GamePadState pad = default)
    {
        _previous = _current;
        _current = keyboard;
        _previousPointer = _pointer;
        _pointer = cursor;
        _previousPad = _pad;
        _pad = pad;
        Gestures.Update(cursor);
    }

    public bool IsDown(Keys key) => _current.IsKeyDown(key);

    public bool WasPressed(Keys key) => _current.IsKeyDown(key) && _previous.IsKeyUp(key);

    /// <summary>
    /// A gamepad button is held. The thumbstick directions (<see cref="Buttons.LeftThumbstickUp"/>...) and
    /// triggers count as buttons too, past their dead zone.
    /// </summary>
    public bool IsDown(Buttons button) => _pad.IsButtonDown(button);

    public bool WasPressed(Buttons button) => _pad.IsButtonDown(button) && _previousPad.IsButtonUp(button);

    /// <summary>A tap landed inside <paramref name="bounds"/> this frame.</summary>
    public bool Tapped(Rectangle bounds) => Tap is { } at && bounds.Contains(at);
}
