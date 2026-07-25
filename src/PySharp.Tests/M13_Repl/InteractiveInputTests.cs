// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M13_Repl;

/// <summary>REPL multi-line detection: when to keep reading, and what opens a block.</summary>
public class InteractiveInputTests
{
    [Theory]
    // complete inputs
    [InlineData("x = 1", false)]
    [InlineData("print('hi')", false)]
    [InlineData("x = (1 + 2)", false)]
    [InlineData("x = [1, 2, 3]", false)]
    [InlineData("x = {1: 2}", false)]
    [InlineData("# just a comment", false)]
    [InlineData("x = '''abc'''", false)]
    [InlineData("x = \"\"\"a\nb\"\"\"", false)]      // closed triple across lines
    [InlineData("f(\"(\")", false)]                 // bracket inside a string doesn't count
    [InlineData("x = \"unterminated", false)]        // single-line: a real error, not continuation
    // incomplete inputs → keep reading
    [InlineData("x = \"\"\"abc", true)]              // open triple string
    [InlineData("x = (", true)]                      // open paren
    [InlineData("x = [1,", true)]                    // open bracket
    [InlineData("x = {1: 2,", true)]                 // open brace
    [InlineData("x = (1 +", true)]                   // open paren across lines
    [InlineData("total = 1 + \\", true)]             // backslash line continuation
    public void IsIncomplete(string source, bool expected)
        => Assert.Equal(expected, InteractiveInput.IsIncomplete(source));

    [Theory]
    [InlineData("def f():", true)]
    [InlineData("class C:", true)]
    [InlineData("if x:", true)]
    [InlineData("elif y:", true)]
    [InlineData("else:", true)]
    [InlineData("for i in xs:", true)]
    [InlineData("while True:", true)]
    [InlineData("try:", true)]
    [InlineData("with open('f') as h:", true)]
    [InlineData("async def g():", true)]
    [InlineData("    for i in xs:", true)]            // indented still counts
    [InlineData("@decorator", true)]
    [InlineData("x = 1", false)]
    [InlineData("print(1)", false)]
    [InlineData("define = 1", false)]                 // not the 'def' keyword
    [InlineData("ifx = 2", false)]
    public void StartsBlock(string firstLine, bool expected)
        => Assert.Equal(expected, InteractiveInput.StartsBlock(firstLine));
}
