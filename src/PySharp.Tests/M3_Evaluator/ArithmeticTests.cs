// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharp.Tests.M3_Evaluator;

public class ArithmeticTests
{
    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("10 - 3", "7")]
    [InlineData("6 * 7", "42")]
    [InlineData("7 / 2", "3.5")]
    [InlineData("7 // 2", "3")]
    [InlineData("-7 // 2", "-4")] // floor division verso -inf
    [InlineData("7 % 3", "1")]
    [InlineData("-7 % 3", "2")] // Python modulo
    [InlineData("7 % -3", "-2")]
    [InlineData("2 ** 10", "1024")]
    [InlineData("2 ** -1", "0.5")]
    [InlineData("2 ** 100", "1267650600228229401496703205376")] // int arbitrari
    [InlineData("1 << 10", "1024")]
    [InlineData("255 >> 4", "15")]
    [InlineData("0xF0 | 0x0F", "255")]
    [InlineData("0xFF & 0x0F", "15")]
    [InlineData("0xFF ^ 0x0F", "240")]
    [InlineData("~5", "-6")]
    [InlineData("-(-5)", "5")]
    [InlineData("1.5 + 2", "3.5")]
    [InlineData("10 / 4", "2.5")]
    [InlineData("3.0 * 2", "6.0")]
    [InlineData("1e3", "1000.0")]
    public void Arithmetic(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("1 < 2", "True")]
    [InlineData("2 <= 2", "True")]
    [InlineData("3 > 4", "False")]
    [InlineData("1 == 1.0", "True")]
    [InlineData("True == 1", "True")]
    [InlineData("1 != 2", "True")]
    [InlineData("1 < 2 < 3", "True")]
    [InlineData("1 < 2 > 3", "False")]
    [InlineData("'a' < 'b'", "True")]
    [InlineData("[1, 2] < [1, 3]", "True")]
    [InlineData("(1, 2) == (1, 2)", "True")]
    [InlineData("None is None", "True")]
    [InlineData("1 is not None", "True")]
    public void Comparisons(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("True and False", "False")]
    [InlineData("True or False", "True")]
    [InlineData("not True", "False")]
    [InlineData("1 and 2", "2")] // returns the last value
    [InlineData("0 or 'x'", "x")]
    [InlineData("'' or None", "None")]
    [InlineData("0 and 1", "0")] // short-circuit
    public void Boolean_logic(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Fact]
    public void Division_by_zero_raises()
    {
        var ex = Assert.Throws<PyRaise>(() => Py.Run("1 / 0"));
        Assert.Equal("ZeroDivisionError", ex.Value.Class.Name);
    }
}
