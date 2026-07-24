using PySharpLib.Lexing;

namespace PySharp.Tests.M1_Lexer;

internal static class Lex
{
    /// <summary>Tokenizes and returns the tokens without the trailing NEWLINE/EOF, for compact assertions.</summary>
    public static List<Token> Body(string source)
    {
        var tokens = Lexer.Tokenize(source);
        Assert.Equal(TokenKind.EndOfFile, tokens[^1].Kind);
        tokens.RemoveAt(tokens.Count - 1);
        while (tokens.Count > 0 && tokens[^1].Kind is TokenKind.Newline or TokenKind.Dedent)
            tokens.RemoveAt(tokens.Count - 1);
        return tokens;
    }

    /// <summary>Compact "Kind:Text" representation of all tokens (incl. NEWLINE/INDENT/DEDENT, excluding EOF).</summary>
    public static string Dump(string source)
    {
        var tokens = Lexer.Tokenize(source);
        var parts = new List<string>();
        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case TokenKind.EndOfFile:
                    break;
                case TokenKind.Newline:
                    parts.Add("NL");
                    break;
                case TokenKind.Indent:
                    parts.Add("IND");
                    break;
                case TokenKind.Dedent:
                    parts.Add("DED");
                    break;
                case TokenKind.Str:
                    parts.Add($"S:{t.StringValue}");
                    break;
                default:
                    parts.Add(t.Text);
                    break;
            }
        }
        return string.Join(" ", parts);
    }
}
