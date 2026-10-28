namespace Jentl.Script;

// The script as the parser reads it, before the compiler turns it into instructions.

internal abstract record Expression(int Line);

internal sealed record LiteralExpression(ScriptValue Value, int Line) : Expression(Line);

internal sealed record InterpolationExpression(IReadOnlyList<Expression> Parts, int Line) : Expression(Line);

internal sealed record NameExpression(string Name, int Line, int Column) : Expression(Line);

internal sealed record ListExpression(IReadOnlyList<Expression> Items, int Line) : Expression(Line);

internal sealed record MapExpression(IReadOnlyList<(Expression Key, Expression Value)> Entries, int Line) : Expression(Line);

internal sealed record UnaryExpression(TokenKind Operator, Expression Operand, int Line) : Expression(Line);

internal sealed record BinaryExpression(TokenKind Operator, Expression Left, Expression Right, int Line) : Expression(Line);

internal sealed record CallExpression(Expression Callee, IReadOnlyList<Expression> Arguments, int Line) : Expression(Line);

internal sealed record FieldExpression(Expression Target, string Name, int Line) : Expression(Line);

internal sealed record IndexExpression(Expression Target, Expression Key, int Line) : Expression(Line);

internal sealed record FunctionExpression(string Name, IReadOnlyList<string> Parameters, IReadOnlyList<Statement> Body, int Line) : Expression(Line);

internal abstract record Statement(int Line);

internal sealed record LetStatement(string Name, Expression? Value, int Line, int Column) : Statement(Line);

internal sealed record FunctionStatement(FunctionExpression Function, int Column) : Statement(Function.Line);

internal sealed record AssignStatement(Expression Target, TokenKind Operator, Expression Value, int Line) : Statement(Line);

internal sealed record ExpressionStatement(Expression Expression) : Statement(Expression.Line);

internal sealed record IfStatement(IReadOnlyList<(Expression Condition, IReadOnlyList<Statement> Body)> Branches, IReadOnlyList<Statement>? Otherwise, int Line) : Statement(Line);

internal sealed record WhileStatement(Expression Condition, IReadOnlyList<Statement> Body, int Line) : Statement(Line);

internal sealed record ForStatement(string First, string? Second, Expression Source, IReadOnlyList<Statement> Body, int Line, int Column) : Statement(Line);

internal sealed record ReturnStatement(Expression? Value, int Line) : Statement(Line);

internal sealed record BreakStatement(bool Continue, int Line) : Statement(Line);
