using System.Globalization;

namespace Jentl.Script;

/// <summary>The functions every script has.</summary>
internal static class Library
{
    public static void Add(ScriptEngine engine)
    {
        engine["pi"] = Math.PI;

        // Text and conversion.
        engine.Define("print", call => engine.Print(string.Join(" ", call.Arguments.Select(argument => argument.ToString()))));
        engine.Define("str", call => call[0].ToString());
        engine.Define("num", call => call[0].Type switch
        {
            ScriptType.Number => call[0],
            ScriptType.Text when double.TryParse(call[0].AsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => ScriptValue.Nil,
        });
        engine.Define("type", call => call[0].TypeName);
        engine.Define("upper", call => call.Text(0).ToUpperInvariant());
        engine.Define("lower", call => call.Text(0).ToLowerInvariant());
        engine.Define("trim", call => call.Text(0).Trim());
        engine.Define("split", call => new ScriptList(call.Text(0)
            .Split(call.Text(1, " "), call.Count > 1 ? StringSplitOptions.None : StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ScriptValue.FromText(part))));
        engine.Define("join", call => string.Join(call.Text(1, ""), call.List(0).Items.Select(item => item.ToString())));
        engine.Define("replace", call => call.Text(0).Replace(call.Text(1), call.Text(2), StringComparison.Ordinal));
        engine.Define("startsWith", call => call.Text(0).StartsWith(call.Text(1), StringComparison.Ordinal));
        engine.Define("endsWith", call => call.Text(0).EndsWith(call.Text(1), StringComparison.Ordinal));
        engine.Define("error", call => throw new ScriptException(call[0].ToString()));

        // Numbers.
        engine.Define("floor", call => Math.Floor(call.Number(0)));
        engine.Define("ceil", call => Math.Ceiling(call.Number(0)));
        engine.Define("round", call => Math.Round(call.Number(0), call.Whole(1, 0), MidpointRounding.AwayFromZero));
        engine.Define("abs", call => Math.Abs(call.Number(0)));
        engine.Define("sqrt", call => Math.Sqrt(call.Number(0)));
        engine.Define("sin", call => Math.Sin(call.Number(0)));
        engine.Define("cos", call => Math.Cos(call.Number(0)));
        engine.Define("atan2", call => Math.Atan2(call.Number(0), call.Number(1)));
        engine.Define("sign", call => (double)Math.Sign(call.Number(0)));
        engine.Define("min", call => Numbers(call).Min());
        engine.Define("max", call => Numbers(call).Max());
        engine.Define("clamp", call => Math.Clamp(call.Number(0), call.Number(1), call.Number(2)));
        engine.Define("lerp", call => call.Number(0) + ((call.Number(1) - call.Number(0)) * call.Number(2)));

        // Chance.
        engine.Define("random", call => call.Count switch
        {
            0 => engine.Random.NextDouble(),
            1 => (double)engine.Random.Next(call.Whole(0)),
            _ => (double)engine.Random.Next(call.Whole(0), call.Whole(1) + 1),
        });
        engine.Define("chance", call => engine.Random.NextDouble() < call.Number(0));
        engine.Define("pick", call => call.List(0).Items is { Count: > 0 } items
            ? items[engine.Random.Next(items.Count)]
            : throw call.Error("can't pick from an empty list"));
        engine.Define("shuffle", call =>
        {
            var items = call.List(0).Items;
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = engine.Random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }

            return call[0];
        });

        // Lists and maps.
        engine.Define("len", call => call[0].Type switch
        {
            ScriptType.List => call[0].AsList.Count,
            ScriptType.Map => call[0].AsMap.Count,
            ScriptType.Text => call[0].AsText.Length,
            ScriptType.Host when call[0].AsHost is ScriptRange range => range.Count,
            ScriptType.Host when call[0].Raw is System.Collections.ICollection collection => collection.Count,
            _ => throw call.Error($"can't measure {call[0].Describe()}"),
        });
        engine.Define("range", call => ScriptValue.FromHost(call.Count == 1
            ? new ScriptRange(0, call.Number(0), 1)
            : new ScriptRange(call.Number(0), call.Number(1), call.Number(2, 1))));
        engine.Define("push", call =>
        {
            var list = call.List(0);
            for (var i = 1; i < call.Count; i++)
            {
                list.Add(call[i]);
            }

            return call[0];
        });
        engine.Define("pop", call => call.List(0).Items is { Count: > 0 } items
            ? Remove(items, items.Count - 1)
            : throw call.Error("the list is empty"));
        engine.Define("insert", call =>
        {
            var items = call.List(0).Items;
            var at = call.Whole(1);
            items.Insert(at < 0 ? items.Count + at + 1 : at, call[2]);
            return call[0];
        });
        engine.Define("remove", call => call[0].Type == ScriptType.Map
            ? RemoveKey(call.Map(0), call[1])
            : Remove(call.List(0).Items, Operations.Position(call[1], call.List(0).Count, "list")));
        engine.Define("contains", call => call[0].Type switch
        {
            ScriptType.List => call[0].AsList.Items.Contains(call[1]),
            ScriptType.Map => call[0].AsMap.Entries.ContainsKey(call[1]),
            ScriptType.Text => call[0].AsText.Contains(call.Text(1), StringComparison.Ordinal),
            _ => throw call.Error($"can't look inside {call[0].Describe()}"),
        });
        engine.Define("indexOf", call => call[0].Type == ScriptType.Text
            ? call[0].AsText.IndexOf(call.Text(1), StringComparison.Ordinal)
            : call.List(0).Items.IndexOf(call[1]));
        engine.Define("keys", call => new ScriptList(call.Map(0).Entries.Keys));
        engine.Define("values", call => new ScriptList(call.Map(0).Entries.Values));
        engine.Define("copy", call => call[0].Type switch
        {
            ScriptType.List => new ScriptList(call[0].AsList.Items),
            ScriptType.Map => Copy(call[0].AsMap),
            _ => call[0],
        });
        engine.Define("slice", call =>
        {
            var items = call.List(0).Items;
            var from = Bound(call.Whole(1, 0), items.Count);
            var to = Bound(call.Whole(2, items.Count), items.Count);
            return new ScriptList(items.Skip(from).Take(Math.Max(0, to - from)));
        });
        engine.Define("sort", call =>
        {
            var items = call.List(0).Items;
            var by = call[1].IsNil ? null : call.Callable(1);
            var keys = items.Select(item => by is null ? item : engine.Call(by, item)).ToList();
            var order = Enumerable.Range(0, items.Count).OrderBy(i => keys[i], ValueOrder.Instance).ToList();
            var sorted = order.Select(i => items[i]).ToList();
            items.Clear();
            items.AddRange(sorted);
            return call[0];
        });
        engine.Define("map", call =>
        {
            var function = call.Callable(1);
            return new ScriptList(Items(call[0]).Select(item => engine.Call(function, item)));
        });
        engine.Define("filter", call =>
        {
            var function = call.Callable(1);
            return new ScriptList(Items(call[0]).Where(item => engine.Call(function, item).IsTruthy));
        });
        engine.Define("list", call => new ScriptList(Items(call[0])));

        // Threads and waiting.
        engine.Define("wait", call => call.Wait(TimeSpan.FromSeconds(call.Number(0, 0))));
        engine.Define("waitUntil", call =>
        {
            var condition = call.Callable(0);
            if (!engine.Call(condition).IsTruthy)
            {
                call.WaitUntil(() => engine.Call(condition).IsTruthy);
            }
        });
        engine.Define("spawn", call => ScriptValue.FromHost(engine.Start(call.Callable(0), [.. call.Arguments.Skip(1)])));
        engine.Define("stop", call => call.Host<ScriptThread>(0).Stop());
    }

