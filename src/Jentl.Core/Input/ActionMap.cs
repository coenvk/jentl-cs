using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>
/// Maps game actions to keys and gamepad buttons, so game code never looks at raw input. Players can rebind them:
/// <see cref="Bind(TAction, Keys, int)"/> puts a key in one of an action's slots (taking it from any other action),
/// <see cref="Reset()"/> goes back to the bindings the map was made with, and <see cref="Save"/> and
/// <see cref="Load"/> keep the changes in a settings file.
/// </summary>
public sealed class ActionMap<TAction>
    where TAction : struct, Enum
{
    private readonly Dictionary<TAction, Keys[]> _defaultKeys;
    private readonly Dictionary<TAction, Buttons[]> _defaultButtons;
    private readonly Dictionary<TAction, Keys[]> _keys;
    private readonly Dictionary<TAction, Buttons[]> _buttons;
    private readonly HashSet<TAction> _toggles = [];
    private readonly Dictionary<TAction, (bool On, long Frame)> _latched = [];

    public ActionMap(IReadOnlyDictionary<TAction, Keys[]> keys, IReadOnlyDictionary<TAction, Buttons[]>? buttons = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _defaultKeys = keys.ToDictionary(b => b.Key, b => b.Value.ToArray());
        _defaultButtons = buttons?.ToDictionary(b => b.Key, b => b.Value.ToArray()) ?? [];
        _keys = Copy(_defaultKeys);
        _buttons = Copy(_defaultButtons);
    }

    private ActionMap(ActionMap<TAction> other)
    {
        _defaultKeys = Copy(other._defaultKeys);
        _defaultButtons = Copy(other._defaultButtons);
        _keys = Copy(other._keys);
        _buttons = Copy(other._buttons);
        _toggles.UnionWith(other._toggles);
    }

    public IReadOnlyList<Keys> KeysFor(TAction action) =>
        _keys.TryGetValue(action, out var keys) ? keys : [];

    public IReadOnlyList<Buttons> ButtonsFor(TAction action) =>
        _buttons.TryGetValue(action, out var buttons) ? buttons : [];

    /// <summary>Whether any binding differs from the ones the map was made with.</summary>
    public bool IsChanged => _toggles.Count > 0 || Enum.GetValues<TAction>().Any(a => KeysChanged(a) || ButtonsChanged(a));

    /// <summary>
    /// Whether the action is held; for a toggle (<see cref="SetToggle"/>), whether it is switched on. A toggle flips
    /// when its key is pressed in a frame this is asked, so ask every frame, as for a held action.
    /// </summary>
    public bool IsDown(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!_toggles.Contains(action))
        {
            return KeysFor(action).Any(input.IsDown) || ButtonsFor(action).Any(input.IsDown);
        }

        var (on, frame) = _latched.GetValueOrDefault(action);
        if (frame != input.Frame)
        {
            on ^= WasPressed(input, action);
            _latched[action] = (on, input.Frame);
        }

        return on;
    }

    /// <summary>
    /// Makes a held action (run, aim, crouch) a toggle, or a held one again: pressed once it stays down until pressed
    /// again, for players who cannot hold a key or button for long.
    /// </summary>
    public void SetToggle(TAction action, bool toggle)
    {
        _latched.Remove(action);
        if (toggle)
        {
            _toggles.Add(action);
        }
        else
        {
            _toggles.Remove(action);
        }
    }

    public bool IsToggle(TAction action) => _toggles.Contains(action);

    /// <summary>Switches every toggled action off, as when a level restarts.</summary>
    public void ReleaseToggles() => _latched.Clear();

    public bool WasPressed(InputState input, TAction action)
    {
        ArgumentNullException.ThrowIfNull(input);
        return KeysFor(action).Any(input.WasPressed) || ButtonsFor(action).Any(input.WasPressed);
    }

    /// <summary>
    /// A map with the same bindings and defaults that changes on its own: rebind a copy of a shared map
    /// (<c>UiKeys.Default</c>, a game's <c>Controls.Default</c>), not the map itself.
    /// </summary>
    public ActionMap<TAction> Copy() => new(this);

    /// <summary>
    /// Binds <paramref name="key"/> to <paramref name="action"/> in <paramref name="slot"/> (0 is the main key, 1 the
    /// alternative...), replacing the key there or adding it after the last. The key leaves any other action or slot
    /// it was bound to, so one key never does two things.
    /// </summary>
    /// <returns>The actions that lost the key.</returns>
    public IReadOnlyList<TAction> Bind(TAction action, Keys key, int slot = 0) => Bind(_keys, action, key, slot);

    /// <summary><see cref="Bind(TAction, Keys, int)"/> for a gamepad button.</summary>
    public IReadOnlyList<TAction> Bind(TAction action, Buttons button, int slot = 0) => Bind(_buttons, action, button, slot);

    /// <summary>Removes the key in <paramref name="slot"/> of <paramref name="action"/>, if there is one.</summary>
    public void UnbindKey(TAction action, int slot) => Unbind(_keys, action, slot);

    /// <summary>Removes the button in <paramref name="slot"/> of <paramref name="action"/>, if there is one.</summary>
    public void UnbindButton(TAction action, int slot) => Unbind(_buttons, action, slot);

    /// <summary>Goes back to the bindings the map was made with, every action held rather than toggled.</summary>
    public void Reset()
    {
        _keys.Clear();
        _buttons.Clear();
        _toggles.Clear();
        _latched.Clear();
        foreach (var (action, keys) in _defaultKeys)
        {
            _keys[action] = keys.ToArray();
        }

        foreach (var (action, buttons) in _defaultButtons)
        {
            _buttons[action] = buttons.ToArray();
        }
    }

    /// <summary>
    /// The bindings that differ from the defaults, as JSON (<c>{"keys":{"Jump":["J"]},"buttons":{}}</c>) for
    /// <see cref="Load"/>. Actions left alone are not written, so they follow the game's defaults when those change.
    /// </summary>
    public string Save()
    {
        var keys = new JsonObject();
        var buttons = new JsonObject();
        foreach (var action in Enum.GetValues<TAction>())
        {
            if (KeysChanged(action))
            {
                keys[action.ToString()] = new JsonArray([.. KeysFor(action).Select(k => JsonValue.Create(k.ToString()))]);
            }

            if (ButtonsChanged(action))
            {
                buttons[action.ToString()] = new JsonArray([.. ButtonsFor(action).Select(b => JsonValue.Create(b.ToString()))]);
            }
        }

        var root = new JsonObject { ["keys"] = keys, ["buttons"] = buttons };
        if (_toggles.Count > 0)
        {
            root["toggles"] = new JsonArray([.. _toggles.Order().Select(a => JsonValue.Create(a.ToString()))]);
        }

        return root.ToJsonString();
    }

    /// <summary>
    /// Resets, then applies bindings written by <see cref="Save"/>. Actions, keys and buttons the game no longer
    /// has are skipped, so an old settings file still loads.
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not bindings JSON.</exception>
    public void Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Bindings must be a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Bindings are not valid JSON: {e.Message}", e);
        }

        Reset();
        Apply(root["keys"], _keys);
        Apply(root["buttons"], _buttons);
        if (root["toggles"] is JsonArray toggles)
        {
            foreach (var item in toggles)
            {
                if (item is JsonValue v && v.TryGetValue<string>(out var name) && Enum.TryParse<TAction>(name, out var action))
                {
                    _toggles.Add(action);
                }
            }
        }
    }

    private static void Apply<T>(JsonNode? node, Dictionary<TAction, T[]> bindings)
        where T : struct, Enum
    {
        if (node is not JsonObject actions)
        {
            return;
        }

        foreach (var (name, list) in actions)
        {
            if (!Enum.TryParse<TAction>(name, out var action) || list is not JsonArray items)
            {
                continue;
            }

            bindings[action] = [.. items
                .Select(i => i is JsonValue v && v.TryGetValue<string>(out var s) && Enum.TryParse<T>(s, out var parsed) ? parsed : (T?)null)
                .OfType<T>()
                .Distinct()];
        }
    }

    private static List<TAction> Bind<T>(Dictionary<TAction, T[]> bindings, TAction action, T input, int slot)
        where T : struct, Enum
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        List<TAction> lost = [];
        var list = bindings.TryGetValue(action, out var current) ? current.ToList() : [];
        if (slot < list.Count)
        {
            list[slot] = input;
        }
        else
        {
            list.Add(input);
            slot = list.Count - 1;
        }

        // The same key in another slot of this action goes, keeping the slot just set.
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (i != slot && EqualityComparer<T>.Default.Equals(list[i], input))
            {
                list.RemoveAt(i);
            }
        }

        foreach (var (other, inputs) in bindings.ToList())
        {
            if (!EqualityComparer<TAction>.Default.Equals(other, action) && inputs.Contains(input))
            {
                bindings[other] = [.. inputs.Where(i => !EqualityComparer<T>.Default.Equals(i, input))];
                lost.Add(other);
            }
        }

        bindings[action] = [.. list];
        return lost;
    }

    private static void Unbind<T>(Dictionary<TAction, T[]> bindings, TAction action, int slot)
    {
        if (bindings.TryGetValue(action, out var inputs) && slot >= 0 && slot < inputs.Length)
        {
            bindings[action] = [.. inputs.Take(slot), .. inputs.Skip(slot + 1)];
        }
    }

    private static Dictionary<TAction, T[]> Copy<T>(Dictionary<TAction, T[]> bindings) =>
        bindings.ToDictionary(b => b.Key, b => b.Value.ToArray());

    private bool KeysChanged(TAction action) =>
        !KeysFor(action).SequenceEqual(_defaultKeys.TryGetValue(action, out var keys) ? keys : []);

    private bool ButtonsChanged(TAction action) =>
        !ButtonsFor(action).SequenceEqual(_defaultButtons.TryGetValue(action, out var buttons) ? buttons : []);
}
