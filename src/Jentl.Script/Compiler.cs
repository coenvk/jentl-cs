namespace Jentl.Script;

internal enum OpCode : byte
{
    Constant,
    Nil,
    True,
    False,
    Pop,
    Duplicate,
    DuplicateTwo,

    /// <summary>A: how many functions out (0 for this one), B: the slot.</summary>
    Load,
    Store,

    /// <summary>A: the name, as a constant.</summary>
    LoadGlobal,
    StoreGlobal,
    DefineGlobal,
    GetField,
    SetField,
    GetIndex,
    SetIndex,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Negate,
    Not,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    /// <summary>A: the instruction to go to.</summary>
    Jump,
    JumpIfFalse,
    JumpIfFalseOrPop,
    JumpIfTrueOrPop,

    /// <summary>A: how many arguments.</summary>
    Call,
    Return,
    MakeList,
    MakeMap,

    /// <summary>A: the inner function.</summary>
    Closure,

    /// <summary>
    /// A: a name. Pops a value, leaving the object it was read from; when the value is one of the game's structs (a
    /// copy), puts it back into the object under the name. For writing back along a path, <c>a.b.c.d = v</c>.
    /// </summary>
    PutBackField,

    /// <summary>As <see cref="PutBackField"/>, for a value read by index: pops the value and the key, leaving the object.</summary>
    PutBackIndex,

    /// <summary>Opens a scope of A locals inside the running function's: a loop's variables for one time round.</summary>
    EnterScope,
    ExitScope,
    IterateStart,

    /// <summary>A: where to go when done, B: how many values to push (1 or 2).</summary>
    IterateNext,
    Interpolate,
}

internal readonly record struct Instruction(OpCode Op, int A, int B);

/// <summary>A function as compiled: its instructions, constants and inner functions. Closures share it.</summary>
internal sealed class FunctionPrototype
{
    public required string Name { get; init; }

    public required string Source { get; init; }

    public required int Line { get; init; }

    public required int ParameterCount { get; init; }

    public required IReadOnlyList<string> Parameters { get; init; }

    public int LocalCount { get; set; }

    public Instruction[] Code { get; set; } = [];

    public int[] Lines { get; set; } = [];

    public ScriptValue[] Constants { get; set; } = [];

    public FunctionPrototype[] Functions { get; set; } = [];
}

/// <summary>
/// Turns parsed statements into instructions for <see cref="ScriptThread"/>, a stack machine. Locals live in slots
/// resolved here; names that aren't locals are globals, looked up when run (the script's own, then the engine's).
/// A <c>let</c> or <c>fn</c> at the top level of a script makes a global of the script, so the game can find it.
/// </summary>
internal sealed class Compiler
{
    private readonly Compiler? _parent;
    private readonly FunctionPrototype _prototype;
    private readonly bool _topLevel;
    private readonly List<Instruction> _code = [];
    private readonly List<int> _lines = [];
    private readonly List<ScriptValue> _constants = [];
    private readonly Dictionary<ScriptValue, int> _constantIndex = [];
    private readonly List<FunctionPrototype> _functions = [];
    private readonly List<Scope> _scopes = [new()];
    private readonly List<Loop> _loops = [];

    private Compiler(Compiler? parent, FunctionPrototype prototype, bool topLevel)
    {
        _parent = parent;
        _prototype = prototype;
        _topLevel = topLevel;
        foreach (var parameter in prototype.Parameters)
        {
            Declare(parameter, prototype.Line, 0);
        }
    }

    /// <summary>Names declared in a block; a runtime one has slots of its own, made afresh each time it is entered.</summary>
    private sealed class Scope
    {
        public Dictionary<string, int> Names { get; } = [];

        public bool Runtime { get; init; }

        public int Size { get; set; }
    }

    private sealed class Loop
    {
        /// <summary>Whether each time round has a runtime scope, to leave on break and continue.</summary>
        public bool Scoped { get; init; }

        public List<int> Breaks { get; } = [];

        public List<int> Continues { get; } = [];
    }

