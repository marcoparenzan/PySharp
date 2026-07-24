// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M3_Evaluator;

public class StringEvalTests
{
    [Theory]
    [InlineData("'abc' + 'def'", "abcdef")]
    [InlineData("'ab' * 3", "ababab")]
    [InlineData("'hello'[1]", "e")]
    [InlineData("'hello'[-1]", "o")]
    [InlineData("'hello'[1:4]", "ell")]
    [InlineData("'hello'[::-1]", "olleh")]
    [InlineData("len('ciao')", "4")]
    [InlineData("'ell' in 'hello'", "True")]
    public void Basic_operations(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("'HeLLo'.upper()", "HELLO")]
    [InlineData("'HeLLo'.lower()", "hello")]
    [InlineData("'  x  '.strip()", "x")]
    [InlineData("'xxayy'.strip('xy')", "a")]
    [InlineData("'a,b,c'.split(',')", "['a', 'b', 'c']")]
    [InlineData("'a b  c'.split()", "['a', 'b', 'c']")]
    [InlineData("'a,b,c'.split(',', 1)", "['a', 'b,c']")]
    [InlineData("'-'.join(['a', 'b'])", "a-b")]
    [InlineData("'hello'.replace('l', 'L')", "heLLo")]
    [InlineData("'hello'.startswith('he')", "True")]
    [InlineData("'hello'.endswith(('x', 'lo'))", "True")]
    [InlineData("'hello'.find('ll')", "2")]
    [InlineData("'hello'.find('z')", "-1")]
    [InlineData("'aabb'.count('a')", "2")]
    [InlineData("'42'.zfill(5)", "00042")]
    [InlineData("'abc'.capitalize()", "Abc")]
    [InlineData("'a=b'.partition('=')", "('a', '=', 'b')")]
    [InlineData("'123'.isdigit()", "True")]
    [InlineData("'ab1'.isalpha()", "False")]
    public void Methods(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("f'{1 + 2}'", "3")]
    [InlineData("f'x={10}!'", "x=10!")]
    [InlineData("f'{3.14159:.2f}'", "3.14")]
    [InlineData("f'{42:>6}'", "    42")]
    [InlineData("f'{\"hi\":<5}|'", "hi   |")]
    [InlineData("f'{255:x}'", "ff")]
    [InlineData("f'{255:#06x}'", "0x00ff")]
    [InlineData("f'{1000000:,}'", "1,000,000")]
    [InlineData("f'{\"s\"!r}'", "'s'")]
    public void FStrings(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("'%s-%s' % ('a', 'b')", "a-b")]
    [InlineData("'%d items' % 5", "5 items")]
    [InlineData("'%05d' % 42", "00042")]
    [InlineData("'%.2f' % 3.14159", "3.14")]
    [InlineData("'%x' % 255", "ff")]
    [InlineData("'%r' % 'x'", "'x'")]
    [InlineData("'%(name)s!' % {'name': 'Bob'}", "Bob!")]
    [InlineData("'100%%' % ()", "100%")]
    public void Percent_formatting(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("'{} {}'.format(1, 2)", "1 2")]
    [InlineData("'{1} {0}'.format('a', 'b')", "b a")]
    [InlineData("'{name}!'.format(name='X')", "X!")]
    [InlineData("'{:.1f}'.format(2.55)", "2.5")]
    [InlineData("'{!r}'.format('s')", "'s'")]
    public void Format_method(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("b'abc' + b'de'", "b'abcde'")]
    [InlineData("b'abc'[0]", "97")]
    [InlineData("b'\\x01\\x02'.hex()", "0102")]
    [InlineData("'ciao'.encode()", "b'ciao'")]
    [InlineData("b'ciao'.decode()", "ciao")]
    [InlineData("len(b'\\x00\\x01')", "2")]
    [InlineData("bytes([77, 81])", "b'MQ'")]
    [InlineData("list(b'AB')", "[65, 66]")]
    public void Bytes(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));

    [Theory]
    [InlineData("str(42)", "42")]
    [InlineData("str(3.5)", "3.5")]
    [InlineData("str(True)", "True")]
    [InlineData("str(None)", "None")]
    [InlineData("int('42')", "42")]
    [InlineData("int('ff', 16)", "255")]
    [InlineData("int(3.9)", "3")]
    [InlineData("int(-3.9)", "-3")]
    [InlineData("float('2.5')", "2.5")]
    [InlineData("repr('a\\nb')", "'a\\nb'")]
    [InlineData("ord('A')", "65")]
    [InlineData("chr(65)", "A")]
    [InlineData("hex(255)", "0xff")]
    [InlineData("bin(5)", "0b101")]
    public void Conversions(string expr, string expected)
        => Assert.Equal(expected, Py.Eval(expr));
}
