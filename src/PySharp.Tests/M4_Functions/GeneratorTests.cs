// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

public class GeneratorTests
{
    [Fact]
    public void Basic_generator()
    {
        string src = """
            def gen():
                yield 1
                yield 2
                yield 3
            print(list(gen()))
            """;
        Assert.Equal("[1, 2, 3]\n", Py.Run(src));
    }

    [Fact]
    public void Generator_with_loop_and_state()
    {
        string src = """
            def countdown(n):
                while n > 0:
                    yield n
                    n -= 1
            print(list(countdown(4)))
            """;
        Assert.Equal("[4, 3, 2, 1]\n", Py.Run(src));
    }

    [Fact]
    public void Generator_is_lazy_with_next()
    {
        string src = """
            def gen():
                print('a')
                yield 1
                print('b')
                yield 2
            g = gen()
            print('start')
            print(next(g))
            print(next(g))
            """;
        Assert.Equal("start\na\n1\nb\n2\n", Py.Run(src));
    }

    [Fact]
    public void Generator_stopiteration()
    {
        string src = """
            def gen():
                yield 1
            g = gen()
            print(next(g))
            try:
                next(g)
            except StopIteration:
                print('done')
            print(next(g, 'default'))
            """;
        Assert.Equal("1\ndone\ndefault\n", Py.Run(src));
    }

    [Fact]
    public void Yield_from_delegates()
    {
        string src = """
            def inner():
                yield 1
                yield 2
            def outer():
                yield 0
                yield from inner()
                yield 3
            print(list(outer()))
            """;
        Assert.Equal("[0, 1, 2, 3]\n", Py.Run(src));
    }

    [Fact]
    public void Return_ends_generator()
    {
        string src = """
            def gen():
                yield 1
                return
                yield 2
            print(list(gen()))
            """;
        Assert.Equal("[1]\n", Py.Run(src));
    }

    [Fact]
    public void Generator_used_in_for_loop_and_sum()
    {
        string src = """
            def squares(n):
                for i in range(n):
                    yield i * i
            total = 0
            for s in squares(5):
                total += s
            print(total, sum(squares(5)))
            """;
        Assert.Equal("30 30\n", Py.Run(src));
    }

    [Fact]
    public void Generators_are_independent()
    {
        string src = """
            def gen():
                yield 1
                yield 2
            a = gen()
            b = gen()
            print(next(a), next(b), next(a), next(b))
            """;
        Assert.Equal("1 1 2 2\n", Py.Run(src));
    }

    [Fact]
    public void Send_resumes_with_the_sent_value_as_the_yield_expressions_result()
    {
        // Real gen.send(value): resumes the generator with `value` as the result of its currently-
        // suspended 'yield' expression — found via httpx's real sync auth flow (`_client.py`'s
        // `_send_handling_auth`: `auth_flow.send(response)`), previously unconditionally rejected
        // ("not supported in PySharp v1").
        string src = """
            def echo():
                x = yield 'first'
                print('got', x)
                yield 'after'
            g = echo()
            print(next(g))
            print(g.send('hello'))
            """;
        Assert.Equal("first\ngot hello\nafter\n", Py.Run(src));
    }

    [Fact]
    public void Send_a_non_None_value_to_a_just_started_generator_raises_TypeError()
    {
        string src = """
            def gen():
                yield 1
            g = gen()
            try:
                g.send('nope')
            except TypeError as e:
                print('caught:', e)
            """;
        Assert.Equal("caught: can't send non-None value to a just-started generator\n", Py.Run(src));
    }

    [Fact]
    public void Generator_return_value_becomes_StopIteration_value()
    {
        // Real CPython: a generator's `return value` becomes the StopIteration it raises on
        // exhaustion's `.value` attribute (None for a bare `return`/falling off the end).
        string src = """
            def gen():
                yield 1
                return 'done'
            g = gen()
            print(next(g))
            try:
                next(g)
            except StopIteration as e:
                print(e.value, e.args)

            def bare():
                yield 1
            g2 = bare()
            next(g2)
            try:
                next(g2)
            except StopIteration as e:
                print(e.value, e.args)
            """;
        Assert.Equal("1\ndone ('done',)\nNone ()\n", Py.Run(src));
    }
}
