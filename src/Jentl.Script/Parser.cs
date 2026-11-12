namespace Jentl.Script;

/// <summary>Reads tokens into statements and expressions, by recursive descent.</summary>
internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly string _name;
    private int _position;

    // Functions made so far: a loop that makes any in its body gives each time round its own variables.
    private int _functionsMade;

    private Parser(List<Token> tokens, string name)
    {
        _tokens = tokens;
        _name = name;
    }

    public static IReadOnlyList<Statement> ParseScript(string source, string name)
    {
        var parser = new Parser(new Lexer(source, name).Read(), name);
        var body = parser.Block();
        parser.Expect(TokenKind.End, "a statement");
        return body;
    }

    public static Expression ParseExpression(string source, string name, int line = 1, int column = 1)
    {
        var parser = new Parser(new Lexer(source, name, line, column).Read(), name);
        if (parser.Current.Kind == TokenKind.End)
        {
            throw parser.Error("an expression");
        }

        var expression = parser.Expression();
        parser.Expect(TokenKind.End, "the end of the expression");
        return expression;
    }

    private Token Current => _tokens[_position];

    private Token Take() => _tokens[_position++];

    private bool Accept(TokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        _position++;
        return true;
    }

    private Token Expect(TokenKind kind, string what) => Current.Kind == kind ? Take() : throw Error(what);

    private ScriptSyntaxException Error(string expected)
    {
        var token = Current;
        var got = token.Kind switch
        {
            TokenKind.End => "the end of the script",
            TokenKind.Text => "text",
            _ => $"'{token.Text}'",
        };
        return new ScriptSyntaxException($"expected {expected}, got {got}", _name, token.Line, token.Column);
    }

    private ScriptSyntaxException ErrorAt(Token token, string reason) => new(reason, _name, token.Line, token.Column);

    private string Name(string what) => Expect(TokenKind.Name, what).Text;

    /// <summary>Statements up to the next <c>end</c>, <c>elif</c>, <c>else</c> or the end of the script.</summary>
    private List<Statement> Block()
    {
        var statements = new List<Statement>();
        while (Current.Kind is not (TokenKind.End or TokenKind.EndKeyword or TokenKind.Elif or TokenKind.Else))
        {
            statements.Add(Statement());
        }

        return statements;
    }

    private Statement Statement()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Let:
            {
                Take();
                var name = Expect(TokenKind.Name, "a name after 'let'");
                var value = Accept(TokenKind.Assign) ? Expression() : null;
                return new LetStatement(name.Text, value, token.Line, name.Column);
            }

            case TokenKind.Fn when _tokens[_position + 1].Kind == TokenKind.Name:
            {
                Take();
                var name = Take();
                return new FunctionStatement(Function(name.Text, token.Line), name.Column);
            }

            case TokenKind.If:
                return If();
            case TokenKind.While:
            {
                Take();
                var condition = Expression();
                Expect(TokenKind.Do, "'do' after the condition of 'while'");
                var made = _functionsMade;
                var body = Block();
                Expect(TokenKind.EndKeyword, "'end' to close 'while'");
                return new WhileStatement(condition, body, token.Line, _functionsMade > made);
            }

            case TokenKind.For:
            {
                Take();
                var first = Expect(TokenKind.Name, "a name after 'for'");
                var second = Accept(TokenKind.Comma) ? Name("a second name after ','") : null;
                Expect(TokenKind.In, "'in' after the names of 'for'");
                var source = Expression();
                Expect(TokenKind.Do, "'do' after 'for ... in ...'");
                var made = _functionsMade;
                var body = Block();
                Expect(TokenKind.EndKeyword, "'end' to close 'for'");
                return new ForStatement(first.Text, second, source, body, token.Line, first.Column, _functionsMade > made);
            }

            case TokenKind.Return:
            {
                Take();
                var value = Current.Kind is TokenKind.End or TokenKind.EndKeyword or TokenKind.Elif or TokenKind.Else || Current.NewlineBefore
                    ? null
                    : Expression();
                return new ReturnStatement(value, token.Line);
            }

            case TokenKind.Break:
            case TokenKind.Continue:
                Take();
                return new BreakStatement(token.Kind == TokenKind.Continue, token.Line);
        }

        var expression = Expression();
        if (Current.Kind is TokenKind.Assign or TokenKind.PlusAssign or TokenKind.MinusAssign or TokenKind.StarAssign or TokenKind.SlashAssign)
        {
            var operation = Take();
            if (expression is not (NameExpression or FieldExpression or IndexExpression))
            {
                throw ErrorAt(operation, $"can't assign to this; '{operation.Text}' needs a name, a field or an index on its left");
            }

            return new AssignStatement(expression, operation.Kind, Expression(), operation.Line);
        }

        return expression is CallExpression
            ? new ExpressionStatement(expression)
            : throw ErrorAt(token, "this does nothing on its own; a statement is a call, an assignment, 'let', 'fn', 'if', 'while', 'for' or 'return'");
    }

    private IfStatement If()
    {
        var line = Take().Line;
        var branches = new List<(Expression, IReadOnlyList<Statement>)>();
        List<Statement>? otherwise = null;
        while (true)
        {
            var condition = Expression();
            Expect(TokenKind.Then, "'then' after the condition of 'if'");
            branches.Add((condition, Block()));
            if (Accept(TokenKind.Elif))
            {
                continue;
            }

            if (Accept(TokenKind.Else))
            {
                otherwise = Block();
            }

            Expect(TokenKind.EndKeyword, "'end' to close 'if'");
            return new IfStatement(branches, otherwise, line);
        }
    }

    private FunctionExpression Function(string name, int line)
    {
        Expect(TokenKind.LeftParen, "'(' before the parameters");
        var parameters = new List<string>();
        if (!Accept(TokenKind.RightParen))
        {
            do
            {
                var parameter = Expect(TokenKind.Name, "a parameter name");
                if (parameters.Contains(parameter.Text))
                {
                    throw ErrorAt(parameter, $"'{parameter.Text}' is already a parameter");
                }

                parameters.Add(parameter.Text);
            }
            while (Accept(TokenKind.Comma));

            Expect(TokenKind.RightParen, "')' after the parameters");
        }

        var body = Block();
        Expect(TokenKind.EndKeyword, $"'end' to close fn {name}");
        _functionsMade++;
        return new FunctionExpression(name, parameters, body, line);
    }

    private Expression Expression() => Or();

    private Expression Or()
    {
        var left = And();
        while (Current.Kind == TokenKind.Or)
        {
            var line = Take().Line;
            left = new BinaryExpression(TokenKind.Or, left, And(), line);
        }

        return left;
    }

    private Expression And()
    {
        var left = Not();
        while (Current.Kind == TokenKind.And)
        {
            var line = Take().Line;
            left = new BinaryExpression(TokenKind.And, left, Not(), line);
        }

        return left;
    }

    private Expression Not()
    {
        if (Current.Kind == TokenKind.Not)
        {
            var line = Take().Line;
            return new UnaryExpression(TokenKind.Not, Not(), line);
        }

        return Comparison();
    }

    private Expression Comparison()
    {
        var left = Sum();
        while (Current.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            var operation = Take();
            left = new BinaryExpression(operation.Kind, left, Sum(), operation.Line);
        }

        return left;
    }

    private Expression Sum()
    {
        var left = Product();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var operation = Take();
            left = new BinaryExpression(operation.Kind, left, Product(), operation.Line);
        }

        return left;
    }

    private Expression Product()
    {
        var left = Unary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var operation = Take();
            left = new BinaryExpression(operation.Kind, left, Unary(), operation.Line);
        }

        return left;
    }

    private Expression Unary()
    {
        if (Current.Kind == TokenKind.Minus)
        {
            var line = Take().Line;
            var operand = Unary();
            return operand is LiteralExpression { Value.Type: ScriptType.Number } literal
                ? new LiteralExpression(-literal.Value.AsNumber, line)
                : new UnaryExpression(TokenKind.Minus, operand, line);
        }

        return Postfix(Primary());
    }

    /// <summary>
    /// Calls, indexes and fields after a value. A '(' or '[' at the start of a line begins a new statement rather than
    /// calling or indexing what is on the line before.
    /// </summary>
    private Expression Postfix(Expression target)
    {
        while (true)
        {
            var token = Current;
            if (token.Kind == TokenKind.LeftParen && !token.NewlineBefore)
            {
                Take();
                var arguments = new List<Expression>();
                if (!Accept(TokenKind.RightParen))
                {
                    do
                    {
                        arguments.Add(Expression());
                    }
                    while (Accept(TokenKind.Comma));

                    Expect(TokenKind.RightParen, "')' after the arguments");
                }

                target = new CallExpression(target, arguments, token.Line);
            }
            else if (token.Kind == TokenKind.LeftBracket && !token.NewlineBefore)
            {
                Take();
                var key = Expression();
                Expect(TokenKind.RightBracket, "']' after the index");
                target = new IndexExpression(target, key, token.Line);
            }
            else if (token.Kind == TokenKind.Dot)
            {
                Take();
                target = new FieldExpression(target, Name("a field name after '.'"), token.Line);
            }
            else
            {
                return target;
            }
        }
    }

    private Expression Primary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Take();
                return new LiteralExpression(token.Number, token.Line);
            case TokenKind.Text:
                Take();
                return token.Parts is null ? new LiteralExpression(token.Text, token.Line) : Interpolation(token);
            case TokenKind.True:
                Take();
                return new LiteralExpression(true, token.Line);
            case TokenKind.False:
                Take();
                return new LiteralExpression(false, token.Line);
            case TokenKind.Nil:
                Take();
                return new LiteralExpression(ScriptValue.Nil, token.Line);
            case TokenKind.Name:
                Take();
                return new NameExpression(token.Text, token.Line, token.Column);
            case TokenKind.Fn:
                Take();
                return Function("(anonymous)", token.Line);
            case TokenKind.LeftParen:
            {
                Take();
                var inner = Expression();
                Expect(TokenKind.RightParen, "')'");
                return inner;
            }

            case TokenKind.LeftBracket:
            {
                Take();
                var items = new List<Expression>();
                while (!Accept(TokenKind.RightBracket))
                {
                    items.Add(Expression());
                    if (!Accept(TokenKind.Comma))
                    {
                        Expect(TokenKind.RightBracket, "',' or ']' in the list");
                        break;
                    }
                }

                return new ListExpression(items, token.Line);
            }

            case TokenKind.LeftBrace:
                return Map();
            default:
                throw Error("a value");
        }
    }

    /// <summary><c>{name: value, "any text": value, [expression]: value}</c>.</summary>
    private MapExpression Map()
    {
        var line = Take().Line;
        var entries = new List<(Expression, Expression)>();
        while (!Accept(TokenKind.RightBrace))
        {
            var token = Current;
            Expression key;
            if (Accept(TokenKind.LeftBracket))
            {
                key = Expression();
                Expect(TokenKind.RightBracket, "']' after the key");
            }
            else if (token.Kind == TokenKind.Name || (token.Kind == TokenKind.Text && token.Parts is null))
            {
                Take();
                key = new LiteralExpression(token.Text, token.Line);
            }
            else
            {
                throw Error("a key (a name, text or [expression])");
            }

            Expect(TokenKind.Colon, "':' after the key");
            entries.Add((key, Expression()));
            if (!Accept(TokenKind.Comma))
            {
                Expect(TokenKind.RightBrace, "',' or '}' in the map");
                break;
            }
        }

        return new MapExpression(entries, line);
    }

    private InterpolationExpression Interpolation(Token token)
    {
        var parts = token.Parts!
            .Select(part => part.IsCode
                ? ParseExpression(part.Value, _name, part.Line, part.Column)
                : new LiteralExpression(part.Value, part.Line))
            .ToList();
        return new InterpolationExpression(parts, token.Line);
    }
}
