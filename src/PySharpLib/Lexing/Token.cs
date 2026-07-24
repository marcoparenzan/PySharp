// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Lexing;

/// <summary>Un token prodotto dal lexer. Posizione 1-based.</summary>
public sealed class Token
{
    public TokenKind Kind { get; }
    /// <summary>Source lexeme (for Name/Keyword/Number/Op) or canonical text.</summary>
    public string Text { get; }
    public int Line { get; }
    public int Column { get; }

    /// <summary>Decoded value for Str (escapes processed; for f-strings the raw content).</summary>
    public string? StringValue { get; init; }
    /// <summary>Value for Bytes.</summary>
    public byte[]? BytesValue { get; init; }
    /// <summary>True if the string literal is an f-string.</summary>
    public bool IsFString { get; init; }
    /// <summary>True if the literal is raw (relevant for f-strings, whose escapes are processed downstream).</summary>
    public bool IsRaw { get; init; }

    public Token(TokenKind kind, string text, int line, int column)
    {
        Kind = kind;
        Text = text;
        Line = line;
        Column = column;
    }

    public bool Is(TokenKind kind, string text) => Kind == kind && Text == text;

    public override string ToString() => Kind switch
    {
        TokenKind.Name or TokenKind.Keyword or TokenKind.Number or TokenKind.Op => $"{Kind}({Text})",
        TokenKind.Str => $"Str({StringValue})",
        TokenKind.Bytes => $"Bytes[{BytesValue!.Length}]",
        _ => Kind.ToString(),
    };
}
