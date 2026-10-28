using System.Globalization;
using System.Text;

namespace Jentl.Script;

/// <summary>What kind of value a <see cref="ScriptValue"/> holds.</summary>
public enum ScriptType
{
    Nil,

    /// <summary>true or false.</summary>
    Logical,
    Number,
    Text,
    List,
    Map,

    /// <summary>A function written in a script or given by the game (<see cref="ScriptCallable"/>).</summary>
    Function,

    /// <summary>One of the game's own objects (<see cref="IScriptObject"/>).</summary>
    Host,
}

/// <summary>
/// A value in a script: nil, true or false, a number (always a double), text, a list, a map, a function or one of the
/// game's objects. Numbers, text and true/false convert to it implicitly.
/// </summary>
public readonly struct ScriptValue : IEquatable<ScriptValue>
{
    private readonly double _number;
    private readonly object? _reference;

    private ScriptValue(ScriptType type, double number, object? reference)
    {
        Type = type;
        _number = number;
        _reference = reference;
    }

    public static ScriptValue Nil => default;

    public static ScriptValue True { get; } = new(ScriptType.Logical, 1, null);

    public static ScriptValue False { get; } = new(ScriptType.Logical, 0, null);

    public ScriptType Type { get; }

    public bool IsNil => Type == ScriptType.Nil;

    /// <summary>Whether an <c>if</c> takes this as yes: everything except nil and false.</summary>
    public bool IsTruthy => Type switch
    {
        ScriptType.Nil => false,
        ScriptType.Logical => _number != 0,
        _ => true,
    };

    public double AsNumber => Type == ScriptType.Number ? _number : throw Mismatch("a number");

    public string AsText => Type == ScriptType.Text ? (string)_reference! : throw Mismatch("text");

    public bool AsLogical => Type == ScriptType.Logical ? _number != 0 : throw Mismatch("true or false");

    public ScriptList AsList => Type == ScriptType.List ? (ScriptList)_reference! : throw Mismatch("a list");

    public ScriptMap AsMap => Type == ScriptType.Map ? (ScriptMap)_reference! : throw Mismatch("a map");

    public ScriptCallable AsFunction => Type == ScriptType.Function ? (ScriptCallable)_reference! : throw Mismatch("a function");

    public IScriptObject AsHost => Type == ScriptType.Host ? (IScriptObject)_reference! : throw Mismatch("an object");

    /// <summary>The C# object behind this value: a double, string, bool, list, map, function, the game's object, or null.</summary>
    public object? Raw => Type switch
    {
        ScriptType.Nil => null,
        ScriptType.Logical => _number != 0,
        ScriptType.Number => _number,
        ScriptType.Host when _reference is HostObject host => host.Target,
        _ => _reference,
    };

    /// <summary>The name of the type as scripts see it: "nil", "bool", "number", "text", "list", "map", "function" or "object".</summary>
    public string TypeName => NameOf(Type);

    public static ScriptValue FromNumber(double value) => new(ScriptType.Number, value, null);

    public static ScriptValue FromText(string value) => new(ScriptType.Text, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static ScriptValue FromLogical(bool value) => value ? True : False;

    public static ScriptValue FromList(ScriptList value) => new(ScriptType.List, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static ScriptValue FromMap(ScriptMap value) => new(ScriptType.Map, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static ScriptValue FromFunction(ScriptCallable value) => new(ScriptType.Function, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static ScriptValue FromHost(IScriptObject value) => new(ScriptType.Host, 0, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>
    /// Turns a C# value into a script value: numbers, text, bools, enums (as their name), script values, lists, maps,
    /// functions and <see cref="IScriptObject"/>s as they are, null as nil, and any other object wrapped so scripts
    /// can read and change its public properties and fields and call its methods.
    /// </summary>
    public static ScriptValue From(object? value) => value switch
    {
        null => Nil,
        ScriptValue script => script,
        bool logical => FromLogical(logical),
        string text => FromText(text),
        char letter => FromText(letter.ToString()),
        Enum choice => FromText(choice.ToString()),
        double number => FromNumber(number),
        float number => FromNumber(number),
        int number => FromNumber(number),
        long number => FromNumber(number),
        short number => FromNumber(number),
        byte number => FromNumber(number),
        uint number => FromNumber(number),
        ulong number => FromNumber(number),
        decimal number => FromNumber((double)number),
        ScriptList list => FromList(list),
        ScriptMap map => FromMap(map),
        ScriptCallable function => FromFunction(function),
        IScriptObject host => FromHost(host),
        _ => FromHost(new HostObject(value)),
    };

    public static implicit operator ScriptValue(double value) => FromNumber(value);

    public static implicit operator ScriptValue(string value) => FromText(value);

    public static implicit operator ScriptValue(bool value) => FromLogical(value);

    public static implicit operator ScriptValue(ScriptList value) => FromList(value);

    public static implicit operator ScriptValue(ScriptMap value) => FromMap(value);

    public static implicit operator ScriptValue(ScriptCallable value) => FromFunction(value);

    public static bool operator ==(ScriptValue left, ScriptValue right) => left.Equals(right);

    public static bool operator !=(ScriptValue left, ScriptValue right) => !left.Equals(right);

    /// <summary>Values are equal when they have the same type and value; text by its characters, lists, maps, functions and objects by identity.</summary>
    public bool Equals(ScriptValue other) => Type == other.Type && Type switch
    {
        ScriptType.Nil => true,
        ScriptType.Logical or ScriptType.Number => _number.Equals(other._number),
        ScriptType.Text => string.Equals((string)_reference!, (string)other._reference!, StringComparison.Ordinal),
        ScriptType.Host when _reference is HostObject mine && other._reference is HostObject theirs => ReferenceEquals(mine.Target, theirs.Target),
        _ => ReferenceEquals(_reference, other._reference),
    };

    public override bool Equals(object? obj) => obj is ScriptValue other && Equals(other);

    public override int GetHashCode() => Type switch
    {
        ScriptType.Nil => 0,
        ScriptType.Logical or ScriptType.Number => _number.GetHashCode(),
        ScriptType.Text => StringComparer.Ordinal.GetHashCode((string)_reference!),
        ScriptType.Host when _reference is HostObject host => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(host.Target),
        _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_reference!),
    };

    /// <summary>The value as a script prints it: text as it is, whole numbers without a point, lists as <c>[1, 2]</c>, maps as <c>{a: 1}</c>.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        Write(text, quote: false, depth: 0);
        return text.ToString();
    }

    internal static string NameOf(ScriptType type) => type switch
    {
        ScriptType.Nil => "nil",
        ScriptType.Logical => "bool",
        ScriptType.Number => "number",
        ScriptType.Text => "text",
        ScriptType.List => "list",
        ScriptType.Map => "map",
        ScriptType.Function => "function",
        _ => "object",
    };

    internal static string FormatNumber(double number) =>
        number == Math.Floor(number) && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("R", CultureInfo.InvariantCulture);

    private void Write(StringBuilder text, bool quote, int depth)
    {
        switch (Type)
        {
            case ScriptType.Nil:
                text.Append("nil");
                break;
            case ScriptType.Logical:
                text.Append(_number != 0 ? "true" : "false");
                break;
            case ScriptType.Number:
                text.Append(FormatNumber(_number));
                break;
            case ScriptType.Text when quote:
                text.Append('"').Append((string)_reference!).Append('"');
                break;
            case ScriptType.Text:
                text.Append((string)_reference!);
                break;
            case ScriptType.List when depth > 4:
                text.Append("[...]");
                break;
            case ScriptType.List:
                text.Append('[');
                var items = ((ScriptList)_reference!).Items;
                for (var i = 0; i < items.Count; i++)
                {
                    text.Append(i == 0 ? "" : ", ");
                    items[i].Write(text, quote: true, depth + 1);
                }

                text.Append(']');
                break;
            case ScriptType.Map when depth > 4:
                text.Append("{...}");
                break;
            case ScriptType.Map:
                text.Append('{');
                var first = true;
                foreach (var (key, value) in ((ScriptMap)_reference!).Entries)
                {
                    text.Append(first ? "" : ", ");
                    first = false;
                    key.Write(text, quote: key.Type != ScriptType.Text || !Lexer.IsName(key.AsText), depth + 1);
                    text.Append(": ");
                    value.Write(text, quote: true, depth + 1);
                }

                text.Append('}');
                break;
            case ScriptType.Function:
                text.Append("fn ").Append(((ScriptCallable)_reference!).Name);
                break;
            default:
                text.Append(_reference);
                break;
        }
    }

    private ScriptException Mismatch(string wanted) => new($"expected {wanted}, got {Describe()}");

    /// <summary>A short description for error messages: the type, with the value for small ones.</summary>
    internal string Describe() => Type switch
    {
        ScriptType.Nil => "nil",
        ScriptType.Logical or ScriptType.Number => $"{TypeName} {this}",
        ScriptType.Text => ((string)_reference!).Length <= 20 ? $"text \"{_reference}\"" : "text",
        _ => TypeName,
    };
}

/// <summary>A script list: zero-based, growing, shared by reference.</summary>
public sealed class ScriptList
{
    public ScriptList()
    {
    }

    public ScriptList(IEnumerable<ScriptValue> items) => Items.AddRange(items);

    public List<ScriptValue> Items { get; } = [];

    public int Count => Items.Count;

    public ScriptValue this[int index]
    {
        get => Items[index];
        set => Items[index] = value;
    }

    public void Add(ScriptValue item) => Items.Add(item);

    public override string ToString() => ScriptValue.FromList(this).ToString();
}

/// <summary>A script map from keys (usually text) to values, keeping the order keys were added in. A missing key reads as nil.</summary>
public sealed class ScriptMap
{
    public Dictionary<ScriptValue, ScriptValue> Entries { get; } = [];

    public int Count => Entries.Count;

    /// <summary>The value at <paramref name="key"/>, or nil; setting nil removes the key.</summary>
    public ScriptValue this[ScriptValue key]
    {
        get => Entries.TryGetValue(key, out var value) ? value : ScriptValue.Nil;
        set
        {
            if (value.IsNil)
            {
                Entries.Remove(key);
            }
            else
            {
                Entries[key] = value;
            }
        }
    }

    public override string ToString() => ScriptValue.FromMap(this).ToString();
}

/// <summary>
/// One of the game's objects as scripts see it: <c>thing.name</c> reads <see cref="GetMember"/>, <c>thing.name = value</c>
/// calls <see cref="SetMember"/>. Objects that don't implement this are wrapped by reflection (<see cref="ScriptValue.From"/>).
/// </summary>
public interface IScriptObject
{
    /// <summary>The member's value; throw a <see cref="ScriptException"/> when there is no such member.</summary>
    ScriptValue GetMember(string name);

    void SetMember(string name, ScriptValue value);
}
