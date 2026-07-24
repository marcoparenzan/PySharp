// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Lexing;

namespace PySharp.Tests.M1_Lexer;

public class NumberTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("1_000_000")]
    [InlineData("0x1F")]
    [InlineData("0o755")]
    [InlineData("0b1010")]
    [InlineData("3.14")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1e10")]
    [InlineData("1.5e-3")]
    [InlineData("2E+8")]
    public void Single_number_token(string src)
    {
        var t = Assert.Single(Lex.Body(src));
        Assert.Equal(TokenKind.Number, t.Kind);
        Assert.Equal(src, t.Text);
    }

    [Fact]
    public void Number_followed_by_dot_attribute_is_not_float()
    {
        var tokens = Lex.Body("1 .real");
        Assert.Equal(3, tokens.Count);
        Assert.Equal("1", tokens[0].Text);
        Assert.Equal(".", tokens[1].Text);
        Assert.Equal("real", tokens[2].Text);
    }

    [Fact]
    public void Arithmetic_expression_token_sequence()
    {
        Assert.Equal("1 + 2 * 3 NL", Lex.Dump("1 + 2 * 3"));
    }
}
