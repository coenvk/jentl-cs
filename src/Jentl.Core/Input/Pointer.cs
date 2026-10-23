using Microsoft.Xna.Framework;

namespace Jentl.Core.Input;

/// <summary>One pointer (mouse or first touch) for one frame, in backbuffer pixels.</summary>
public readonly record struct PointerState(bool IsDown, Vector2 Position);

public enum SwipeDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Turns raw pointer frames into taps and swipes. A tap is a press and release that barely moves.
/// A swipe fires as soon as a held pointer has travelled <see cref="SwipeDistance"/> along one axis,
/// and again for every further <see cref="SwipeDistance"/>, so dragging steps repeatedly.
/// </summary>
public sealed class GestureTracker
{
    private Vector2 _anchor;
    private Vector2 _pressedAt;
    private bool _swiped;
    private bool _wasDown;

    /// <summary>Largest movement, in pixels, that still counts as a tap.</summary>
    public float TapRadius { get; set; } = 16;

    /// <summary>Distance, in pixels, a drag must cover for each swipe step.</summary>
    public float SwipeDistance { get; set; } = 48;

    public Vector2? Tap { get; private set; }

    public SwipeDirection? Swipe { get; private set; }

    /// <summary>Pressed down this frame.</summary>
    public bool Pressed { get; private set; }

    /// <summary>Where the current or last press started.</summary>
    public Vector2 PressedAt => _pressedAt;

    public void Update(PointerState cursor)
    {
        Tap = null;
        Swipe = null;
        Pressed = cursor.IsDown && !_wasDown;

        if (Pressed)
        {
            _anchor = _pressedAt = cursor.Position;
            _swiped = false;
        }
        else if (cursor.IsDown)
        {
            var delta = cursor.Position - _anchor;
            if (MathF.Abs(delta.X) >= MathF.Abs(delta.Y) && MathF.Abs(delta.X) >= SwipeDistance)
            {
                Swipe = delta.X > 0 ? SwipeDirection.Right : SwipeDirection.Left;
                _anchor.X += MathF.CopySign(SwipeDistance, delta.X);
                _anchor.Y = cursor.Position.Y;
                _swiped = true;
            }
            else if (MathF.Abs(delta.Y) > MathF.Abs(delta.X) && MathF.Abs(delta.Y) >= SwipeDistance)
            {
                Swipe = delta.Y > 0 ? SwipeDirection.Down : SwipeDirection.Up;
                _anchor.Y += MathF.CopySign(SwipeDistance, delta.Y);
                _anchor.X = cursor.Position.X;
                _swiped = true;
            }
        }
        else if (_wasDown && !_swiped && Vector2.Distance(cursor.Position, _pressedAt) <= TapRadius)
        {
            Tap = cursor.Position;
        }

        _wasDown = cursor.IsDown;
    }
}
