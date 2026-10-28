namespace Jentl.Script;

/// <summary>
/// Where scripts run: the globals all scripts share (the standard functions, and whatever the game defines or
/// exposes), loading scripts, calling script functions, and the threads that wait across frames, advanced by
/// <see cref="Update"/> once a frame.
/// </summary>
public sealed class ScriptEngine
{
    private readonly List<ScriptThread> _threads = [];
    private readonly List<ScriptThread> _started = [];
    private readonly Dictionary<string, FunctionPrototype> _expressions = new(StringComparer.Ordinal);
    private readonly Script _shell;

    /// <param name="seed">Seeds <see cref="Random"/>, for scripts that do the same every time.</param>
    public ScriptEngine(int? seed = null)
    {
        Random = seed is { } fixedSeed ? new Random(fixedSeed) : new Random();
        _shell = new Script(this, "(engine)", Compile("", "(engine)"));
        Library.Add(this);
    }

    /// <summary>The globals every script sees. Scripts can change the ones here, but not add to them.</summary>
    public Dictionary<string, ScriptValue> Globals { get; } = [];

    /// <summary>A global, or nil.</summary>
    public ScriptValue this[string name]
    {
        get => Globals.TryGetValue(name, out var value) ? value : ScriptValue.Nil;
        set => Globals[name] = value;
    }

    /// <summary>For <c>random</c>, <c>chance</c>, <c>pick</c> and <c>shuffle</c>.</summary>
    public Random Random { get; set; }

    /// <summary>Where <c>print</c> writes; the console by default.</summary>
    public Action<string> Print { get; set; } = Console.WriteLine;

    /// <summary>How many steps a thread may run in one go before it counts as stuck and fails.</summary>
    public int InstructionLimit { get; set; } = 10_000_000;

    /// <summary>How deep script functions may call each other.</summary>
    public int MaxDepth { get; set; } = 200;

    /// <summary>The threads started and not yet finished.</summary>
    public IReadOnlyList<ScriptThread> Threads => [.. _threads, .. _started];

    /// <summary>
    /// Raised when a thread fails in <see cref="Update"/>, with the error in <see cref="ScriptThread.Error"/>. Without a
    /// handler, <see cref="Update"/> throws the error instead.
    /// </summary>
    public event Action<ScriptThread>? Failed;

    /// <summary>Gives scripts a function of the game's: <c>engine.Define("say", call => ...)</c>.</summary>
    public void Define(string name, Func<ScriptCall, ScriptValue> function) => Globals[name] = new NativeFunction(name, function);

    /// <summary>Gives scripts a function of the game's that returns nothing.</summary>
    public void Define(string name, Action<ScriptCall> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Define(name, call =>
        {
            action(call);
            return ScriptValue.Nil;
        });
    }

    /// <summary>Gives scripts one of the game's objects (or a value) by name, through <see cref="ScriptValue.From"/>.</summary>
    public void Expose(string name, object? value) => Globals[name] = ScriptValue.From(value);

    /// <summary>Reads and compiles a script without running it; throws <see cref="ScriptSyntaxException"/> on a mistake.</summary>
    public Script Load(string source, string name = "script") => new(this, name, Compile(source, name));

    /// <summary>Loads a script and runs its top level at once.</summary>
    public Script Run(string source, string name = "script")
    {
        var script = Load(source, name);
        script.Run();
        return script;
    }

    /// <summary>Works out an expression with the engine's globals: <c>engine.Evaluate("gold &gt;= 10")</c>. Compiled once per text.</summary>
    public ScriptValue Evaluate(string expression) => Call(new ScriptFunction(CompileExpression(expression, "(expression)"), null, _shell));

    /// <summary>Calls a function at once and returns its result; it may not wait. Throws its <see cref="ScriptException"/> when it fails.</summary>
    public ScriptValue Call(ScriptCallable function, params ScriptValue[] arguments)
    {
        ArgumentNullException.ThrowIfNull(function);
        var thread = new ScriptThread(this, function, arguments, canWait: false);
        thread.Advance(TimeSpan.Zero);
        return thread.Status == ScriptThreadStatus.Failed ? throw thread.Error! : thread.Result;
    }

    /// <summary>Calls a function value at once; see <see cref="Call(ScriptCallable, ScriptValue[])"/>.</summary>
    public ScriptValue Call(ScriptValue function, params ScriptValue[] arguments) => Call(function.AsFunction, arguments);

    /// <summary>Starts a function as a thread; it first runs in the next <see cref="Update"/>, and may wait.</summary>
    public ScriptThread Start(ScriptCallable function, params ScriptValue[] arguments)
    {
        ArgumentNullException.ThrowIfNull(function);
        var thread = new ScriptThread(this, function, arguments, canWait: true);
        _started.Add(thread);
        return thread;
    }

    /// <summary>Advances every thread by a frame: ones whose wait is over carry on, new ones start. Threads started meanwhile wait for the next one.</summary>
    public void Update(TimeSpan elapsed)
    {
        _threads.AddRange(_started);
        _started.Clear();
        List<ScriptThread>? failed = null;
        foreach (var thread in _threads)
        {
            thread.Advance(elapsed);
            if (thread.Status == ScriptThreadStatus.Failed)
            {
                (failed ??= []).Add(thread);
            }
        }

        _threads.RemoveAll(thread => thread.IsFinished);
        foreach (var thread in failed ?? [])
        {
            if (Failed is null)
            {
                throw thread.Error!;
            }

            Failed(thread);
        }
    }

    /// <summary>Stops every thread.</summary>
    public void StopAll()
    {
        foreach (var thread in Threads)
        {
            thread.Stop();
        }

        _threads.Clear();
        _started.Clear();
    }

    internal static FunctionPrototype Compile(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Compiler.CompileScript(Parser.ParseScript(source, name), name);
    }

    internal FunctionPrototype CompileExpression(string expression, string name)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var key = $"{name}\n{expression}";
        if (!_expressions.TryGetValue(key, out var prototype))
        {
            prototype = Compiler.CompileExpression(Parser.ParseExpression(expression, name), name);
            _expressions[key] = prototype;
        }

        return prototype;
    }
}
