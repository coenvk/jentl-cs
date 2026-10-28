namespace Jentl.Script;

/// <summary>
/// A loaded script: its top level, and the globals its top-level <c>let</c>s and <c>fn</c>s make once it has run, which
/// the game reads and calls by name. Functions in it see these globals first, then the engine's.
/// </summary>
public sealed class Script
{
    private FunctionPrototype _main;

    internal Script(ScriptEngine engine, string name, FunctionPrototype main)
    {
        Engine = engine;
        Name = name;
        _main = main;
    }

    public ScriptEngine Engine { get; }

    /// <summary>The name it was loaded under, as error messages show it.</summary>
    public string Name { get; }

    public Dictionary<string, ScriptValue> Globals { get; } = [];

    /// <summary>A global of the script, or nil.</summary>
    public ScriptValue this[string name]
    {
        get => Globals.TryGetValue(name, out var value) ? value : ScriptValue.Nil;
        set => Globals[name] = value;
    }

    /// <summary>The top level as a function, to run or start as a thread.</summary>
    public ScriptFunction Main => new(_main, null, this);

    public bool Has(string name) => Globals.ContainsKey(name);

    /// <summary>Runs the top level at once, making its globals; it may not wait. Returns what a top-level <c>return</c> gives, or nil.</summary>
    public ScriptValue Run() => Engine.Call(Main);

    /// <summary>Runs the top level as a thread, which may wait (a cutscene written as a script, say).</summary>
    public ScriptThread Start() => Engine.Start(Main);

    /// <summary>Calls a function of the script at once; it may not wait.</summary>
    public ScriptValue Call(string function, params ScriptValue[] arguments) => Engine.Call(Function(function), arguments);

    /// <summary>Starts a function of the script as a thread, which may wait.</summary>
    public ScriptThread Start(string function, params ScriptValue[] arguments) => Engine.Start(Function(function), arguments);

    /// <summary>Works out an expression with the script's globals and the engine's: <c>gold &gt;= 10 and has_key</c>.</summary>
    public ScriptValue Evaluate(string expression) => Engine.Call(new ScriptFunction(Engine.CompileExpression(expression, Name), null, this));

    /// <summary>
    /// Compiles new source for the script and runs its top level, keeping the globals it had (top-level <c>let</c>s
    /// set theirs again). Threads already running carry on with the old functions. For hot reload.
    /// </summary>
    public ScriptValue Reload(string source)
    {
        _main = ScriptEngine.Compile(source, Name);
        return Run();
    }

    private ScriptCallable Function(string name) => this[name] is { Type: ScriptType.Function } function
        ? function.AsFunction
        : throw new ScriptException($"{Name} has no function '{name}'");

    public override string ToString() => Name;
}
