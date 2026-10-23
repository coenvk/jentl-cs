namespace Jentl.Core.Input;

/// <summary>
/// Turns held keys into repeated actions, like a keyboard's auto-repeat.
/// Only the most recently pressed action repeats, so switching direction mid-hold feels right.
/// </summary>
public sealed class ActionRepeater<TAction>
    where TAction : struct, Enum
{
    private readonly TAction[] _actions;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private TAction? _held;
    private TimeSpan _untilNext;

    public ActionRepeater(TimeSpan initialDelay, TimeSpan interval, params TAction[] actions)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive.");
        }

        _actions = actions;
        _initialDelay = initialDelay;
        _interval = interval;
    }

    /// <summary>Returns the action that fires this frame, if any.</summary>
    public TAction? Update(InputState input, ActionMap<TAction> map, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var action in _actions)
        {
            if (map.WasPressed(input, action))
            {
                _held = action;
                _untilNext = _initialDelay;
                return action;
            }
        }

        if (_held is not { } held)
        {
            return null;
        }

        if (!map.IsDown(input, held))
        {
            _held = null;
            return null;
        }

        _untilNext -= elapsed;
        if (_untilNext > TimeSpan.Zero)
        {
            return null;
        }

        _untilNext += _interval;
        return held;
    }

    /// <summary>Forgets the held action, e.g. when the scene using it is left.</summary>
    public void Reset() => _held = null;
}
