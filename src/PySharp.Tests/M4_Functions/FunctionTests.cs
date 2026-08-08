// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

public class FunctionTests
{
    [Fact]
    public void Basic_function_and_return()
    {
        Assert.Equal("7\n", Py.Run("def add(a, b):\n    return a + b\nprint(add(3, 4))"));
    }

    [Fact]
    public void Default_arguments()
    {
        string src = """
            def greet(name, greeting='hello'):
                return greeting + ' ' + name
            print(greet('x'))
            print(greet('x', 'ciao'))
            print(greet('x', greeting='hey'))
            """;
        Assert.Equal("hello x\nciao x\nhey x\n", Py.Run(src));
    }

    [Fact]
    public void Star_args_and_kwargs()
    {
        string src = """
            def f(a, *args, **kwargs):
                return (a, args, sorted(kwargs.items()))
            print(f(1, 2, 3, x=10, y=20))
            """;
        Assert.Equal("(1, (2, 3), [('x', 10), ('y', 20)])\n", Py.Run(src));
    }

    [Fact]
    public void Keyword_only_arguments()
    {
        string src = """
            def f(a, *, b, c=3):
                return a + b + c
            print(f(1, b=2))
            """;
        Assert.Equal("6\n", Py.Run(src));
    }

    [Fact]
    public void Call_with_star_unpacking()
    {
        string src = """
            def f(a, b, c):
                return a * 100 + b * 10 + c
            args = [1, 2]
            print(f(*args, 3))
            print(f(**{'a': 9, 'b': 8, 'c': 7}))
            """;
        Assert.Equal("123\n987\n", Py.Run(src));
    }

    [Fact]
    public void Closures_capture_environment()
    {
        string src = """
            def counter():
                n = 0
                def inc():
                    nonlocal n
                    n += 1
                    return n
                return inc
            c = counter()
            print(c(), c(), c())
            """;
        Assert.Equal("1 2 3\n", Py.Run(src));
    }

    [Fact]
    public void Lambda_expressions()
    {
        Assert.Equal("25", Py.Eval("(lambda x: x * x)(5)"));
        Assert.Equal("3", Py.Eval("(lambda a, b=2: a + b)(1)"));
    }

    [Fact]
    public void Recursion()
    {
        string src = """
            def fib(n):
                return n if n < 2 else fib(n - 1) + fib(n - 2)
            print(fib(10))
            """;
        Assert.Equal("55\n", Py.Run(src));
    }

    /// <summary>Runaway recursion raises a catchable RecursionError instead of crashing the
    /// process — Interp.Call's real recursion-depth guard (matching CPython's default
    /// sys.getrecursionlimit() of 1000), backed by running on a real large-stack thread
    /// (PyEngine.Run/BigStack) so genuinely deep-but-legitimate recursion still succeeds. Found
    /// via a real corpus regression: `Foo.__repr__ = Foo.__str__` (recursion.py) combined with
    /// object.__str__'s new real default (calling __repr__) turned an uncatchable C# stack
    /// overflow into what real CPython also does here — a clean RecursionError.</summary>
    [Fact]
    public void Runaway_recursion_raises_a_catchable_RecursionError()
        => Assert.Equal("caught\n", Py.Run("""
            def rec(n):
                return rec(n + 1)
            try:
                rec(0)
            except RecursionError:
                print("caught")
            """));

    [Fact]
    public void Decorators_wrap_functions()
    {
        string src = """
            def twice(fn):
                def wrapper(*args, **kwargs):
                    return fn(*args, **kwargs) * 2
                return wrapper

            @twice
            def val():
                return 21
            print(val())
            """;
        Assert.Equal("42\n", Py.Run(src));
    }

    [Fact]
    public void Decorator_with_arguments()
    {
        string src = """
            def repeat(n):
                def deco(fn):
                    def wrapper():
                        return fn() * n
                    return wrapper
                return deco

            @repeat(3)
            def s():
                return 'ab'
            print(s())
            """;
        Assert.Equal("ababab\n", Py.Run(src));
    }

    [Fact]
    public void Functions_are_first_class()
    {
        string src = """
            def apply(fn, x):
                return fn(x)
            print(apply(len, 'ciao'))
            fns = [str.upper, str.lower]
            print(fns[0]('hi'))
            """;
        Assert.Equal("4\nHI\n", Py.Run(src));
    }

    [Fact]
    public void Default_evaluated_once_at_def_time()
    {
        string src = """
            x = 10
            def f(v=x):
                return v
            x = 99
            print(f())
            """;
        Assert.Equal("10\n", Py.Run(src));
    }
}
