// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Lexing;

namespace PySharp.Tests.M1_Lexer;

public class OperatorAndKeywordTests
{
    [Theory]
    [InlineData("**=")]
    [InlineData("//=")]
    [InlineData(">>=")]
    [InlineData("<<=")]
    [InlineData("...")]
    [InlineData("->")]
    [InlineData(":=")]
    [InlineData("!=")]
    [InlineData("==")]
    [InlineData("**")]
    [InlineData("//")]
    public void Multi_char_operators_lex_as_single_token(string op)
    {
        var t = Assert.Single(Lex.Body(op));
        Assert.Equal(TokenKind.Op, t.Kind);
        Assert.Equal(op, t.Text);
    }

    [Fact]
    public void Keywords_are_distinguished_from_names()
    {
        var tokens = Lex.Body("if name is None");
        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal(TokenKind.Name, tokens[1].Kind);
        Assert.Equal(TokenKind.Keyword, tokens[2].Kind);
        Assert.Equal(TokenKind.Keyword, tokens[3].Kind);
    }

    [Fact]
    public void Keyword_prefix_in_identifier_is_a_name()
    {
        var t = Assert.Single(Lex.Body("iffy"));
        Assert.Equal(TokenKind.Name, t.Kind);
    }

    [Fact]
    public void Comment_is_ignored()
    {
        Assert.Equal("x = 1 NL", Lex.Dump("x = 1  # commento\n"));
    }

    [Fact]
    public void Walrus_and_arrow_in_context()
    {
        Assert.Equal("def f ( ) -> int : NL IND ( n := 10 ) NL DED",
            Lex.Dump("def f() -> int:\n    (n := 10)\n"));
    }

    [Fact]
    public void Decorator_at_sign()
    {
        Assert.Equal("@ property NL def f ( ) : NL IND pass NL DED",
            Lex.Dump("@property\ndef f():\n    pass\n"));
    }
}