    public static FunctionPrototype CompileScript(IReadOnlyList<Statement> body, string source)
    {
        var prototype = NewPrototype("(script)", source, 1, []);
        var compiler = new Compiler(null, prototype, topLevel: true);
        compiler.Body(body);
        return compiler.Finish();
    }

    public static FunctionPrototype CompileExpression(Expression expression, string source)
    {
        var prototype = NewPrototype("(expression)", source, expression.Line, []);
        var compiler = new Compiler(null, prototype, topLevel: true);
        compiler.Compile(expression);
        compiler.Emit(OpCode.Return, expression.Line);
        return compiler.Finish();
    }

    private static FunctionPrototype NewPrototype(string name, string source, int line, IReadOnlyList<string> parameters) => new()
    {
        Name = name,
        Source = source,
        Line = line,
        ParameterCount = parameters.Count,
        Parameters = parameters,
    };

    private FunctionPrototype Finish()
    {
        _prototype.Code = [.. _code];
        _prototype.Lines = [.. _lines];
        _prototype.Constants = [.. _constants];
        _prototype.Functions = [.. _functions];
        return _prototype;
    }

    private void Body(IReadOnlyList<Statement> body)
    {
        foreach (var statement in body)
        {
            Compile(statement);
        }

        var line = body.Count > 0 ? body[^1].Line : _prototype.Line;
        Emit(OpCode.Nil, line);
        Emit(OpCode.Return, line);
    }

    private int Emit(OpCode op, int line, int a = 0, int b = 0)
    {
        _code.Add(new Instruction(op, a, b));
        _lines.Add(line);
        return _code.Count - 1;
    }

    private void Patch(int at, int target) => _code[at] = _code[at] with { A = target };

    private int Here => _code.Count;

    private int Constant(ScriptValue value)
    {
        if (!_constantIndex.TryGetValue(value, out var index))
        {
            index = _constants.Count;
            _constants.Add(value);
            _constantIndex[value] = index;
        }

        return index;
    }

    /// <summary>Whether a declaration here makes a global of the script rather than a local.</summary>
    private bool AtTop => _topLevel && _scopes.Count == 1;

    private int Declare(string name, int line, int column)
    {
        var scope = _scopes[^1];
        if (scope.Names.ContainsKey(name))
        {
            throw new ScriptSyntaxException($"'{name}' is already declared here", _prototype.Source, line, column);
        }

        // The slot is in the innermost runtime scope, or else the function's own.
        var owner = _scopes.LastOrDefault(s => s.Runtime);
        var slot = owner is null ? _prototype.LocalCount++ : owner.Size++;
        scope.Names[name] = slot;
        return slot;
    }

