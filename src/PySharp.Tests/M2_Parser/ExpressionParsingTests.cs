// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Parsing;

namespace PySharp.Tests.M2_Parser;

internal static class P
{
    public static string Expr(string src) => AstDumper.Dump(Parser.ParseExpression(src));
    public static string Mod(string src) => AstDumper.Dump(Parser.Parse(src));
}

public class ExpressionParsingTests
{
    [Theory]
    [InlineData("1 + 2 * 3", "(+ 1 (* 2 3))")]
    [InlineData("(1 + 2) * 3", "(* (+ 1 2) 3)")]
    [InlineData("2 ** 3 ** 2", "(** 2 (** 3 2))")] // right-assoc
    [InlineData("-2 ** 2", "(- (** 2 2))")] // unario lega meno di **
    [InlineData("1 + 2 - 3", "(- (+ 1 2) 3)")]
    [InlineData("7 // 2 % 3", "(% (// 7 2) 3)")]
    [InlineData("1 | 2 & 3 ^ 4", "(| 1 (^ (& 2 3) 4))")]
    [InlineData("1 << 2 + 3", "(<< 1 (+ 2 3))")]
    [InlineData("~x", "(~ x)")]
    public void Arithmetic_precedence(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("a < b <= c", "(cmp a < b <= c)")]
    [InlineData("x in y", "(cmp x in y)")]
    [InlineData("x not in y", "(cmp x not in y)")]
    [InlineData("x is None", "(cmp x is None)")]
    [InlineData("x is not None", "(cmp x is not None)")]
    public void Comparisons(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("a and b or not c", "(or (and a b) (not c))")]
    [InlineData("a or b or c", "(or a b c)")]
    public void Boolean_operators(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("x if c else y", "(ifexp c x y)")]
    [InlineData("lambda x, y=1: x + y", "(lambda (x y=1) (+ x y))")]
    [InlineData("lambda *args, **kw: 0", "(lambda (*args **kw) 0)")]
    public void Ternary_and_lambda(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("f(1, x=2)", "(call f 1 x=2)")]
    [InlineData("f(*a, **b)", "(call f *a **b)")]
    [InlineData("obj.method(1).attr", "(. (call (. obj method) 1) attr)")]
    [InlineData("d['k']", "([] d 'k')")]
    [InlineData("a[1:2:3]", "([] a (slice 1 2 3))")]
    [InlineData("a[::2]", "([] a (slice _ _ 2))")]
    [InlineData("a[1:]", "([] a (slice 1 _ _))")]
    [InlineData("d[k1, k2]", "([] d (tuple k1 k2))")]
    public void Calls_attributes_subscripts(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("[1, 2, 3]", "(list 1 2 3)")]
    [InlineData("(1, 2)", "(tuple 1 2)")]
    [InlineData("(1,)", "(tuple 1)")]
    [InlineData("()", "(tuple )")]
    [InlineData("{1, 2}", "(set 1 2)")]
    [InlineData("{'a': 1, 'b': 2}", "(dict 'a':1 'b':2)")]
    [InlineData("{}", "(dict )")]
    [InlineData("{**base, 'k': 1}", "(dict **base 'k':1)")]
    [InlineData("[*a, *b]", "(list *a *b)")]
    public void Displays(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Theory]
    [InlineData("[x * 2 for x in xs]", "(listcomp (* x 2) (for x in xs))")]
    [InlineData("[x for x in xs if x > 0]", "(listcomp x (for x in xs if (cmp x > 0)))")]
    [InlineData("{k: v for k, v in items}", "(dictcomp k:v (for (tuple k v) in items))")]
    [InlineData("{x for x in xs}", "(setcomp x (for x in xs))")]
    [InlineData("(x for x in xs)", "(generatorcomp x (for x in xs))")]
    [InlineData("[x for x in xs for y in x]", "(listcomp x (for x in xs) (for y in x))")]
    public void Comprehensions(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));

    [Fact]
    public void Generator_argument_in_call()
        => Assert.Equal("(call sum (generatorcomp x (for x in xs)))", P.Expr("sum(x for x in xs)"));

    [Fact]
    public void Walrus_expression()
        => Assert.Equal("(:= n 10)", P.Expr("(n := 10)"));

    [Fact]
    public void String_concatenation()
        => Assert.Equal("'abc'", P.Expr("'a' 'b' 'c'"));

    [Theory]
    [InlineData("f'x={x}'", "(fstr 'x=' {x})")]
    [InlineData("f'{x!r}'", "(fstr {x!r})")]
    [InlineData("f'{x:>10}'", "(fstr {x:'>10'})")]
    [InlineData("f'{x:{w}}'", "(fstr {x:{w}})")]
    [InlineData("f'{{literal}}'", "(fstr '{literal}')")]
    [InlineData("f'{d[\"k\"]}'", "(fstr {([] d 'k')})")]
    [InlineData("f'a{1 + 2}b'", "(fstr 'a' {(+ 1 2)} 'b')")]
    public void FStrings(string src, string expected)
        => Assert.Equal(expected, P.Expr(src));
}
