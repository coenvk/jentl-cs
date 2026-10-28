namespace Jentl.Script;

/// <summary>Something a script can call: a <see cref="ScriptFunction"/> written in a script, or a <see cref="NativeFunction"/> from the game.</summary>
public abstract class ScriptCallable
{
    private protected ScriptCallable(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => $"fn {Name}";
}

/// <summary>A function written in a script, with the variables around it that it can still see.</summary>
public sealed class ScriptFunction : ScriptCallable
{
    internal ScriptFunction(FunctionPrototype prototype, LocalScope? captured, Script script)
        : base(prototype.Name)
    {
        Prototype = prototype;
        Captured = captured;
        Script = script;
    }

    /// <summary>The script it was written in; its globals are the function's globals.</summary>
    public Script Script { get; }

    public IReadOnlyList<string> Parameters => Prototype.Parameters;

    internal FunctionPrototype Prototype { get; }

    internal LocalScope? Captured { get; }
}

/// <summary>
/// A function the game gives scripts. It reads its arguments from the <see cref="ScriptCall"/> and returns a value
/// (nil for nothing); to make the script wait, it calls <see cref="ScriptCall.Wait"/> or one of its kin first.
/// </summary>
public sealed class NativeFunction : ScriptCallable
{
    private readonly Func<ScriptCall, ScriptValue> _body;

    public NativeFunction(string name, Func<ScriptCall, ScriptValue> body)
        : base(name) => _body = body ?? throw new ArgumentNullException(nameof(body));

    internal ScriptValue Invoke(ScriptCall call) => _body(call);
}

/// <summary>The locals of one running function, and the ones around it that it closes over.</summary>
internal sealed class LocalScope(int size, LocalScope? parent)
{
    public ScriptValue[] Slots { get; } = new ScriptValue[size];

    public LocalScope? Parent { get; } = parent;

    public LocalScope Out(int depth)
    {
        var scope = this;
        for (var i = 0; i < depth; i++)
        {
            scope = scope.Parent!;
        }

        return scope;
    }
}

/// <summary>
/// A value a script waits for until the game hands it over: <see cref="ScriptCall.Suspend"/> returns one, and the
/// call that made it returns <see cref="Resolve"/>'s value to the script. For a choice in a dialogue, say.
/// </summary>
public sealed class ScriptPromise
{
    public bool IsResolved { get; private set; }

    public ScriptValue Value { get; private set; }

    public void Resolve(ScriptValue value)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("This promise was already resolved.");
        }

        Value = value;
        IsResolved = true;
    }
}

/// <summary>What a waiting thread waits for; checked once per <see cref="ScriptEngine.Update"/>.</summary>
internal abstract class Waiter
{
    public abstract bool IsOver(TimeSpan elapsed);

    /// <summary>The value the waiting call returns, when it isn't the one it returned before waiting.</summary>
    public virtual ScriptValue? Result => null;

    public sealed class Time(TimeSpan left) : Waiter
    {
        private TimeSpan _left = left;

        public override bool IsOver(TimeSpan elapsed)
        {
            _left -= elapsed;
            return _left <= TimeSpan.Zero;
        }
    }

    public sealed class Until(Func<bool> condition) : Waiter
    {
        public override bool IsOver(TimeSpan elapsed) => condition();
    }

    public sealed class Promised(ScriptPromise promise) : Waiter
    {
        public override bool IsOver(TimeSpan elapsed) => promise.IsResolved;

        public override ScriptValue? Result => promise.Value;
    }
}

/// <summary>Numbers from a start up to (not including) a stop, by a step: what <c>range</c> returns for <c>for</c> loops.</summary>
public sealed class ScriptRange : IScriptObject
{
    public ScriptRange(double start, double stop, double step)
    {
        if (step == 0 || double.IsNaN(step))
        {
            throw new ScriptException("range: the step can't be 0");
        }

        Start = start;
        Stop = stop;
        Step = step;
    }

    public double Start { get; }

    public double Stop { get; }

    public double Step { get; }

    public int Count => Math.Max(0, (int)Math.Ceiling((Stop - Start) / Step));

    public double this[int index] => Start + (index * Step);

    public ScriptValue GetMember(string name) => name switch
    {
        "start" => Start,
        "stop" => Stop,
        "step" => Step,
        "count" => Count,
        _ => throw new ScriptException($"a range has no field '{name}' (it has start, stop, step and count)"),
    };

    public void SetMember(string name, ScriptValue value) => throw new ScriptException("a range can't be changed");

    public override string ToString() => Step == 1
        ? $"range({ScriptValue.FormatNumber(Start)}, {ScriptValue.FormatNumber(Stop)})"
        : $"range({ScriptValue.FormatNumber(Start)}, {ScriptValue.FormatNumber(Stop)}, {ScriptValue.FormatNumber(Step)})";
}

/// <summary>A <c>for</c> loop's place in what it goes through.</summary>
internal sealed class Iteration : IScriptObject
{
    private readonly ScriptValue _source;
    private readonly ScriptValue[]? _keys;
    private readonly System.Collections.IEnumerator? _host;
    private int _index;

    public Iteration(ScriptValue source)
    {
        _source = source;
        switch (source.Type)
        {
            case ScriptType.Map:
                _keys = [.. source.AsMap.Entries.Keys];
                break;
            case ScriptType.List or ScriptType.Text:
                break;
            case ScriptType.Host when source.AsHost is ScriptRange:
                break;
            case ScriptType.Host when source.Raw is System.Collections.IEnumerable items:
                _host = items.GetEnumerator();
                break;
            default:
                throw new ScriptException($"can't go through {source.Describe()} with 'for'; use a list, a map, text or range(...)");
        }
    }

    /// <summary>The next key (or index) and value; false when there are no more.</summary>
    public bool Next(out ScriptValue key, out ScriptValue value)
    {
        var index = _index++;
        key = index;
        switch (_source.Type)
        {
            case ScriptType.Map:
                while (index < _keys!.Length)
                {
                    key = _keys[index];
                    value = _source.AsMap[key];
                    if (!value.IsNil)
                    {
                        return true;
                    }

                    index = _index++;
                }

                break;
            case ScriptType.List when index < _source.AsList.Count:
                value = _source.AsList[index];
                return true;
            case ScriptType.Text when index < _source.AsText.Length:
                value = _source.AsText[index].ToString();
                return true;
            case ScriptType.Host when _host is not null:
                if (_host.MoveNext())
                {
                    value = ScriptValue.From(_host.Current);
                    return true;
                }

                break;
            case ScriptType.Host when _source.AsHost is ScriptRange range && index < range.Count:
                value = range[index];
                return true;
        }

        value = ScriptValue.Nil;
        return false;
    }

    /// <summary>The loop variable when there is only one: the key of a map, the value of anything else.</summary>
    public bool KeysOnly => _source.Type == ScriptType.Map;

    public ScriptValue GetMember(string name) => throw new ScriptException("a loop's place has no fields");

    public void SetMember(string name, ScriptValue value) => throw new ScriptException("a loop's place can't be changed");
}
