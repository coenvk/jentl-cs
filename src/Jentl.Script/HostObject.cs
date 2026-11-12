using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Jentl.Script;

/// <summary>
/// One of the game's objects seen through reflection: its public properties and fields to read and set, its public
/// methods to call, by name without regard to case (<c>player.health</c> is <c>Health</c>). A struct member comes back
/// as a copy; setting a part of it in one go (<c>thing.position.x = 5</c>) puts the changed copy back, but a copy kept
/// in a variable is the script's own.
/// </summary>
internal sealed class HostObject(object target) : IScriptObject
{
    private static readonly Dictionary<Type, Dictionary<string, MemberInfo[]>> Members = [];

    public object Target { get; } = target;

    public ScriptValue GetMember(string name)
    {
        if (Target is IDictionary dictionary && dictionary.Contains(name))
        {
            return ScriptValue.From(dictionary[name]);
        }

        var members = Find(name);
        return members[0] switch
        {
            PropertyInfo property => ScriptValue.From(property.GetValue(Target)),
            FieldInfo field => ScriptValue.From(field.GetValue(Target)),
            _ => new NativeFunction(members[0].Name, call => Invoke(members, call)),
        };
    }

    public void SetMember(string name, ScriptValue value)
    {
        if (Target is IDictionary { IsReadOnly: false } dictionary
            && ItemTypes(Target.GetType(), typeof(IDictionary<,>)) is [var key, var item] && key == typeof(string))
        {
            dictionary[name] = ToClr(value, item, name);
            return;
        }

        switch (Find(name)[0])
        {
            case PropertyInfo { CanWrite: true } property when property.SetMethod!.IsPublic
                && !property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)):
                property.SetValue(Target, ToClr(value, property.PropertyType, property.Name));
                break;
            case FieldInfo { IsInitOnly: false, IsLiteral: false } field:
                field.SetValue(Target, ToClr(value, field.FieldType, field.Name));
                break;
            case MethodInfo method:
                throw new ScriptException($"{method.Name} is a method of {TypeName(Target.GetType())}; call it instead");
            case var member:
                throw new ScriptException($"{member.Name} of {TypeName(Target.GetType())} can't be changed");
        }
    }

    public override string ToString() => Target.ToString() ?? TypeName(Target.GetType());

    /// <summary>An item of a game list, by a script index (negative from the end).</summary>
    internal static ScriptValue GetItem(IList list, ScriptValue index) => ScriptValue.From(list[Operations.Position(index, list.Count, "list")]);

    internal static void SetItem(IList list, ScriptValue index, ScriptValue value)
    {
        var item = ItemTypes(list.GetType(), typeof(IList<>)) is [var type] ? type : typeof(object);
        list[Operations.Position(index, list.Count, "list")] = ToClr(value, item, "a list item");
    }

    /// <summary>A type's name without the generic arity: <c>Dictionary</c>, not <c>Dictionary`2</c>.</summary>
    private static string TypeName(Type type) => type.Name.Split('`')[0];

    private static Type[] ItemTypes(Type type, Type generic) =>
        type.GetInterfaces().FirstOrDefault(face => face.IsGenericType && face.GetGenericTypeDefinition() == generic)?.GetGenericArguments() ?? [];

    /// <summary>Turns a script value into the C# type a member or parameter wants.</summary>
    internal static object? ToClr(ScriptValue value, Type type, string what)
    {
        if (type == typeof(ScriptValue))
        {
            return value;
        }

        if (value.IsNil)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
                ? null
                : throw new ScriptException($"{what} needs {Describe(type)}, got nil");
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        var raw = value.Raw;
        if (type.IsInstanceOfType(raw))
        {
            return raw;
        }

        if (type.IsEnum)
        {
            return value.Type switch
            {
                ScriptType.Text when Enum.TryParse(type, value.AsText, ignoreCase: true, out var choice) => choice,
                ScriptType.Number => Enum.ToObject(type, (long)value.AsNumber),
                _ => throw new ScriptException($"{what} needs one of {string.Join(", ", Enum.GetNames(type))}, got {value.Describe()}"),
            };
        }

        if (value.Type == ScriptType.Number && type.IsPrimitive && type != typeof(bool) && type != typeof(char))
        {
            try
            {
                return Convert.ChangeType(value.AsNumber, type, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                throw new ScriptException($"{what}: {value} doesn't fit in {Describe(type)}");
            }
        }

        if (type == typeof(decimal) && value.Type == ScriptType.Number)
        {
            return (decimal)value.AsNumber;
        }

        if (type == typeof(string))
        {
            return value.ToString();
        }

        throw new ScriptException($"{what} needs {Describe(type)}, got {value.Describe()}");
    }

    private static string Describe(Type type) =>
        type == typeof(string) ? "text"
        : type == typeof(bool) ? "true or false"
        : type.IsPrimitive || type == typeof(decimal) ? "a number"
        : $"a {TypeName(type)}";

    private MemberInfo[] Find(string name)
    {
        var type = Target.GetType();
        Dictionary<string, MemberInfo[]>? members;
        lock (Members)
        {
            if (!Members.TryGetValue(type, out members))
            {
                members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(member => (member is PropertyInfo property && property.GetIndexParameters().Length == 0) || member is FieldInfo
                        || (member is MethodInfo { IsSpecialName: false } method && method.DeclaringType != typeof(object)))
                    .GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
                Members[type] = members;
            }
        }

        return members.TryGetValue(name, out var found)
            ? found
            : throw new ScriptException($"{TypeName(type)} has no member '{name}'");
    }

    private ScriptValue Invoke(MemberInfo[] members, ScriptCall call)
    {
        var methods = members.OfType<MethodInfo>()
            .Where(method => !method.ContainsGenericParameters)
            .Where(method => method.GetParameters() is var parameters
                && call.Count <= parameters.Length
                && parameters.Skip(call.Count).All(parameter => parameter.HasDefaultValue))
            .OrderBy(method => method.GetParameters().Length)
            .ToList();
        ScriptException? failure = null;
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var arguments = new object?[parameters.Length];
            try
            {
                for (var i = 0; i < parameters.Length; i++)
                {
                    arguments[i] = i < call.Count
                        ? ToClr(call[i], parameters[i].ParameterType, $"{method.Name}: argument {i + 1}")
                        : parameters[i].DefaultValue;
                }
            }
            catch (ScriptException mismatch)
            {
                failure ??= mismatch;
                continue;
            }

            try
            {
                var result = method.Invoke(Target, arguments);
                return method.ReturnType == typeof(void) ? ScriptValue.Nil : ScriptValue.From(result);
            }
            catch (TargetInvocationException thrown) when (thrown.InnerException is { } inner)
            {
                throw inner is ScriptException ? inner : new ScriptException($"{method.Name}: {inner.Message}", inner);
            }
        }

        throw failure ?? new ScriptException($"{members[0].Name} of {TypeName(Target.GetType())} doesn't take {call.Count} argument{(call.Count == 1 ? "" : "s")}");
    }
}
