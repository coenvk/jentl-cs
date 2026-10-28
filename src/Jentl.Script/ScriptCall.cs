namespace Jentl.Script;

/// <summary>
/// A call from a script to one of the game's functions: its arguments, read with checks that explain a wrong one
/// (<c>say: argument 1 should be text, got number 3</c>), and the ways to make the script wait. Valid only during the
/// call; a missing argument reads as nil.
/// </summary>
public sealed class ScriptCall
{
    private int _base;

    internal ScriptCall(ScriptThread thread, ScriptEngine engine)
    {
        Thread = thread;
        Engine = engine;
    }

    public ScriptEngine Engine { get; }

    /// <summary>The thread the script is running on.</summary>
    public ScriptThread Thread { get; }

    /// <summary>The name of the function called.</summary>
    public string Function { get; private set; } = "";

    /// <summary>How many arguments the script passed.</summary>
    public int Count { get; private set; }

    public ScriptValue this[int index] => index >= 0 && index < Count ? Thread.Argument(_base, index) : ScriptValue.Nil;

    public IEnumerable<ScriptValue> Arguments
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }
    }

    public double Number(int index) => Wanted(index, ScriptType.Number, "a number").AsNumber;

    public double Number(int index, double fallback) => this[index].IsNil ? fallback : Number(index);

    /// <summary>A whole number, rounding a fraction off and checking it is in range.</summary>
    public int Whole(int index) => checked((int)Math.Round(Number(index), MidpointRounding.AwayFromZero));

    public int Whole(int index, int fallback) => this[index].IsNil ? fallback : Whole(index);

    public string Text(int index) => Wanted(index, ScriptType.Text, "text").AsText;

    public string Text(int index, string fallback) => this[index].IsNil ? fallback : Text(index);

    /// <summary>Whether the argument is truthy (anything but nil and false).</summary>
    public bool IsTrue(int index) => this[index].IsTruthy;

    public ScriptList List(int index) => Wanted(index, ScriptType.List, "a list").AsList;

    public ScriptMap Map(int index) => Wanted(index, ScriptType.Map, "a map").AsMap;

    public ScriptCallable Callable(int index) => Wanted(index, ScriptType.Function, "a function").AsFunction;

    /// <summary>The game's object passed as the argument, of type <typeparamref name="T"/>.</summary>
    public T Host<T>(int index) => this[index].Raw is T host
        ? host
        : throw Error($"argument {index + 1} should be {typeof(T).Name}, got {this[index].Describe()}");

    /// <summary>An error to throw, saying which function it came from.</summary>
    public ScriptException Error(string message) => new($"{Function}: {message}");

    /// <summary>Makes the script wait this long once the call returns (the game goes on meanwhile).</summary>
    public void Wait(TimeSpan time) => Thread.WaitFor(new Waiter.Time(time));

    /// <summary>Makes the script wait, once the call returns, until <paramref name="condition"/> holds; checked once a frame.</summary>
    public void WaitUntil(Func<bool> condition) => Thread.WaitFor(new Waiter.Until(condition ?? throw new ArgumentNullException(nameof(condition))));

    /// <summary>
    /// Makes the script wait until the game resolves the promise returned here; the call then returns the promise's
    /// value instead of its own. For a choice the player makes in a menu, say.
    /// </summary>
    public ScriptPromise Suspend()
    {
        var promise = new ScriptPromise();
        Thread.WaitFor(new Waiter.Promised(promise));
        return promise;
    }

    internal void Begin(string function, int stackBase, int count)
    {
        Function = function;
        _base = stackBase;
        Count = count;
    }

    private ScriptValue Wanted(int index, ScriptType type, string what)
    {
        var value = this[index];
        return value.Type == type
            ? value
            : throw Error(index < Count
                ? $"argument {index + 1} should be {what}, got {value.Describe()}"
                : $"argument {index + 1} ({what}) is missing");
    }
}
