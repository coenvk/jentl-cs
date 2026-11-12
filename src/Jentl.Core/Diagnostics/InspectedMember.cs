using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Diagnostics;

/// <summary>What sort of value an <see cref="InspectedMember"/> holds, which decides how it is shown and edited.</summary>
public enum MemberKind
{
    /// <summary>A bool: toggled.</summary>
    Toggle,

    /// <summary>A whole number: stepped by one (or ten).</summary>
    Whole,

    /// <summary>A float, double or decimal: stepped by a tenth of its size.</summary>
    Number,

    /// <summary>A string: typed.</summary>
    Text,

    /// <summary>An enum: stepped through its values.</summary>
    Choice,

    /// <summary>A length of time: stepped by a tenth of a second.</summary>
    Time,

    /// <summary>Something with members of its own (an object, a struct such as a vector or a colour, a list): opened.</summary>
    Group,

    /// <summary>Anything else: shown, not changed.</summary>
    Other,
}

/// <summary>
/// One value the inspector shows: a property, a field, a list item, or an object at the top. It reads and writes
/// through the object it belongs to, so a member of a struct (a vector's X) writes the changed struct back where it
/// came from. <see cref="Children"/> opens objects, structs and lists into their members: public properties and every
/// field, private ones too, since this is a tool for the game's own developers.
/// </summary>
public sealed class InspectedMember
{
    private const int MaxItems = 200;
    private readonly Func<object?> _get;
    private readonly Action<object?>? _set;

    private InspectedMember(string name, Type type, Func<object?> get, Action<object?>? set)
    {
        Name = name;
        Type = type;
        _get = get;
        _set = set;
        Kind = KindOf(type);
    }

    public string Name { get; }

    /// <summary>The member's declared type (the value's own type may be more specific).</summary>
    public Type Type { get; }

    public MemberKind Kind { get; }

    /// <summary>Whether it can be changed: it has a setter (or is a writable field) and a kind that can be edited.</summary>
    public bool CanWrite => _set is not null && Kind is not (MemberKind.Group or MemberKind.Other);

    /// <summary>A member at the top: <paramref name="target"/> under <paramref name="name"/>, read only.</summary>
    public static InspectedMember Of(string name, object? target) =>
        new(name, target?.GetType() ?? typeof(object), () => target, null);

    /// <summary>A member read and written through functions: something that lives elsewhere, such as an entity's component.</summary>
    public static InspectedMember Of(string name, Type type, Func<object?> get, Action<object?>? set) =>
        new(name, type, get, set);

    /// <summary>A group whose children are the given members, for a list of things to inspect.</summary>
    public static InspectedMember Group(string name, IReadOnlyList<InspectedMember> members) =>
        new(name, typeof(IReadOnlyList<InspectedMember>), () => members, null);

    /// <summary>The value now, or the exception reading it threw.</summary>
    public object? Value
    {
        get
        {
            try
            {
                return _get();
            }
#pragma warning disable CA1031 // A property that throws is shown with its error instead of closing the inspector.
            catch (Exception e)
#pragma warning restore CA1031
            {
                return e is TargetInvocationException { InnerException: { } inner } ? inner : e;
            }
        }
    }

    /// <summary>The value as text: numbers short and invariant, vectors as (x, y), colours as #RRGGBBAA, lists with their count.</summary>
    public string Display => Describe(Value);

