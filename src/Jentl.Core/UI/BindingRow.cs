using Jentl.Core.Input;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.UI;

/// <summary>Whether a <see cref="BindingRow{TAction}"/> binds a key or a gamepad button.</summary>
public enum BindingDevice
{
    Keyboard,
    GamePad,
}

/// <summary>
/// A control setting: shows the key (or button) in one slot of an action and, chosen, waits for the next one pressed
/// to bind there (<see cref="ActionMap{TAction}.Bind(TAction, Keys, int)"/>: a key taken from another action leaves
/// it). Escape, or Back on the gamepad, cancels; Backspace or Delete clears the slot. <paramref name="changed"/> runs
/// after every change, to save the bindings.
/// </summary>
public sealed class BindingRow<TAction>(
    string label,
    ActionMap<TAction> map,
    TAction action,
    int slot = 0,
    BindingDevice device = BindingDevice.Keyboard,
    Action? changed = null)
    : FormRow(label)
    where TAction : struct, Enum
{
    private readonly ActionMap<TAction> _map = map ?? throw new ArgumentNullException(nameof(map));

    public TAction Action { get; } = action;

    public int Slot { get; } = slot;

    public BindingDevice Device { get; } = device;

    /// <summary>Waiting for a key or button to bind.</summary>
    public bool IsCapturing { get; private set; }

    internal override bool IsEditing => IsCapturing;

    internal override string Value
    {
        get
        {
            if (IsCapturing)
            {
                return Device == BindingDevice.Keyboard ? UiText.PressKey : UiText.PressButton;
            }

            if (Device == BindingDevice.Keyboard)
            {
                var keys = _map.KeysFor(Action);
                return Slot < keys.Count ? InputNames.Of(keys[Slot]) : UiText.Unbound;
            }

            var buttons = _map.ButtonsFor(Action);
            return Slot < buttons.Count ? InputNames.Of(buttons[Slot]) : UiText.Unbound;
        }
    }

    internal override void Activate() => IsCapturing = true;

    internal override void Edit(InputState input)
    {
        if (input.WasPressed(Keys.Escape) || input.WasPressed(Buttons.Back) || input.Tap is not null)
        {
            IsCapturing = false;
            return;
        }

        if (Device == BindingDevice.Keyboard)
        {
            if (input.WasPressed(Keys.Back) || input.WasPressed(Keys.Delete))
            {
                _map.UnbindKey(Action, Slot);
                Done();
            }
            else if (input.PressedKey() is { } key)
            {
                _map.Bind(Action, key, Slot);
                Done();
            }
        }
        else if (input.PressedButton() is { } button)
        {
            _map.Bind(Action, button, Slot);
            Done();
        }
    }

    private void Done()
    {
        IsCapturing = false;
        changed?.Invoke();
    }
}
