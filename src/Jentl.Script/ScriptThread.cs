namespace Jentl.Script;

public enum ScriptThreadStatus
{
    /// <summary>Started, and runs on the next <see cref="ScriptEngine.Update"/>.</summary>
    Ready,

    /// <summary>In a <c>wait</c>, or waiting for the game (<see cref="ScriptCall.Suspend"/>).</summary>
    Waiting,
    Done,
    Failed,
    Stopped,
}

/// <summary>
/// One running script function, on its own stack, so it can wait (<c>wait(2)</c>, <c>say("...")</c>) and carry on in a
/// later frame where it left off while the game and other threads go on. <see cref="ScriptEngine.Start(ScriptCallable, ScriptValue[])"/>
/// makes one; scripts see it as an object with <c>done</c> and <c>result</c>.
/// </summary>
public sealed class ScriptThread : IScriptObject
{
    private readonly ScriptEngine _engine;
    private readonly bool _canWait;
    private readonly List<Frame> _frames = [];
    private readonly ScriptCall _call;
    private ScriptValue[] _stack = new ScriptValue[64];
    private int _count;
    private Waiter? _waiter;

    internal ScriptThread(ScriptEngine engine, ScriptCallable function, IReadOnlyList<ScriptValue> arguments, bool canWait)
    {
        _engine = engine;
        _canWait = canWait;
        _call = new ScriptCall(this, engine);
        Name = function.Name;
        Push(function);
        foreach (var argument in arguments)
        {
            Push(argument);
        }
    }

    private sealed class Frame(ScriptFunction function, LocalScope locals, int stackBase)
    {
        public ScriptFunction Function { get; } = function;

        public FunctionPrototype Prototype { get; } = function.Prototype;

        public LocalScope Locals { get; set; } = locals;

        public int StackBase { get; } = stackBase;

        public int Ip { get; set; }

        public int Line => Prototype.Lines[Math.Max(0, Ip - 1)];
    }

    /// <summary>The name of the function it started with.</summary>
    public string Name { get; }

    public ScriptThreadStatus Status { get; private set; }

    /// <summary>Whether it is done, failed or stopped.</summary>
    public bool IsFinished => Status is ScriptThreadStatus.Done or ScriptThreadStatus.Failed or ScriptThreadStatus.Stopped;

    /// <summary>What the function returned, once <see cref="ScriptThreadStatus.Done"/>.</summary>
    public ScriptValue Result { get; private set; }

    /// <summary>What went wrong, once <see cref="ScriptThreadStatus.Failed"/>.</summary>
    public ScriptException? Error { get; private set; }

    /// <summary>Whether it may wait: false for <see cref="ScriptEngine.Call(ScriptCallable, ScriptValue[])"/>, which needs its answer at once.</summary>
    public bool CanWait => _canWait;

    /// <summary>Stops it where it is; it won't run again.</summary>
    public void Stop()
    {
        if (!IsFinished)
        {
            Status = ScriptThreadStatus.Stopped;
            _waiter = null;
        }
    }

    public ScriptValue GetMember(string name) => name switch
    {
        "done" => IsFinished,
        "result" => Result,
        "name" => Name,
        _ => throw new ScriptException($"a thread has no field '{name}' (it has done, result and name)"),
    };

    public void SetMember(string name, ScriptValue value) => throw new ScriptException("a thread's fields can't be changed; use stop(thread)");

    public override string ToString() => $"thread {Name} ({Status})";

    internal ScriptValue Argument(int stackBase, int index) => _stack[stackBase + index];

    internal void WaitFor(Waiter waiter)
    {
        if (!_canWait)
        {
            throw new ScriptException("can't wait here: the game called this function and needs its answer now (start it as a thread to let it wait)");
        }

        _waiter = waiter;
    }