    private (int Depth, int Slot)? Resolve(string name)
    {
        // Each runtime scope between here and the one holding the name's slot is a step out.
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].Names.TryGetValue(name, out var slot))
            {
                var owner = _scopes.FindLastIndex(i, s => s.Runtime);
                return (_scopes.Skip(owner + 1).Count(s => s.Runtime), slot);
            }
        }

        return _parent?.Resolve(name) is { } outer ? (outer.Depth + 1, outer.Slot) : null;
    }

    private void Scoped(IReadOnlyList<Statement> body)
    {
        _scopes.Add(new Scope());
        foreach (var statement in body)
        {
            Compile(statement);
        }

        _scopes.RemoveAt(_scopes.Count - 1);
    }

    private void Compile(Statement statement)
    {
        switch (statement)
        {
            case LetStatement let:
                if (let.Value is null)
                {
                    Emit(OpCode.Nil, let.Line);
                }
                else
                {
                    Compile(let.Value);
                }

                if (AtTop)
                {
                    Emit(OpCode.DefineGlobal, let.Line, Constant(let.Name));
                }
                else
                {
                    Emit(OpCode.Store, let.Line, 0, Declare(let.Name, let.Line, let.Column));
                }

                break;
            case FunctionStatement function when AtTop:
                Compile(function.Function);
                Emit(OpCode.DefineGlobal, function.Line, Constant(function.Function.Name));
                break;
            case FunctionStatement function:
                var slot = Declare(function.Function.Name, function.Line, function.Column);
                Compile(function.Function);
                Emit(OpCode.Store, function.Line, 0, slot);
                break;
            case AssignStatement assign:
                Assign(assign);
                break;
            case ExpressionStatement expression:
                Compile(expression.Expression);
                Emit(OpCode.Pop, expression.Line);
                break;
            case IfStatement branches:
                If(branches);
                break;
            case WhileStatement loop:
                While(loop);
                break;
            case ForStatement loop:
                For(loop);
                break;
            case ReturnStatement result:
                if (result.Value is null)
                {
                    Emit(OpCode.Nil, result.Line);
                }
                else
                {
                    Compile(result.Value);
                }

                Emit(OpCode.Return, result.Line);
                break;
            case BreakStatement jump:
                if (_loops.Count == 0)
                {
                    throw new ScriptSyntaxException($"'{(jump.Continue ? "continue" : "break")}' outside a loop", _prototype.Source, jump.Line, 1);
                }

                if (_loops[^1].Scoped)
                {
                    Emit(OpCode.ExitScope, jump.Line);
                }

                var at = Emit(OpCode.Jump, jump.Line);
                (jump.Continue ? _loops[^1].Continues : _loops[^1].Breaks).Add(at);
                break;
        }
    }

    private void Assign(AssignStatement assign)
    {
        var operation = assign.Operator switch
        {
            TokenKind.PlusAssign => OpCode.Add,
            TokenKind.MinusAssign => OpCode.Subtract,
            TokenKind.StarAssign => OpCode.Multiply,
            TokenKind.SlashAssign => OpCode.Divide,
            _ => (OpCode?)null,
        };
        var line = assign.Line;
        switch (assign.Target)
        {
            case NameExpression name:
                var local = Resolve(name.Name);
                if (operation is { } before)
                {
                    LoadName(name.Name, local, line);
                    Compile(assign.Value);
                    Emit(before, line);
                }
                else
                {
                    Compile(assign.Value);
                }

                if (local is { } found)
                {
                    Emit(OpCode.Store, line, found.Depth, found.Slot);
                }
                else
                {
                    Emit(OpCode.StoreGlobal, line, Constant(name.Name));
                }

                break;
            case FieldExpression { Target: FieldExpression or IndexExpression } or IndexExpression { Target: FieldExpression or IndexExpression }:
                AssignPath(assign.Target, assign.Value, operation, line);
                break;
            case FieldExpression field:
                Compile(field.Target);
                if (operation is { } fieldOperation)
                {
                    Emit(OpCode.Duplicate, line);
                    Emit(OpCode.GetField, line, Constant(field.Name));
                    Compile(assign.Value);
                    Emit(fieldOperation, line);
                }
                else
                {
                    Compile(assign.Value);
                }

                Emit(OpCode.SetField, line, Constant(field.Name));
                break;
            case IndexExpression index:
                Compile(index.Target);
                Compile(index.Key);
                if (operation is { } indexOperation)
                {
                    Emit(OpCode.DuplicateTwo, line);
                    Emit(OpCode.GetIndex, line);
                    Compile(assign.Value);
                    Emit(indexOperation, line);
                }
                else
                {
                    Compile(assign.Value);
                }

                Emit(OpCode.SetIndex, line);
                break;
        }
    }

    /// <summary>
    /// <c>a.b[i].c = v</c>, any number of steps deep: each step read in turn and kept on the stack (with its key, for an
    /// index), the last set, then each put back into the one before, from the end. A step may be one of the game's
    /// structs, read as a copy, so the change only sticks when the copies go back.
    /// </summary>
    private void AssignPath(Expression target, Expression value, OpCode? operation, int line)
    {
        // The steps from the root out: fields and indexes.
        var steps = new List<Expression>();
        var root = target;
        while (root is FieldExpression or IndexExpression)
        {
            steps.Add(root);
            root = root is FieldExpression field ? field.Target : ((IndexExpression)root).Target;
        }

        steps.Reverse();
        Compile(root);
        foreach (var step in steps.Take(steps.Count - 1))
        {
            Read(step);
        }

        // The last step set on the last thing read, a copy of it kept to put back.
        Emit(OpCode.Duplicate, line);
        switch (steps[^1])
        {
            case FieldExpression last:
                if (operation is { } fieldOperation)
                {
                    Emit(OpCode.Duplicate, line);
                    Emit(OpCode.GetField, line, Constant(last.Name));
                    Compile(value);
                    Emit(fieldOperation, line);
                }
                else
                {
                    Compile(value);
                }

                Emit(OpCode.SetField, line, Constant(last.Name));
                break;
            case IndexExpression last:
                Compile(last.Key);
                if (operation is { } indexOperation)
                {
                    Emit(OpCode.DuplicateTwo, line);
                    Emit(OpCode.GetIndex, line);
                    Compile(value);
                    Emit(indexOperation, line);
                }
                else
                {
                    Compile(value);
                }

                Emit(OpCode.SetIndex, line);
                break;
        }

        for (var i = steps.Count - 2; i >= 0; i--)
        {
            if (steps[i] is FieldExpression field)
            {
                Emit(OpCode.PutBackField, line, Constant(field.Name));
            }
            else
            {
                Emit(OpCode.PutBackIndex, line);
            }
        }

        Emit(OpCode.Pop, line);

        // A step read on the way: [object] becomes [object, value], or [object, key, value] for an index.
        void Read(Expression step)
        {
            if (step is FieldExpression field)
            {
                Emit(OpCode.Duplicate, line);
                Emit(OpCode.GetField, line, Constant(field.Name));
            }
            else
            {
                Compile(((IndexExpression)step).Key);
                Emit(OpCode.DuplicateTwo, line);
                Emit(OpCode.GetIndex, line);
            }
        }
    }

    private void If(IfStatement branches)
    {
        var ends = new List<int>();
        foreach (var (condition, body) in branches.Branches)
        {
            Compile(condition);
            var skip = Emit(OpCode.JumpIfFalse, condition.Line);
            Scoped(body);
            ends.Add(Emit(OpCode.Jump, condition.Line));
            Patch(skip, Here);
        }

        if (branches.Otherwise is { } otherwise)
        {
            Scoped(otherwise);
        }

        foreach (var end in ends)
        {
            Patch(end, Here);
        }
    }

    private void While(WhileStatement loop)
    {
        var start = Here;
        Compile(loop.Condition);
        var exit = Emit(OpCode.JumpIfFalse, loop.Line);
        var record = new Loop { Scoped = loop.MakesFunctions };
        _loops.Add(record);
        if (loop.MakesFunctions)
        {
            // A scope of its own each time round, so functions made in it keep that time's variables.
            var enter = Emit(OpCode.EnterScope, loop.Line);
            var scope = new Scope { Runtime = true };
            _scopes.Add(scope);
            Scoped(loop.Body);
            _scopes.RemoveAt(_scopes.Count - 1);
            _code[enter] = _code[enter] with { A = scope.Size };
            Emit(OpCode.ExitScope, loop.Line);
        }
        else
        {
            Scoped(loop.Body);
        }

        _loops.RemoveAt(_loops.Count - 1);
        Emit(OpCode.Jump, loop.Line, start);
        Patch(exit, Here);
        record.Breaks.ForEach(at => Patch(at, Here));
        record.Continues.ForEach(at => Patch(at, start));
    }

    private void For(ForStatement loop)
    {
        Compile(loop.Source);
        Emit(OpCode.IterateStart, loop.Line);
        var scope = new Scope { Runtime = loop.MakesFunctions };
        var start = Emit(OpCode.IterateNext, loop.Line, 0, loop.Second is null ? 1 : 2);
        var enter = loop.MakesFunctions ? Emit(OpCode.EnterScope, loop.Line) : -1;
        _scopes.Add(scope);
        var first = Declare(loop.First, loop.Line, loop.Column);
        var second = loop.Second is null ? -1 : Declare(loop.Second, loop.Line, loop.Column);
        if (second >= 0)
        {
            Emit(OpCode.Store, loop.Line, 0, second);
        }

        Emit(OpCode.Store, loop.Line, 0, first);
        var record = new Loop { Scoped = loop.MakesFunctions };
        _loops.Add(record);
        Scoped(loop.Body);
        _loops.RemoveAt(_loops.Count - 1);
        _scopes.RemoveAt(_scopes.Count - 1);
        if (enter >= 0)
        {
            // Each time round its own variables: the loop's names and what the body declares.
            _code[enter] = _code[enter] with { A = scope.Size };
            Emit(OpCode.ExitScope, loop.Line);
        }

        Emit(OpCode.Jump, loop.Line, start);
        Patch(start, Here);
        record.Breaks.ForEach(at => Patch(at, Here));
        record.Continues.ForEach(at => Patch(at, start));
        Emit(OpCode.Pop, loop.Line);
    }

    private void LoadName(string name, (int Depth, int Slot)? local, int line)
    {
        if (local is { } found)
        {
            Emit(OpCode.Load, line, found.Depth, found.Slot);
        }
        else
        {
            Emit(OpCode.LoadGlobal, line, Constant(name));
        }
    }

    private void Compile(Expression expression)
    {
        var line = expression.Line;
        switch (expression)
        {
            case LiteralExpression { Value.Type: ScriptType.Nil }:
                Emit(OpCode.Nil, line);
                break;
            case LiteralExpression { Value.Type: ScriptType.Logical } literal:
                Emit(literal.Value.AsLogical ? OpCode.True : OpCode.False, line);
                break;
            case LiteralExpression literal:
                Emit(OpCode.Constant, line, Constant(literal.Value));
                break;
            case InterpolationExpression text:
                foreach (var part in text.Parts)
                {
                    Compile(part);
                }

                Emit(OpCode.Interpolate, line, text.Parts.Count);
                break;
            case NameExpression name:
                LoadName(name.Name, Resolve(name.Name), line);
                break;
            case ListExpression list:
                foreach (var item in list.Items)
                {
                    Compile(item);
                }

                Emit(OpCode.MakeList, line, list.Items.Count);
                break;
            case MapExpression map:
                foreach (var (key, value) in map.Entries)
                {
                    Compile(key);
                    Compile(value);
                }

                Emit(OpCode.MakeMap, line, map.Entries.Count);
                break;
            case UnaryExpression unary:
                Compile(unary.Operand);
                Emit(unary.Operator == TokenKind.Not ? OpCode.Not : OpCode.Negate, line);
                break;
            case BinaryExpression { Operator: TokenKind.And or TokenKind.Or } logical:
                Compile(logical.Left);
                var skip = Emit(logical.Operator == TokenKind.And ? OpCode.JumpIfFalseOrPop : OpCode.JumpIfTrueOrPop, line);
                Compile(logical.Right);
                Patch(skip, Here);
                break;
            case BinaryExpression binary:
                Compile(binary.Left);
                Compile(binary.Right);
                Emit(binary.Operator switch
                {
                    TokenKind.Plus => OpCode.Add,
                    TokenKind.Minus => OpCode.Subtract,
                    TokenKind.Star => OpCode.Multiply,
                    TokenKind.Slash => OpCode.Divide,
                    TokenKind.Percent => OpCode.Modulo,
                    TokenKind.Equal => OpCode.Equal,
                    TokenKind.NotEqual => OpCode.NotEqual,
                    TokenKind.Less => OpCode.Less,
                    TokenKind.LessEqual => OpCode.LessEqual,
                    TokenKind.Greater => OpCode.Greater,
                    _ => OpCode.GreaterEqual,
                }, line);
                break;
            case CallExpression call:
                Compile(call.Callee);
                foreach (var argument in call.Arguments)
                {
                    Compile(argument);
                }

                Emit(OpCode.Call, line, call.Arguments.Count);
                break;
            case FieldExpression field:
                Compile(field.Target);
                Emit(OpCode.GetField, line, Constant(field.Name));
                break;
            case IndexExpression index:
                Compile(index.Target);
                Compile(index.Key);
                Emit(OpCode.GetIndex, line);
                break;
            case FunctionExpression function:
                var prototype = NewPrototype(function.Name, _prototype.Source, function.Line, function.Parameters);
                var inner = new Compiler(this, prototype, topLevel: false);
                inner.Body(function.Body);
                _functions.Add(inner.Finish());
                Emit(OpCode.Closure, line, _functions.Count - 1);
                break;
        }
    }
}
