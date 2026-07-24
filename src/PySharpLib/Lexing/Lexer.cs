using PySharpLib;
using System.Text;

namespace PySharpLib.Lexing;

/// <summary>
/// Lexer for a subset of Python 3.x: produces the token stream including
/// NEWLINE/INDENT/DEDENT, with implicit continuation inside parentheses and explicit with backslash.
/// </summary>
public sealed class Lexer
{
    private static readonly HashSet<string> Keywords = new()
    {
        "False", "None", "True", "and", "as", "assert", "async", "await",
        "break", "class", "continue", "def", "del", "elif", "else", "except",
        "finally", "for", "from", "global", "if", "import", "in", "is",
        "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
        "while", "with", "yield",
    };

    // Sorted by decreasing length: always try the longest match.
    private static readonly string[] Operators =
    {
        "**=", "//=", ">>=", "<<=", "...",
        "!=", ">=", "<=", "==", "->", ":=", "+=", "-=", "*=", "/=", "%=",
        "&=", "|=", "^=", "@=", "**", "//", ">>", "<<",
        "+", "-", "*", "/", "%", "@", "&", "|", "^", "~", "<", ">",
        "(", ")", "[", "]", "{", "}", ",", ":", ".", ";", "=",
    };

    private const int TabSize = 8;

    private readonly string _src;
    private readonly string _fileName;
    private readonly List<Token> _tokens = new();
    private readonly Stack<int> _indents = new();

    private int _pos;
    private int _line = 1;
    private int _col = 1;
    private int _parenDepth;
    private bool _atLineStart = true;

    public Lexer(string source, string fileName = "<string>")
    {
        // Normalize line endings to simplify everything else.
        _src = source.Replace("\r\n", "\n").Replace('\r', '\n');
        _fileName = fileName;
        _indents.Push(0);
    }

    public static List<Token> Tokenize(string source, string fileName = "<string>")
        => new Lexer(source, fileName).Run();

    public List<Token> Run()
    {
        while (true)
        {
            if (_atLineStart && _parenDepth == 0)
            {
                if (!HandleLineStart())
                    break; // EOF reached on an empty/comment line
            }

            SkipSpacesAndComment();

            if (AtEnd)
                break;

            char c = Peek;

            if (c == '\n')
            {
                if (_parenDepth > 0)
                {
                    // Implicit continuation: the newline is whitespace.
                    Advance();
                    continue;
                }
                Emit(TokenKind.Newline, "\n");
                Advance();
                _atLineStart = true;
                continue;
            }

            if (c == '\\' && PeekAt(1) == '\n')
            {
                // Explicit line continuation.
                Advance();
                Advance();
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && char.IsDigit(PeekAt(1))))
            {
                LexNumber();
                continue;
            }

            if (IsIdentStart(c))
            {
                if (TryLexStringPrefix())
                    continue;
                LexNameOrKeyword();
                continue;
            }

            if (c == '"' || c == '\'')
            {
                LexString(prefix: "");
                continue;
            }

            LexOperator();
        }