    /// <summary>Runs it until it waits, finishes or fails; a waiting thread first counts <paramref name="elapsed"/> towards its wait.</summary>
    internal void Advance(TimeSpan elapsed)
    {
        if (IsFinished)
        {
            return;
        }

        try
        {
            if (Status == ScriptThreadStatus.Waiting)
            {
                if (!_waiter!.IsOver(elapsed))
                {
                    return;
                }

                if (_waiter.Result is { } result)
                {
                    _stack[_count - 1] = result;
                }

                _waiter = null;
                Status = ScriptThreadStatus.Ready;
                if (_frames.Count == 0)
                {
                    // The game's own function it started with is done waiting.
                    Finish(Pop());
                    return;
                }
            }
            else if (_frames.Count == 0 && !Invoke(0))
            {
                return;
            }

            if (!IsFinished)
            {
                Execute();
            }
        }
        catch (Exception exception) when (exception is ScriptException or ArgumentException or InvalidOperationException or InvalidCastException or FormatException or OverflowException or NullReferenceException or IndexOutOfRangeException or KeyNotFoundException or NotSupportedException or System.Reflection.TargetInvocationException)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        _waiter = null;
        Status = ScriptThreadStatus.Failed;
        var inner = exception is System.Reflection.TargetInvocationException { InnerException: { } thrown } ? thrown : exception;
        if (inner is ScriptException { ScriptName: not null } placed)
        {
            // Already placed, by a call into another script function from the game.
            Error = placed;
        }
        else if (_frames.Count == 0)
        {
            Error = new ScriptException(Reason(inner), null, 0, [], inner is ScriptException ? null : inner);
        }
        else
        {
            var frame = _frames[^1];
            var trace = Enumerable.Range(0, _frames.Count).Reverse()
                .Select(i => $"{_frames[i].Prototype.Source}:{_frames[i].Line} in {_frames[i].Prototype.Name}")
                .ToList();
            Error = new ScriptException(Reason(inner), frame.Prototype.Source, frame.Line, trace, inner is ScriptException ? null : inner);
        }

        _frames.Clear();
        _count = 0;
    }

    private static string Reason(Exception exception) => exception is ScriptException script ? script.Reason : exception.Message;

    private void Push(ScriptValue value)
    {
        if (_count == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_count++] = value;
    }

    private ScriptValue Pop() => _stack[--_count];

    private ScriptValue Peek => _stack[_count - 1];

    /// <summary>Calls the function at <paramref name="calleeAt"/> with the values above it; false when it made the thread wait.</summary>
    private bool Invoke(int calleeAt)
    {
        var callee = _stack[calleeAt];
        var count = _count - calleeAt - 1;
        if (callee.Type != ScriptType.Function)
        {
            throw new ScriptException($"can't call {callee.Describe()}");
        }

        switch (callee.AsFunction)
        {
            case ScriptFunction function:
                if (_frames.Count >= _engine.MaxDepth)
                {
                    throw new ScriptException($"calls went more than {_engine.MaxDepth} deep (a function calling itself without end?)");
                }

                var prototype = function.Prototype;
                var locals = new LocalScope(prototype.LocalCount, function.Captured);
                for (var i = 0; i < Math.Min(count, prototype.ParameterCount); i++)
                {
                    locals.Slots[i] = _stack[calleeAt + 1 + i];
                }

                Array.Clear(_stack, calleeAt, _count - calleeAt);
                _count = calleeAt;
                _frames.Add(new Frame(function, locals, calleeAt));
                return true;
            case NativeFunction native:
                _call.Begin(native.Name, calleeAt + 1, count);
                var result = native.Invoke(_call);
                Array.Clear(_stack, calleeAt, _count - calleeAt);
                _count = calleeAt;
                Push(result);
                if (_waiter is not null)
                {
                    Status = ScriptThreadStatus.Waiting;
                    return false;
                }

                if (_frames.Count == 0)
                {
                    Finish(Pop());
                }

                return true;
            default:
                throw new ScriptException($"can't call {callee.Describe()}");
        }
    }

    private void Finish(ScriptValue result)
    {
        Result = result;
        Status = ScriptThreadStatus.Done;
        _count = 0;
    }

