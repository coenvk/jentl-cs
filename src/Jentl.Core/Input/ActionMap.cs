using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Maps game actions to keys, so game code never looks at raw keys.</summary>
public sealed class ActionMap<TAction>
    where TAction : struct, Enum
{
    private readonly Dictionary<TAction, Keys[]> _bindings;

    public ActionMap(IReadOnlyDictionary<TAction, Keys[]> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings.ToDictionary(b => b.Key, b => b.Value.ToArray());
    }

    public IReadOnlyList<Keys> KeysFor(TAction action) =>
        _bindings.TryGetValue(action, out var keys) ? keys : [];

    public bool IsDown(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KeysFor(action).Any(input.IsDown);
    }

    public bool WasPressed(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KeysFor(action).Any(input.WasPressed);
    }
}
