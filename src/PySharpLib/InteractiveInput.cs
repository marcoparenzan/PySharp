// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib;

/// <summary>
/// Helpers for a line-based REPL to decide when the user's input is still incomplete and a
/// continuation line is needed — open triple-quoted strings, unbalanced brackets, a trailing
/// line-continuation backslash — and whether a line starts a compound (indented) block.
/// </summary>
public static class InteractiveInput
{
    private static readonly string[] BlockKeywords =
    {
        "def", "class", "if", "elif", "else", "for", "while", "try", "except", "finally", "with", "async",
    };

    /// <summary>
    /// True if <paramref name="source"/> cannot yet be a complete statement because it ends inside an
    /// open triple-quoted string, inside unbalanced <c>()</c>/<c>[]</c>/<c>{}</c>, or on a
    /// backslash line-continuation. (An unterminated *single*-line string is a real error, not
    /// incompleteness, so it returns false and lets the parser report it.)
    /// </summary>
    public static bool IsIncomplete(string source)
    {
        int depth = 0, i = 0, n = source.Length;
        while (i < n)
        {
            char c = source[i];
            if (c == '#')
            {
                while (i < n && source[i] != '\n') i++;
                continue;
            }
            if (c is '\'' or '"')
            {
                bool triple = i + 2 < n && source[i + 1] == c && source[i + 2] == c;
                if (!ConsumeString(source, ref i, c, triple))
                    return triple; // unterminated: only a triple-quoted string means "keep going"
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { if (depth > 0) depth--; }
            i++;
        }
        if (source.TrimEnd('\r', '\n', ' ', '\t').EndsWith('\\'))
            return true;
        return depth > 0;
    }

    /// <summary>True if the (first) line opens a compound statement whose body spans further lines.</summary>
    public static bool StartsBlock(string firstLine)
    {
        var t = firstLine.TrimStart();
        if (t.StartsWith('@'))
            return true; // decorator
        foreach (var kw in BlockKeywords)
        {
            if (t == kw || t.StartsWith(kw + " ") || t.StartsWith(kw + ":"))
                return true;
        }
        return false;
    }

    /// <summary>Advance <paramref name="i"/> past a string literal; returns true if it was closed.</summary>
    private static bool ConsumeString(string s, ref int i, char quote, bool triple)
    {
        i += triple ? 3 : 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\') { i += 2; continue; } // escape: skip next char
            if (triple)
            {
                if (i + 2 < s.Length && s[i] == quote && s[i + 1] == quote && s[i + 2] == quote)
                {
                    i += 3;
                    return true;
                }
                i++;
            }
            else
            {
                if (c == '\n') return false;      // single-line string ended by newline = unterminated
                if (c == quote) { i++; return true; }
                i++;
            }
        }
        return false; // reached end of input still open
    }
}