    private void Execute()
    {
        var steps = 0;
        var limit = _engine.InstructionLimit;
        while (_frames.Count > 0)
        {
            var frame = _frames[^1];
            var instruction = frame.Prototype.Code[frame.Ip++];
            if (++steps > limit)
            {
                throw new ScriptException($"ran more than {limit} steps without waiting (an endless loop?)");
            }

            switch (instruction.Op)
            {
                case OpCode.Constant:
                    Push(frame.Prototype.Constants[instruction.A]);
                    break;
                case OpCode.Nil:
                    Push(ScriptValue.Nil);
                    break;
                case OpCode.True:
                    Push(ScriptValue.True);
                    break;
                case OpCode.False:
                    Push(ScriptValue.False);
                    break;
                case OpCode.Pop:
                    _count--;
                    break;
                case OpCode.Duplicate:
                    Push(Peek);
                    break;
                case OpCode.DuplicateTwo:
                    Push(_stack[_count - 2]);
                    Push(_stack[_count - 2]);
                    break;
                case OpCode.Load:
                    Push(frame.Locals.Out(instruction.A).Slots[instruction.B]);
                    break;
                case OpCode.Store:
                    frame.Locals.Out(instruction.A).Slots[instruction.B] = Pop();
                    break;
                case OpCode.LoadGlobal:
                    Push(LoadGlobal(frame, frame.Prototype.Constants[instruction.A].AsText));
                    break;
                case OpCode.StoreGlobal:
                    StoreGlobal(frame, frame.Prototype.Constants[instruction.A].AsText, Pop());
                    break;
                case OpCode.DefineGlobal:
                    frame.Function.Script.Globals[frame.Prototype.Constants[instruction.A].AsText] = Pop();
                    break;
                case OpCode.GetField:
                {
                    var target = Pop();
                    Push(Operations.GetField(target, frame.Prototype.Constants[instruction.A].AsText));
                    break;
                }

                case OpCode.SetField:
                {
                    var value = Pop();
                    var target = Pop();
                    Operations.SetField(target, frame.Prototype.Constants[instruction.A].AsText, value);
                    break;
                }

                case OpCode.PutBackField:
                {
                    var value = Pop();
                    if (value.Type == ScriptType.Host && value.Raw is { } part && part.GetType().IsValueType)
                    {
                        Operations.SetField(Peek, frame.Prototype.Constants[instruction.A].AsText, value);
                    }

                    break;
                }

                case OpCode.PutBackIndex:
                {
                    var value = Pop();
                    var key = Pop();
                    if (value.Type == ScriptType.Host && value.Raw is { } part && part.GetType().IsValueType)
                    {
                        Operations.SetIndex(Peek, key, value);
                    }

                    break;
                }

                case OpCode.GetIndex:
                {
                    var key = Pop();
                    var target = Pop();
                    Push(Operations.GetIndex(target, key));
                    break;
                }

                case OpCode.SetIndex:
                {
                    var value = Pop();
                    var key = Pop();
                    var target = Pop();
                    Operations.SetIndex(target, key, value);
                    break;
                }

                case OpCode.Add:
                case OpCode.Subtract:
                case OpCode.Multiply:
                case OpCode.Divide:
                case OpCode.Modulo:
                case OpCode.Less:
                case OpCode.LessEqual:
                case OpCode.Greater:
                case OpCode.GreaterEqual:
                {
                    var right = Pop();
                    var left = Pop();
                    Push(Operations.Binary(instruction.Op, left, right));
                    break;
                }

                case OpCode.Equal:
                {
                    var right = Pop();
                    var left = Pop();
                    Push(left == right);
                    break;
                }

                case OpCode.NotEqual:
                {
                    var right = Pop();
                    var left = Pop();
                    Push(left != right);
                    break;
                }

                case OpCode.Negate:
                {
                    var operand = Pop();
                    Push(operand.Type == ScriptType.Number ? -operand.AsNumber : throw new ScriptException($"can't negate {operand.Describe()}"));
                    break;
                }

                case OpCode.Not:
                    Push(!Pop().IsTruthy);
                    break;
                case OpCode.Jump:
                    frame.Ip = instruction.A;
                    break;
                case OpCode.JumpIfFalse:
                    if (!Pop().IsTruthy)
                    {
                        frame.Ip = instruction.A;
                    }

                    break;
                case OpCode.JumpIfFalseOrPop:
                    if (!Peek.IsTruthy)
                    {
                        frame.Ip = instruction.A;
                    }
                    else
                    {
                        _count--;
                    }

                    break;
                case OpCode.JumpIfTrueOrPop:
                    if (Peek.IsTruthy)
                    {
                        frame.Ip = instruction.A;
                    }
                    else
                    {
                        _count--;
                    }

                    break;
                case OpCode.Call:
                    if (!Invoke(_count - instruction.A - 1))
                    {
                        return;
                    }

                    break;
                case OpCode.Return:
                {
                    var result = Pop();
                    _frames.RemoveAt(_frames.Count - 1);
                    Array.Clear(_stack, frame.StackBase, _count - frame.StackBase);
                    _count = frame.StackBase;
                    if (_frames.Count == 0)
                    {
                        Finish(result);
                        return;
                    }

                    Push(result);
                    break;
                }

                case OpCode.MakeList:
                {
                    var list = new ScriptList();
                    for (var i = _count - instruction.A; i < _count; i++)
                    {
                        list.Add(_stack[i]);
                    }

                    _count -= instruction.A;
                    Push(list);
                    break;
                }

                case OpCode.MakeMap:
                {
                    var map = new ScriptMap();
                    for (var i = _count - (instruction.A * 2); i < _count; i += 2)
                    {
                        map[Operations.Key(_stack[i])] = _stack[i + 1];
                    }

                    _count -= instruction.A * 2;
                    Push(map);
                    break;
                }

                case OpCode.EnterScope:
                    frame.Locals = new LocalScope(instruction.A, frame.Locals);
                    break;
                case OpCode.ExitScope:
                    frame.Locals = frame.Locals.Parent!;
                    break;
                case OpCode.Closure:
                    Push(new ScriptFunction(frame.Prototype.Functions[instruction.A], frame.Locals, frame.Function.Script));
                    break;
                case OpCode.IterateStart:
                    Push(ScriptValue.FromHost(new Iteration(Pop())));
                    break;
                case OpCode.IterateNext:
                {
                    var iteration = (Iteration)Peek.AsHost;
                    if (!iteration.Next(out var key, out var value))
                    {
                        frame.Ip = instruction.A;
                    }
                    else if (instruction.B == 2)
                    {
                        Push(key);
                        Push(value);
                    }
                    else
                    {
                        Push(iteration.KeysOnly ? key : value);
                    }

                    break;
                }

                case OpCode.Interpolate:
                {
                    var text = string.Concat(Enumerable.Range(_count - instruction.A, instruction.A).Select(i => _stack[i].ToString()));
                    _count -= instruction.A;
                    Push(text);
                    break;
                }
            }
        }
    }

    private ScriptValue LoadGlobal(Frame frame, string name)
    {
        if (frame.Function.Script.Globals.TryGetValue(name, out var value) || _engine.Globals.TryGetValue(name, out value))
        {
            return value;
        }

        throw new ScriptException($"unknown name '{name}'");
    }

    private void StoreGlobal(Frame frame, string name, ScriptValue value)
    {
        if (frame.Function.Script.Globals.ContainsKey(name))
        {
            frame.Function.Script.Globals[name] = value;
        }
        else if (_engine.Globals.ContainsKey(name))
        {
            _engine.Globals[name] = value;
        }
        else
        {
            throw new ScriptException($"unknown name '{name}' (use 'let {name} = ...' to make a new variable)");
        }
    }
}
