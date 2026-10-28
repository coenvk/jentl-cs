using System.Globalization;
using System.Text;

namespace Jentl.Script;

internal enum TokenKind
{
    End,
    Number,
    Text,
    Name,

    // Keywords.
    Let,
    Fn,
    If,
    Then,
    Elif,
    Else,
    EndKeyword,
    While,
    Do,
    For,
    In,
    Return,
    Break,
    Continue,
    And,
    Or,
    Not,
    True,
    False,
    Nil,

    // Symbols.
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,
    Comma,
    Dot,
    Colon,
    Assign,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
}

/// <summary>A piece of a text literal: plain text, or the source of a <c>{expression}</c> inside it.</summary>
internal readonly record struct TextPart(string Value, bool IsCode, int Line, int Column);

internal sealed record Token(TokenKind Kind, string Text, int Line, int Column, bool NewlineBefore)
{
    public double Number { get; init; }

    /// <summary>For text literals: the parts, when it has <c>{expression}</c>s in it.</summary>
    public IReadOnlyList<TextPart>? Parts { get; init; }
}

/// <summary>Splits a script into tokens. <c>#</c> starts a comment to the end of the line.</summary>
internal sealed class Lexer
{
    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["let"] = TokenKind.Let,
        ["fn"] = TokenKind.Fn,
        ["if"] = TokenKind.If,
        ["then"] = TokenKind.Then,
        ["elif"] = TokenKind.Elif,
        ["else"] = TokenKind.Else,
        ["end"] = TokenKind.EndKeyword,
        ["while"] = TokenKind.While,
        ["do"] = TokenKind.Do,
        ["for"] = TokenKind.For,
        ["in"] = TokenKind.In,
        ["return"] = TokenKind.Return,
        ["break"] = TokenKind.Break,
        ["continue"] = TokenKind.Continue,
        ["and"] = TokenKind.And,
        ["or"] = TokenKind.Or,
        ["not"] = TokenKind.Not,
        ["true"] = TokenKind.True,
        ["false"] = TokenKind.False,
        ["nil"] = TokenKind.Nil,
    };

    private readonly string _source;
    private readonly string _name;
    private int _position;
    private int _line;
    private int _column;

    public Lexer(string source, string name, int line = 1, int column = 1)
    {
        _source = source;
        _name = name;
        _line = line;
        _column = column;
    }

    public static bool IsName(string text) =>
        text.Length > 0 && IsNameStart(text[0]) && text.All(IsNamePart) && !Keywords.ContainsKey(text);

    public List<Token> Read()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var newline = SkipSpace();
            var token = Next(newline);
            tokens.Add(token);
            if (token.Kind == TokenKind.End)
            {
                return tokens;
            }
        }
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private char Peek(int ahead = 0) => _position + ahead < _source.Length ? _source[_position + ahead] : '\0';

    private char Advance()
    {
        var c = _source[_position++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return c;
    }

    /// <summary>Skips spaces and comments; true when a line ended among them.</summary>
    private bool SkipSpace()
    {
        var newline = _position == 0;
        while (_position < _source.Length)
        {
            var c = Peek();
            if (c == '#')
            {
                while (_position < _source.Length && Peek() != '\n')
                {
                    Advance();
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                newline |= c == '\n';
                Advance();
            }
            else
            {
                break;
            }
        }

        return newline;
    }

    private Token Next(bool newline)
    {
        var line = _line;
        var column = _column;
        if (_position >= _source.Length)
        {
            return new Token(TokenKind.End, "end of script", line, column, newline);
        }

        var c = Peek();
        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
        {
            return ReadNumber(line, column, newline);
        }

        if (IsNameStart(c))
        {
            var start = _position;
            while (_position < _source.Length && IsNamePart(Peek()))
            {
                Advance();
            }

            var text = _source[start.._position];
            return new Token(Keywords.TryGetValue(text, out var keyword) ? keyword : TokenKind.Name, text, line, column, newline);
        }

        if (c is '"' or '\'')
        {
            return ReadText(line, column, newline);
        }

        var symbol = _position;
        Advance();
        var kind = c switch
        {
            '(' => TokenKind.LeftParen,
            ')' => TokenKind.RightParen,
            '[' => TokenKind.LeftBracket,
            ']' => TokenKind.RightBracket,
            '{' => TokenKind.LeftBrace,
            '}' => TokenKind.RightBrace,
            ',' => TokenKind.Comma,
            '.' => TokenKind.Dot,
            ':' => TokenKind.Colon,
            '=' => Then('=', TokenKind.Equal, TokenKind.Assign),
            '!' when Peek() == '=' => Then('=', TokenKind.NotEqual, TokenKind.NotEqual),
            '<' => Then('=', TokenKind.LessEqual, TokenKind.Less),
            '>' => Then('=', TokenKind.GreaterEqual, TokenKind.Greater),
            '+' => Then('=', TokenKind.PlusAssign, TokenKind.Plus),
            '-' => Then('=', TokenKind.MinusAssign, TokenKind.Minus),
            '*' => Then('=', TokenKind.StarAssign, TokenKind.Star),
            '/' => Then('=', TokenKind.SlashAssign, TokenKind.Slash),
            '%' => TokenKind.Percent,
            _ => throw new ScriptSyntaxException($"unexpected '{c}'", _name, line, column),
        };
        return new Token(kind, _source[symbol.._position], line, column, newline);
    }

    private TokenKind Then(char next, TokenKind ifNext, TokenKind otherwise)
    {
        if (Peek() != next)
        {
            return otherwise;
        }

        Advance();
        return ifNext;
    }

    private Token ReadNumber(int line, int column, bool newline)
    {
        var start = _position;
        while (char.IsDigit(Peek()) || Peek() == '_')
        {
            Advance();
        }

        if (Peek() == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (char.IsDigit(Peek()) || Peek() == '_')
            {
                Advance();
            }
        }

        if (Peek() is 'e' or 'E' && (char.IsDigit(Peek(1)) || (Peek(1) is '+' or '-' && char.IsDigit(Peek(2)))))
        {
            Advance();
            Advance();
            while (char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        if (IsNameStart(Peek()))
        {
            throw new ScriptSyntaxException($"unexpected '{Peek()}' after a number", _name, _line, _column);
        }

        var text = _source[start.._position];
        var number = double.Parse(text.Replace("_", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture);
        return new Token(TokenKind.Number, text, line, column, newline) { Number = number };
    }

    private Token ReadText(int line, int column, bool newline)
    {
        var quote = Advance();
        var parts = new List<TextPart>();
        var text = new StringBuilder();
        var partLine = _line;
        var partColumn = _column;
        while (true)
        {
            if (_position >= _source.Length || Peek() == '\n')
            {
                throw new ScriptSyntaxException("text without its closing quote", _name, line, column);
            }

            var c = Advance();
            if (c == quote)
            {
                break;
            }

            if (c == '\\')
            {
                text.Append(Escape());
            }
            else if (c == '{' && quote == '"')
            {
                if (text.Length > 0)
                {
                    parts.Add(new TextPart(text.ToString(), IsCode: false, partLine, partColumn));
                    text.Clear();
                }

                var codeLine = _line;
                var codeColumn = _column;
                var start = _position;
                var depth = 0;
                while (true)
                {
                    if (_position >= _source.Length || Peek() == '\n')
                    {
                        throw new ScriptSyntaxException("'{' in text without its closing '}'", _name, codeLine, codeColumn - 1);
                    }

                    var inner = Advance();
                    if (inner == '{')
                    {
                        depth++;
                    }
                    else if (inner == '}' && depth-- == 0)
                    {
                        break;
                    }
                }

                parts.Add(new TextPart(_source[start..(_position - 1)], IsCode: true, codeLine, codeColumn));
                partLine = _line;
                partColumn = _column;
            }
            else
            {
                text.Append(c);
            }
        }

        if (parts.Count == 0)
        {
            return new Token(TokenKind.Text, text.ToString(), line, column, newline);
        }

        if (text.Length > 0)
        {
            parts.Add(new TextPart(text.ToString(), IsCode: false, partLine, partColumn));
        }

        return new Token(TokenKind.Text, "", line, column, newline) { Parts = parts };
    }

    private string Escape()
    {
        if (_position >= _source.Length)
        {
            throw new ScriptSyntaxException("'\\' at the end of the script", _name, _line, _column);
        }

        var c = Advance();
        return c switch
        {
            'n' => "\n",
            't' => "\t",
            '\\' => "\\",
            '"' => "\"",
            '\'' => "'",
            '{' => "{",
            '}' => "}",
            _ => throw new ScriptSyntaxException($"unknown escape '\\{c}' (use \\n, \\t, \\\\, \\\", \\', \\{{ or \\}})", _name, _line, _column - 2),
        };
    }
}
