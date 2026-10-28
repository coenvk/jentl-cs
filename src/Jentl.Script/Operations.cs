namespace Jentl.Script;

/// <summary>What operators, fields and indexes do to values.</summary>
internal static class Operations
{
    public static ScriptValue Binary(OpCode op, ScriptValue left, ScriptValue right)
    {
        if (left.Type == ScriptType.Number && right.Type == ScriptType.Number)
        {
            var a = left.AsNumber;
            var b = right.AsNumber;
            return op switch
            {
                OpCode.Add => a + b,
                OpCode.Subtract => a - b,
                OpCode.Multiply => a * b,
                OpCode.Divide => b == 0 ? throw new ScriptException("division by zero") : a / b,
                OpCode.Modulo => b == 0 ? throw new ScriptException("division by zero") : a - (b * Math.Floor(a / b)),
                OpCode.Less => a < b,
                OpCode.LessEqual => a <= b,
                OpCode.Greater => a > b,
                _ => a >= b,
            };
        }

        if (op == OpCode.Add)
        {
            if (left.Type == ScriptType.Text || right.Type == ScriptType.Text)
            {
                return string.Concat(left.ToString(), right.ToString());
            }

            if (left.Type == ScriptType.List && right.Type == ScriptType.List)
            {
                return new ScriptList(left.AsList.Items.Concat(right.AsList.Items));
            }
        }

        if (left.Type == ScriptType.Text && right.Type == ScriptType.Text && op is OpCode.Less or OpCode.LessEqual or OpCode.Greater or OpCode.GreaterEqual)
        {
            var order = string.CompareOrdinal(left.AsText, right.AsText);
            return op switch
            {
                OpCode.Less => order < 0,
                OpCode.LessEqual => order <= 0,
                OpCode.Greater => order > 0,
                _ => order >= 0,
            };
        }

        var verb = op switch
        {
            OpCode.Add => "add",
            OpCode.Subtract => "subtract",
            OpCode.Multiply => "multiply",
            OpCode.Divide or OpCode.Modulo => "divide",
            _ => "compare",
        };
        return verb == "compare"
            ? throw new ScriptException($"can't compare {left.Describe()} with {right.Describe()}")
            : throw new ScriptException($"can't {verb} {left.Describe()} and {right.Describe()}");
    }

    public static ScriptValue GetField(ScriptValue target, string name) => target.Type switch
    {
        ScriptType.Map => target.AsMap[name],
        ScriptType.Host => target.AsHost.GetMember(name),
        _ => throw new ScriptException($"can't read field '{name}' of {target.Describe()}"),
    };

    public static void SetField(ScriptValue target, string name, ScriptValue value)
    {
        switch (target.Type)
        {
            case ScriptType.Map:
                target.AsMap[name] = value;
                break;
            case ScriptType.Host:
                target.AsHost.SetMember(name, value);
                break;
            default:
                throw new ScriptException($"can't set field '{name}' of {target.Describe()}");
        }
    }

    public static ScriptValue GetIndex(ScriptValue target, ScriptValue key)
    {
        switch (target.Type)
        {
            case ScriptType.List:
                var items = target.AsList.Items;
                return items[Position(key, items.Count, "list")];
            case ScriptType.Text:
                var text = target.AsText;
                return text[Position(key, text.Length, "text")].ToString();
            case ScriptType.Map:
                return target.AsMap[Key(key)];
            case ScriptType.Host when key.Type == ScriptType.Text:
                return target.AsHost.GetMember(key.AsText);
            case ScriptType.Host when target.Raw is System.Collections.IList list:
                return HostObject.GetItem(list, key);
            default:
                throw new ScriptException($"can't index {target.Describe()}");
        }
    }

    public static void SetIndex(ScriptValue target, ScriptValue key, ScriptValue value)
    {
        switch (target.Type)
        {
            case ScriptType.List:
                var items = target.AsList.Items;
                items[Position(key, items.Count, "list")] = value;
                break;
            case ScriptType.Map:
                target.AsMap[Key(key)] = value;
                break;
            case ScriptType.Host when key.Type == ScriptType.Text:
                target.AsHost.SetMember(key.AsText, value);
                break;
            case ScriptType.Host when target.Raw is System.Collections.IList list:
                HostObject.SetItem(list, key, value);
                break;
            default:
                throw new ScriptException($"can't set an index of {target.Describe()}");
        }
    }

    /// <summary>A map key: anything but nil (and NaN).</summary>
    public static ScriptValue Key(ScriptValue key) =>
        key.IsNil || (key.Type == ScriptType.Number && double.IsNaN(key.AsNumber))
            ? throw new ScriptException($"{key.Describe()} can't be a map key")
            : key;

    /// <summary>A zero-based index; negative ones count from the end (-1 is the last).</summary>
    public static int Position(ScriptValue index, int count, string what)
    {
        if (index.Type != ScriptType.Number || index.AsNumber != Math.Floor(index.AsNumber))
        {
            throw new ScriptException($"a {what} index should be a whole number, got {index.Describe()}");
        }

        var position = index.AsNumber < 0 ? count + index.AsNumber : index.AsNumber;
        return position >= 0 && position < count
            ? (int)position
            : throw new ScriptException($"index {index} is outside the {what} (it has {count})");
    }
}