    /// <summary>Sets the value; false when it can't be set or the setter refused (threw).</summary>
    public bool Set(object? value)
    {
        if (!CanWrite)
        {
            return false;
        }

        try
        {
            _set!(value);
            return true;
        }
        catch (Exception e) when (e is TargetInvocationException or ArgumentException or InvalidCastException or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Steps the value <paramref name="steps"/> times (negative: down), each step <paramref name="scale"/> times the
    /// normal one: a bool flips, an enum moves to its neighbour, a number by a step suited to its size.
    /// </summary>
    public bool Adjust(int steps, float scale = 1)
    {
        if (!CanWrite || steps == 0)
        {
            return false;
        }

        var value = Value;
        switch (Kind)
        {
            case MemberKind.Toggle when value is bool on:
                return Set(steps % 2 == 0 ? on : !on);
            case MemberKind.Choice when value is Enum:
                var values = Enum.GetValues(Type);
                var index = Array.IndexOf(values, value);
                var next = ((index + steps) % values.Length + values.Length) % values.Length;
                return Set(values.GetValue(next));
            case MemberKind.Whole when value is not null:
                var whole = Convert.ToDouble(value, CultureInfo.InvariantCulture) + steps * Math.Max(1, Math.Round(scale));
                return SetNumber(whole);
            case MemberKind.Number when value is not null:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return SetNumber(number + steps * Step(number) * scale);
            case MemberKind.Time when value is TimeSpan time:
                return Set(time + TimeSpan.FromSeconds(steps * 0.1 * scale));
            default:
                return false;
        }
    }

    /// <summary>Sets the value from typed text (a number, true/false, an enum's name, a time, any string); false when it doesn't parse.</summary>
    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!CanWrite)
        {
            return false;
        }

        var c = CultureInfo.InvariantCulture;
        text = text.Trim();
        return Kind switch
        {
            MemberKind.Text => Set(text),
            MemberKind.Toggle => bool.TryParse(text, out var on) && Set(on),
            MemberKind.Choice => Enum.TryParse(Type, text, ignoreCase: true, out var choice) && Enum.IsDefined(Type, choice!) && Set(choice),
            MemberKind.Whole or MemberKind.Number => double.TryParse(text, NumberStyles.Float, c, out var number) && SetNumber(number),
            MemberKind.Time => ParseTime(text) is { } time && Set(time),
            _ => false,
        };
    }

    /// <summary>"1.5" as seconds, or a time as <see cref="TimeSpan"/> writes it ("00:00:01.5").</summary>
    private static TimeSpan? ParseTime(string text)
    {
        var c = CultureInfo.InvariantCulture;
        if (double.TryParse(text.TrimEnd('s', ' '), NumberStyles.Float, c, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(text, c, out var time) ? time : null;
    }

    /// <summary>
    /// The members inside: an object's properties and fields, a struct's (writing the struct back on a change), a
    /// list's items (the first 200), or a group's members. Empty for simple values and null.
    /// </summary>
    public IReadOnlyList<InspectedMember> Children()
    {
        if (Kind != MemberKind.Group)
        {
            return [];
        }

        var value = Value;
        switch (value)
        {
            case null or Exception:
                return [];
            case IReadOnlyList<InspectedMember> group:
                return group;
            case IList list:
                return Items(list);
            case IEnumerable sequence when value is not string:
                return [.. sequence.Cast<object?>().Take(MaxItems).Select((item, i) => Of($"[{i}]", item))];
        }

        var type = value.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var members = new List<InspectedMember>();
        var seen = new HashSet<string>();
        var settable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead || IsHidden(property.PropertyType) || !seen.Add(property.Name))
            {
                continue;
            }

            var setter = property.SetMethod is { IsPublic: true } ? property : null;
            if (setter is not null)
            {
                settable.Add(property.Name);
            }

            members.Add(Member(property.Name, property.PropertyType, target => property.GetValue(target), setter is null ? null : (target, v) => setter.SetValue(target, v)));
        }

        foreach (var field in type.GetFields(flags))
        {
            // An auto-property's backing field is shown as the property already.
            if (field.Name.StartsWith('<') || IsHidden(field.FieldType))
            {
                continue;
            }

            // "_speed" shows as "speed", unless that name is taken. A field behind a property that can be set
            // ("_mass" behind Mass) is left out: the property shows it, and setting it keeps the class's own rules.
            var name = field.Name.TrimStart('_');
            if (settable.Contains(name))
            {
                continue;
            }

            if (name.Length == 0 || !seen.Add(name))
            {
                name = field.Name;
                if (!seen.Add(name))
                {
                    continue;
                }
            }

            members.Add(Member(name, field.FieldType, field.GetValue, field.IsInitOnly || field.IsLiteral ? null : field.SetValue));
        }

        return members;
    }

