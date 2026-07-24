// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M1_Lexer;

public class IndentationTests
{
    [Fact]
    public void Simple_block()
    {
        string src = "if x:\n    y = 1\n";
        Assert.Equal("if x : NL IND y = 1 NL DED", Lex.Dump(src));
    }

    [Fact]
    public void Nested_blocks()
    {
        string src = string.Join("\n",
            "def f():",
            "    if x:",
            "        return 1",
            "    return 2",
            "");
        Assert.Equal(
            "def f ( ) : NL IND if x : NL IND return 1 NL DED return 2 NL DED",
            Lex.Dump(src));
    }

    [Fact]
    public void Blank_lines_and_comments_do_not_affect_indentation()
    {
        string src = string.Join("\n",
            "if x:",
            "",
            "    # commento",
            "    y = 1",
            "");
        Assert.Equal("if x : NL IND y = 1 NL DED", Lex.Dump(src));
    }

    [Fact]
    public void Dedent_to_unknown_level_raises()
    {
        string src = "if x:\n        y = 1\n    z = 2\n";
        Assert.Throws<PySyntaxError>(() => Lex.Dump(src));
    }

    [Fact]
    public void Implicit_continuation_inside_parens_ignores_newlines()
    {
        string src = "x = (1 +\n     2)\n";
        Assert.Equal("x = ( 1 + 2 ) NL", Lex.Dump(src));
    }

    [Fact]
    public void Explicit_backslash_continuation()
    {
        string src = "x = 1 + \\\n    2\n";
        Assert.Equal("x = 1 + 2 NL", Lex.Dump(src));
    }

    [Fact]
    public void Missing_final_newline_still_closes_line_and_blocks()
    {
        Assert.Equal("if x : NL IND y = 1 NL DED", Lex.Dump("if x:\n    y = 1"));
    }
}