    private static IEnumerable<double> Numbers(ScriptCall call)
    {
        var values = call.Count == 1 && call[0].Type == ScriptType.List ? call[0].AsList.Items : [.. call.Arguments];
        return values.Count > 0
            ? values.Select((value, i) => value.Type == ScriptType.Number ? value.AsNumber : throw call.Error($"value {i + 1} should be a number, got {value.Describe()}"))
            : throw call.Error("needs at least one number");
    }

    /// <summary>What a one-name <c>for</c> loop would go through, all at once.</summary>
    private static List<ScriptValue> Items(ScriptValue source)
    {
        var items = new List<ScriptValue>();
        var iteration = new Iteration(source);
        while (iteration.Next(out var key, out var value))
        {
            items.Add(iteration.KeysOnly ? key : value);
        }

        return items;
    }

    private static ScriptValue Remove(List<ScriptValue> items, int at)
    {
        var item = items[at];
        items.RemoveAt(at);
        return item;
    }

    private static ScriptValue RemoveKey(ScriptMap map, ScriptValue key)
    {
        var value = map[key];
        map.Entries.Remove(key);
        return value;
    }

    private static ScriptMap Copy(ScriptMap map)
    {
        var copy = new ScriptMap();
        foreach (var (key, value) in map.Entries)
        {
            copy.Entries[key] = value;
        }

        return copy;
    }

    private static int Bound(int index, int count) => Math.Clamp(index < 0 ? count + index : index, 0, count);

    /// <summary>Numbers before text, each in its own order; anything else can't be sorted.</summary>
    private sealed class ValueOrder : IComparer<ScriptValue>
    {
        public static ValueOrder Instance { get; } = new();

        public int Compare(ScriptValue x, ScriptValue y) => (x.Type, y.Type) switch
        {
            (ScriptType.Number, ScriptType.Number) => x.AsNumber.CompareTo(y.AsNumber),
            (ScriptType.Text, ScriptType.Text) => string.CompareOrdinal(x.AsText, y.AsText),
            (ScriptType.Number, ScriptType.Text) => -1,
            (ScriptType.Text, ScriptType.Number) => 1,
            _ => throw new ScriptException($"can't sort {x.Describe()} with {y.Describe()}"),
        };
    }
}
