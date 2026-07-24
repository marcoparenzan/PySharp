// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M2_Parser;

public class StatementParsingTests
{
    [Theory]
    [InlineData("x = 1", "(= [x] 1)")]
    [InlineData("x = y = 1", "(= [x y] 1)")]
    [InlineData("a, b = 1, 2", "(= [(tuple a b)] (tuple 1 2))")]
    [InlineData("a, *rest = xs", "(= [(tuple a *rest)] xs)")]
    [InlineData("x += 1", "(+= x 1)")]
    [InlineData("x //= 2", "(//= x 2)")]
    [InlineData("x: int = 5", "(ann x int 5)")]
    [InlineData("x: int", "(ann x int)")]
    [InlineData("obj.attr = 1", "(= [(. obj attr)] 1)")]
    [InlineData("d['k'] = 1", "(= [([] d 'k')] 1)")]
    public void Assignments(string src, string expected)
        => Assert.Equal(expected, P.Mod(src));

    [Fact]
    public void Cannot_assign_to_literal()
        => Assert.Throws<PySyntaxError>(() => P.Mod("1 = x"));

    [Theory]
    [InlineData("pass", "(pass)")]
    [InlineData("break", "(break)")]
    [InlineData("continue", "(continue)")]
    [InlineData("return", "(return)")]
    [InlineData("return 1, 2", "(return (tuple 1 2))")]
    [InlineData("raise", "(raise)")]
    [InlineData("raise ValueError('x')", "(raise (call ValueError 'x'))")]
    [InlineData("raise A() from b", "(raise (call A) from b)")]
    [InlineData("del x, y", "(del x y)")]
    [InlineData("assert x, 'msg'", "(assert x 'msg')")]
    [InlineData("global a, b", "(global a b)")]
    [InlineData("nonlocal n", "(nonlocal n)")]
    public void Simple_statements(string src, string expected)
        => Assert.Equal(expected, P.Mod(src));

    [Theory]
    [InlineData("import os", "(import os)")]
    [InlineData("import os.path as p", "(import os.path as p)")]
    [InlineData("import a, b", "(import a b)")]
    [InlineData("from os import path", "(from os import path)")]
    [InlineData("from os import path as p, sep", "(from os import path as p sep)")]
    [InlineData("from a.b import *", "(from a.b import *)")]
    [InlineData("from . import x", "(from . import x)")]
    [InlineData("from ..pkg import y", "(from ..pkg import y)")]
    [InlineData("from paho.mqtt import client", "(from paho.mqtt import client)")]
    public void Imports(string src, string expected)
        => Assert.Equal(expected, P.Mod(src));

    [Fact]
    public void If_elif_else()
    {
        string src = string.Join("\n",
            "if a:",
            "    x = 1",
            "elif b:",
            "    x = 2",
            "else:",
            "    x = 3");
        Assert.Equal("(if a [(= [x] 1)] [(if b [(= [x] 2)] [(= [x] 3)])])", P.Mod(src));
    }

    [Fact]
    public void While_with_else()
    {
        string src = "while x:\n    f()\nelse:\n    g()";
        Assert.Equal("(while x [(expr (call f))] [(expr (call g))])", P.Mod(src));
    }

    [Fact]
    public void For_loop_with_tuple_target()
    {
        string src = "for k, v in d.items():\n    print(k)";
        Assert.Equal("(for (tuple k v) (call (. d items)) [(expr (call print k))])", P.Mod(src));
    }

    [Fact]
    public void Try_except_else_finally()
    {
        string src = string.Join("\n",
            "try:",
            "    f()",
            "except ValueError as e:",
            "    g()",
            "except:",
            "    h()",
            "else:",
            "    i()",
            "finally:",
            "    j()");
        Assert.Equal(
            "(try [(expr (call f))] (except ValueError as e [(expr (call g))]) (except [(expr (call h))]) (else [(expr (call i))]) (finally [(expr (call j))]))",
            P.Mod(src));
    }

    [Fact]
    public void With_statement()
    {
        string src = "with open('f') as fh, lock:\n    pass";
        Assert.Equal("(with ((call open 'f') as fh) lock [(pass)])", P.Mod(src));
    }

    [Fact]
    public void Function_def_with_full_signature()
    {
        string src = "def f(a, b=1, *args, c, d=2, **kw):\n    return a";
        Assert.Equal("(def f (a b=1 *args c d=2 **kw) [(return a)])", P.Mod(src));
    }

    [Fact]
    public void Function_with_keyword_only_after_bare_star()
    {
        string src = "def f(a, *, b=1):\n    pass";
        Assert.Equal("(def f (a * b=1) [(pass)])", P.Mod(src));
    }

    [Fact]
    public void Decorated_function()
    {
        string src = "@property\n@wraps(f)\ndef g():\n    pass";
        Assert.Equal("(def g () [(pass)] @[property (call wraps f)])", P.Mod(src));
    }

    [Fact]
    public void Class_def_with_bases_and_keywords()
    {
        string src = "class C(Base, metaclass=Meta):\n    x = 1";
        Assert.Equal("(class C (Base metaclass=Meta) [(= [x] 1)])", P.Mod(src));
    }

    [Fact]
    public void Generator_is_detected()
    {
        string src = "def gen():\n    yield 1\n    yield 2";
        Assert.Equal("(def* gen () [(expr (yield 1)) (expr (yield 2))])", P.Mod(src));
    }

    [Fact]
    public void Nested_function_yield_does_not_mark_outer()
    {
        string src = "def outer():\n    def inner():\n        yield 1\n    return inner";
        Assert.StartsWith("(def outer", P.Mod(src));
        Assert.Contains("(def* inner", P.Mod(src));
    }

    [Fact]
    public void Semicolon_separated_statements()
        => Assert.Equal("(block (= [x] 1) (= [y] 2))", P.Mod("x = 1; y = 2"));

    [Fact]
    public void Inline_suite()
        => Assert.Equal("(if x [(return 1)])", P.Mod("if x: return 1"));

    [Fact]
    public void Annotations_are_parsed_on_functions()
    {
        string src = "def f(x: int, y: str = 'a') -> bool:\n    return True";
        Assert.Equal("(def f (x y='a') [(return True)])", P.Mod(src));
    }

    [Fact]
    public void Paho_like_snippet_parses()
    {
        // Representative fragment of paho-mqtt's style
        string src = string.Join("\n",
            "class Client:",
            "    def __init__(self, client_id='', clean_session=True):",
            "        self._client_id = client_id or ''",
            "        self._userdata = None",
            "        self._handlers = {}",
            "",
            "    def connect(self, host, port=1883, keepalive=60):",
            "        if not host:",
            "            raise ValueError('Invalid host.')",
            "        self._host = host",
            "        self._port = port",
            "        return self._reconnect()",
            "",
            "    @property",
            "    def host(self):",
            "        return self._host");
        // Must not raise
        var dump = P.Mod(src);
        Assert.Contains("(class Client", dump);
        Assert.Contains("(def __init__", dump);
        Assert.Contains("@[property]", dump);
    }
}
