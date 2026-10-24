using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Maps game actions to keys and gamepad buttons, so game code never looks at raw input.</summary>
public sealed class ActionMap<TAction>
    where TAction : struct, Enum
{
    private readonly Dictionary<TAction, Keys[]> _keys;
    private readonly Dictionary<TAction, Buttons[]> _buttons;

    public ActionMap(IReadOnlyDictionary<TAction, Keys[]> keys, IReadOnlyDictionary<TAction, Buttons[]>? buttons = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys.ToDictionary(b => b.Key, b => b.Value.ToArray());
        _buttons = buttons?.ToDictionary(b => b.Key, b => b.Value.ToArray()) ?? [];
    }

    public IReadOnlyList<Keys> KeysFor(TAction action) =>
        _keys.TryGetValue(action, out var keys) ? keys : [];

    public IReadOnlyList<Buttons> ButtonsFor(TAction action) =>
        _buttons.TryGetValue(action, out var buttons) ? buttons : [];

    public bool IsDown(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KeysFor(action).Any(input.IsDown) || ButtonsFor(action).Any(input.IsDown);
    }

    public bool WasPressed(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KeysFor(action).Any(input.WasPressed) || ButtonsFor(action).Any(input.WasPressed);
    }
}
