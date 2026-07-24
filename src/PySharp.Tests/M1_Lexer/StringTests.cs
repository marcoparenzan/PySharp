using PySharpLib;
using PySharpLib.Lexing;

namespace PySharp.Tests.M1_Lexer;

public class StringTests
{
    [Theory]
    [InlineData("'ciao'", "ciao")]
    [InlineData("\"ciao\"", "ciao")]
    [InlineData("''", "")]
    [InlineData("'it''s'", "it")] // two adjacent strings: the first is "it"
    public void Simple_strings(string src, string firstValue)
    {
        var tokens = Lex.Body(src);
        Assert.Equal(TokenKind.Str, tokens[0].Kind);
        Assert.Equal(firstValue, tokens[0].StringValue);
    }

    [Theory]
    [InlineData(@"'a\nb'", "a\nb")]
    [InlineData(@"'a\tb'", "a\tb")]
    [InlineData(@"'\\'", "\\")]
    [InlineData(@"'\''", "'")]
    [InlineData(@"'\x41'", "A")]
    [InlineData(@"'è'", "è")]
    [InlineData(@"'\101'", "A")] // ottale
    [InlineData(@"'\q'", "\\q")] // escape sconosciuto resta letterale
    public void Escape_sequences(string src, string expected)
    {
        var t = Assert.Single(Lex.Body(src));
        Assert.Equal(expected, t.StringValue);
    }

    [Fact]
    public void Raw_string_keeps_backslashes()
    {
        var t = Assert.Single(Lex.Body(@"r'a\nb'"));
        Assert.Equal(@"a\nb", t.StringValue);
    }

    [Fact]
    public void Triple_quoted_string_spans_lines()
    {
        var t = Assert.Single(Lex.Body("'''a\nb'''"));
        Assert.Equal("a\nb", t.StringValue);
    }

    [Fact]
    public void Triple_quoted_allows_single_quotes_inside()
    {
        var t = Assert.Single(Lex.Body("\"\"\"a\"b\"\"\""));
        Assert.Equal("a\"b", t.StringValue);
    }

    [Fact]
    public void Bytes_literal()
    {
        var t = Assert.Single(Lex.Body("b'MQTT'"));
        Assert.Equal(TokenKind.Bytes, t.Kind);
        Assert.Equal("MQTT"u8.ToArray(), t.BytesValue);
    }

    [Fact]
    public void Bytes_with_hex_escape()
    {
        var t = Assert.Single(Lex.Body(@"b'\x00\x04MQTT'"));
        Assert.Equal(new byte[] { 0, 4, (byte)'M', (byte)'Q', (byte)'T', (byte)'T' }, t.BytesValue);
    }

    [Fact]
    public void FString_is_marked_and_raw_content_preserved()
    {
        var t = Assert.Single(Lex.Body("f'x={x!r:>10}'"));
        Assert.Equal(TokenKind.Str, t.Kind);
        Assert.True(t.IsFString);
        Assert.Equal("x={x!r:>10}", t.StringValue);
    }

    [Fact]
    public void Unterminated_string_raises_syntax_error()
    {
        Assert.Throws<PySyntaxError>(() => Lexer.Tokenize("'abc"));
    }

    [Fact]
    public void Newline_in_single_quoted_string_raises()
    {
        Assert.Throws<PySyntaxError>(() => Lexer.Tokenize("'a\nb'"));
    }
}