        // Closing: implicit NEWLINE if the last logical line is unterminated.
        if (_tokens.Count > 0 && _tokens[^1].Kind is not (TokenKind.Newline or TokenKind.Dedent))
            Emit(TokenKind.Newline, "\n");
        while (_indents.Peek() > 0)
        {
            _indents.Pop();
            Emit(TokenKind.Dedent, "");
        }
        Emit(TokenKind.EndOfFile, "");
        return _tokens;
    }

    // ------------------------------------------------------------------ indentation

    /// <summary>Handles line start: skips empty/comment lines, emits INDENT/DEDENT. False at EOF.</summary>
    private bool HandleLineStart()
    {
        while (true)
        {
            int indent = 0;
            while (!AtEnd && (Peek == ' ' || Peek == '\t'))
            {
                indent = Peek == '\t' ? (indent / TabSize + 1) * TabSize : indent + 1;
                Advance();
            }

            if (AtEnd)
                return false;

            if (Peek == '\n')
            {
                Advance(); // empty line: no token
                continue;
            }
            if (Peek == '#')
            {
                while (!AtEnd && Peek != '\n') Advance();
                continue; // comment-only line
            }

            if (indent > _indents.Peek())
            {
                _indents.Push(indent);
                Emit(TokenKind.Indent, "");
            }
            else
            {
                while (indent < _indents.Peek())
                {
                    _indents.Pop();
                    Emit(TokenKind.Dedent, "");
                }
                if (indent != _indents.Peek())
                    throw Error("unindent does not match any outer indentation level");
            }

            _atLineStart = false;
            return true;
        }
    }

    // ------------------------------------------------------------------ simple tokens

    private void SkipSpacesAndComment()
    {
        while (!AtEnd && (Peek == ' ' || Peek == '\t'))
            Advance();
        if (!AtEnd && Peek == '#')
            while (!AtEnd && Peek != '\n')
                Advance();
    }

    private void LexNameOrKeyword()
    {
        int startLine = _line, startCol = _col, start = _pos;
        while (!AtEnd && IsIdentPart(Peek))
            Advance();
        string text = _src[start.._pos];
        _tokens.Add(new Token(Keywords.Contains(text) ? TokenKind.Keyword : TokenKind.Name,
            text, startLine, startCol));
    }

    private void LexNumber()
    {
        int startLine = _line, startCol = _col, start = _pos;

        if (Peek == '0' && (PeekAt(1) is 'x' or 'X' or 'o' or 'O' or 'b' or 'B'))
        {
            Advance();
            Advance();
            while (!AtEnd && (char.IsLetterOrDigit(Peek) || Peek == '_'))
                Advance();
        }
        else
        {
            while (!AtEnd && (char.IsDigit(Peek) || Peek == '_'))
                Advance();
            if (!AtEnd && Peek == '.' && char.IsDigit(PeekAt(1)))
            {
                Advance();
                while (!AtEnd && (char.IsDigit(Peek) || Peek == '_'))
                    Advance();
            }
            else if (!AtEnd && Peek == '.' && !IsIdentStart(PeekAt(1)) && PeekAt(1) != '.')
            {
                // "1." → float (but not "1..x" nor "1.foo")
                Advance();
            }
            if (!AtEnd && (Peek is 'e' or 'E'))
            {
                int save = _pos;
                Advance();
                if (!AtEnd && (Peek is '+' or '-'))
                    Advance();
                if (!AtEnd && char.IsDigit(Peek))
                {
                    while (!AtEnd && (char.IsDigit(Peek) || Peek == '_'))
                        Advance();
                }
                else
                {
                    _pos = save; // it was not an exponent (e.g. "1else" does not exist, but "1e" alone is an error handled in the parser)
                }
            }
        }

        _tokens.Add(new Token(TokenKind.Number, _src[start.._pos], startLine, startCol));
    }

    private void LexOperator()
    {
        foreach (string op in Operators)
        {
            if (Matches(op))
            {
                if (op is "(" or "[" or "{")
                    _parenDepth++;
                else if (op is ")" or "]" or "}")
                    _parenDepth = Math.Max(0, _parenDepth - 1);

                Emit(TokenKind.Op, op);
                for (int i = 0; i < op.Length; i++)
                    Advance();
                return;
            }
        }
        throw Error($"invalid character '{Peek}'");
    }

    // ------------------------------------------------------------------ strings

    /// <summary>If the current position has a string prefix (r/b/f/u and combinations) followed by a quote, lex the string.</summary>
    private bool TryLexStringPrefix()
    {
        // candidate prefix = letters before the quote
        int p = 0;
        while (p < 3 && PeekAt(p) != '\0' && IsIdentPart(PeekAt(p)))
            p++;
        if (p == 0 || p > 2)
            return false;
        char q = PeekAt(p);
        if (q != '"' && q != '\'')
            return false;

        string prefix = _src.Substring(_pos, p).ToLowerInvariant();
        foreach (char c in prefix)
            if (c is not ('r' or 'b' or 'f' or 'u'))
                return false;
        if (prefix.Contains('b') && prefix.Contains('f'))
            return false;

        for (int i = 0; i < p; i++)
            Advance();
        LexString(prefix);
        return true;
    }

    private void LexString(string prefix)
    {
        int startLine = _line, startCol = _col;
        bool isRaw = prefix.Contains('r');
        bool isBytes = prefix.Contains('b');
        bool isFString = prefix.Contains('f');

        char quote = Peek;
        Advance();
        bool triple = Peek == quote && PeekAt(1) == quote;
        if (triple)
        {
            Advance();
            Advance();
        }

        var raw = new StringBuilder();
        while (true)
        {
            if (AtEnd)
                throw Error(triple ? "EOF in multi-line string" : "EOL while scanning string literal");

            char c = Peek;
            if (!triple && c == '\n')
                throw Error("EOL while scanning string literal");

            if (c == quote)
            {
                if (!triple)
                {
                    Advance();
                    break;
                }
                if (PeekAt(1) == quote && PeekAt(2) == quote)
                {
                    Advance();
                    Advance();
                    Advance();
                    break;
                }
                raw.Append(c);
                Advance();
                continue;
            }

            if (c == '\\')
            {
                // Keep the sequence; decoding happens later (or never, for raw/f-strings).
                raw.Append(c);
                Advance();
                if (AtEnd)
                    throw Error("EOF in string literal");
                raw.Append(Peek);
                Advance();
                continue;
            }

            raw.Append(c);
            Advance();
        }

        string rawText = raw.ToString();

        if (isBytes)
        {
            byte[] value = DecodeBytes(rawText, isRaw, startLine, startCol);
            _tokens.Add(new Token(TokenKind.Bytes, rawText, startLine, startCol) { BytesValue = value });
            return;
        }

        if (isFString)
        {
            // Raw content: parsing of {expr} and escapes happens in the parser.
            _tokens.Add(new Token(TokenKind.Str, rawText, startLine, startCol)
            {
                StringValue = rawText,
                IsFString = true,
                IsRaw = isRaw,
            });
            return;
        }

        string decoded = isRaw ? rawText : DecodeEscapes(rawText, _fileName, startLine, startCol);
        _tokens.Add(new Token(TokenKind.Str, rawText, startLine, startCol) { StringValue = decoded });
    }

    /// <summary>Decodes the escapes of a text string (\n, \xhh, \uXXXX, octals, ...).</summary>
    public static string DecodeEscapes(string s, string fileName, int line, int col)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c != '\\')
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (i + 1 >= s.Length)
                throw new PySyntaxError("EOF in string literal", fileName, line, col);
            char e = s[i + 1];
            i += 2;
            switch (e)
            {
                case '\n': break; // line continuation inside a string
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'x':
                    sb.Append((char)ReadHex(s, ref i, 2, fileName, line, col));
                    break;
                case 'u':
                    sb.Append((char)ReadHex(s, ref i, 4, fileName, line, col));
                    break;
                case 'U':
                    sb.Append(char.ConvertFromUtf32(ReadHex(s, ref i, 8, fileName, line, col)));
                    break;
                case >= '0' and <= '7':
                {
                    int v = e - '0';
                    for (int k = 0; k < 2 && i < s.Length && s[i] is >= '0' and <= '7'; k++, i++)
                        v = v * 8 + (s[i] - '0');
                    sb.Append((char)v);
                    break;
                }
                default:
                    // Like CPython: unknown escape left literal.
                    sb.Append('\\').Append(e);
                    break;
            }
        }
        return sb.ToString();
    }

    private byte[] DecodeBytes(string s, bool isRaw, int line, int col)
    {
        var bytes = new List<byte>(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c > 0xFF)
                throw new PySyntaxError("bytes can only contain ASCII literal characters", _fileName, line, col);
            if (isRaw || c != '\\')
            {
                bytes.Add((byte)c);
                i++;
                continue;
            }
            if (i + 1 >= s.Length)
                throw new PySyntaxError("EOF in bytes literal", _fileName, line, col);
            char e = s[i + 1];
            i += 2;
            switch (e)
            {
                case '\n': break;
                case '\\': bytes.Add((byte)'\\'); break;
                case '\'': bytes.Add((byte)'\''); break;
                case '"': bytes.Add((byte)'"'); break;
                case 'a': bytes.Add(7); break;
                case 'b': bytes.Add(8); break;
                case 'f': bytes.Add(12); break;
                case 'n': bytes.Add(10); break;
                case 'r': bytes.Add(13); break;
                case 't': bytes.Add(9); break;
                case 'v': bytes.Add(11); break;
                case 'x':
                    bytes.Add((byte)ReadHex(s, ref i, 2, _fileName, line, col));
                    break;
                case >= '0' and <= '7':
                {
                    int v = e - '0';
                    for (int k = 0; k < 2 && i < s.Length && s[i] is >= '0' and <= '7'; k++, i++)
                        v = v * 8 + (s[i] - '0');
                    bytes.Add((byte)v);
                    break;
                }
                default:
                    bytes.Add((byte)'\\');
                    bytes.Add((byte)e);
                    break;
            }
        }
        return bytes.ToArray();
    }

    private static int ReadHex(string s, ref int i, int digits, string fileName, int line, int col)
    {
        if (i + digits > s.Length)
            throw new PySyntaxError("truncated escape sequence", fileName, line, col);
        int v = 0;
        for (int k = 0; k < digits; k++, i++)
        {
            if (!Uri.IsHexDigit(s[i]))
                throw new PySyntaxError("invalid hex digit in escape sequence", fileName, line, col);
            v = v * 16 + Uri.FromHex(s[i]);
        }
        return v;
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private bool AtEnd => _pos >= _src.Length;
    private char Peek => _src[_pos];

    private char PeekAt(int offset)
        => _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

    private bool Matches(string s)
        => _pos + s.Length <= _src.Length && _src.AsSpan(_pos, s.Length).SequenceEqual(s);

    private void Advance()
    {
        if (_src[_pos] == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        _pos++;
    }

    private void Emit(TokenKind kind, string text)
        => _tokens.Add(new Token(kind, text, _line, _col));

    private PySyntaxError Error(string message)
        => new(message, _fileName, _line, _col);
}
