using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>
/// A pointer steered with a gamepad's right stick and pressed with the right trigger (or by clicking the stick),
/// so screens made for the mouse or touch (dragging cards, tapping a map) also work from the sofa. It wakes up
/// where the real pointer last was and goes to sleep again when the mouse or a finger takes over.
/// </summary>
public sealed class PadCursor
{
    /// <summary>How far the stick must tilt (0 to 1) before the cursor moves.</summary>
    public float DeadZone { get; set; } = 0.2f;

    /// <summary>Speed at full tilt, in screen heights a second. Smaller tilts go slower than linearly, for aiming.</summary>
    public float Speed { get; set; } = 1.1f;

    /// <summary>How far the right trigger must be pulled (0 to 1) to press.</summary>
    public float TriggerThreshold { get; set; } = 0.4f;

    /// <summary>The cursor is in charge of the pointer: the stick or trigger was used since the mouse or a touch last was.</summary>
    public bool IsActive { get; private set; }

    public Vector2 Position { get; private set; }

    public bool IsDown { get; private set; }

    /// <summary>The pointer this cursor stands for.</summary>
    public PointerState State => new(IsDown, Position);

    /// <summary>
    /// Moves and presses for one frame within <paramref name="bounds"/>. When the cursor wakes up it starts at
    /// <paramref name="from"/>, the real pointer's position.
    /// </summary>
    public void Update(GamePadState pad, TimeSpan elapsed, Rectangle bounds, Vector2 from)
    {
        // Sticks point up for positive Y; the screen counts down.
        var stick = pad.ThumbSticks.Right * new Vector2(1, -1);
        var tilt = stick.Length();
        var pressed = pad.Triggers.Right > TriggerThreshold || pad.IsButtonDown(Buttons.RightStick);
        if (!IsActive && (tilt > DeadZone || pressed))
        {
            IsActive = true;
            Position = from;
        }

        if (!IsActive)
        {
            return;
        }

        if (tilt > DeadZone)
        {
            var amount = MathF.Min(1, (tilt - DeadZone) / (1 - DeadZone));
            Position += stick / tilt * amount * amount * Speed * bounds.Height * (float)elapsed.TotalSeconds;
        }

        Position = Vector2.Clamp(Position, new Vector2(bounds.Left, bounds.Top), new Vector2(bounds.Right - 1, bounds.Bottom - 1));
        IsDown = pressed;
    }

    /// <summary>Hands the pointer back to the mouse or touch.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsDown = false;
    }

    /// <summary>Draws the cursor as a ring with a shadow, smaller while pressed; nothing while inactive.</summary>
    public void Draw(SpriteBatch batch, Rectangle viewport)
    {
        if (!IsActive)
        {
            return;
        }

        var radius = MathF.Max(8, viewport.Height / 70f) * (IsDown ? 0.8f : 1);
        batch.FillCircle(Position + new Vector2(2, 3), radius * 1.15f, Color.Black * 0.35f);
        batch.FillCircle(Position, radius * 1.15f, new Color(30, 30, 40));
        batch.FillCircle(Position, radius, Color.White);
        batch.FillCircle(Position, radius * 0.35f, new Color(30, 30, 40));
    }
}