    /// <summary>A member of this one's value; for a struct, a change is written to a copy that is then set back here.</summary>
    private InspectedMember Member(string name, Type type, Func<object, object?> read, Action<object, object?>? write)
    {
        var isStruct = Type.IsValueType || Value?.GetType().IsValueType == true;
        Action<object?>? set = null;
        if (write is not null && (!isStruct || _set is not null))
        {
            set = v =>
            {
                var target = Value ?? throw new InvalidOperationException("The object is gone.");
                write(target, ConvertTo(v, type));
                if (isStruct)
                {
                    // The struct was boxed by reading it: put the changed box back where it came from.
                    _set!(target);
                }
            };
        }

        return new InspectedMember(name, type, () => Value is { } target and not Exception ? read(target) : null, set);
    }

    private static List<InspectedMember> Items(IList list)
    {
        var element = list.GetType() is { IsArray: true } array
            ? array.GetElementType()!
            : list.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))?.GetGenericArguments()[0] ?? typeof(object);
        var writable = !list.IsReadOnly;
        return [.. Enumerable.Range(0, Math.Min(list.Count, MaxItems)).Select(i =>
            new InspectedMember($"[{i}]", element, () => i < list.Count ? list[i] : null, writable ? v => list[i] = ConvertTo(v, element) : null))];
    }

    private bool SetNumber(double number)
    {
        try
        {
            var type = Nullable.GetUnderlyingType(Type) ?? Type;
            var value = Kind == MemberKind.Whole ? System.Convert.ChangeType(Math.Round(number), type, CultureInfo.InvariantCulture) : System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
            return Set(value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>A step suited to a number's size: a tenth of its order of magnitude, at least 0.01 (0.1 for 3, 10 for 420).</summary>
    internal static double Step(double value) =>
        Math.Max(0.01, Math.Pow(10, Math.Floor(Math.Log10(Math.Max(Math.Abs(value), 1)))) / 10);

    private static object? ConvertTo(object? value, Type type) =>
        value is null || type.IsInstanceOfType(value) ? value : System.Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type, CultureInfo.InvariantCulture);

    /// <summary>Delegates, events and reflection's own types tell a game's developer nothing and are left out.</summary>
    private static bool IsHidden(Type type) =>
        typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type) || type.IsPointer || type.IsByRefLike;

    private static MemberKind KindOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool))
        {
            return MemberKind.Toggle;
        }

        if (type.IsEnum)
        {
            return MemberKind.Choice;
        }

        if (type == typeof(string))
        {
            return MemberKind.Text;
        }

        if (type == typeof(TimeSpan))
        {
            return MemberKind.Time;
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => MemberKind.Whole,
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => MemberKind.Number,
            TypeCode.Char or TypeCode.DateTime => MemberKind.Other,
            _ when type.IsPrimitive || type == typeof(Type) => MemberKind.Other,
            _ => MemberKind.Group,
        };
    }

    private static string Describe(object? value)
    {
        var c = CultureInfo.InvariantCulture;
        return value switch
        {
            null => "null",
            Exception e => $"<{e.GetType().Name}>",
            string s => $"\"{s}\"",
            float f => f.ToString("0.###", c),
            double d => d.ToString("0.###", c),
            decimal m => m.ToString("0.###", c),
            TimeSpan t => t.TotalSeconds.ToString("0.###", c) + " s",
            Vector2 v => string.Create(c, $"({v.X:0.##}, {v.Y:0.##})"),
            System.Numerics.Vector2 v => string.Create(c, $"({v.X:0.##}, {v.Y:0.##})"),
            System.Numerics.Vector3 v => string.Create(c, $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})"),
            Color color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}",
            Point p => string.Create(c, $"({p.X}, {p.Y})"),
            Rectangle r => string.Create(c, $"({r.X}, {r.Y}) {r.Width}x{r.Height}"),
            IReadOnlyList<InspectedMember> members => $"{members.Count} things",
            ICollection collection => $"{TypeName(value.GetType())} [{collection.Count}]",
            IFormattable formattable => formattable.ToString(null, c),
            _ when value.GetType().IsPrimitive || value is Enum => System.Convert.ToString(value, c) ?? "",
            _ => TypeName(value.GetType()),
        };
    }

    /// <summary>A type's name as written in C#: <c>List&lt;Body&gt;</c>, not <c>List`1</c>.</summary>
    private static string TypeName(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>" : type.Name;
}
