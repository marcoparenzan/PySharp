// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

public class ExceptionTests
{
    [Fact]
    public void Try_except_catches()
    {
        string src = """
            try:
                1 / 0
            except ZeroDivisionError:
                print('caught')
            """;
        Assert.Equal("caught\n", Py.Run(src));
    }

    [Fact]
    public void Except_binds_exception_and_message()
    {
        string src = """
            try:
                raise ValueError('bad value')
            except ValueError as e:
                print(e)
                print(e.args)
            """;
        Assert.Equal("bad value\n('bad value',)\n", Py.Run(src));
    }

    [Fact]
    public void Except_matches_base_class()
    {
        string src = """
            try:
                raise KeyError('k')
            except LookupError:
                print('lookup')
            """;
        Assert.Equal("lookup\n", Py.Run(src));
    }

    [Fact]
    public void Except_tuple_of_types()
    {
        string src = """
            for exc in (ValueError, TypeError):
                try:
                    raise exc('x')
                except (ValueError, TypeError) as e:
                    print(type(e).__name__)
            """;
        Assert.Equal("ValueError\nTypeError\n", Py.Run(src));
    }

    [Fact]
    public void Else_and_finally()
    {
        string src = """
            try:
                x = 1
            except ValueError:
                print('exc')
            else:
                print('else')
            finally:
                print('finally')
            """;
        Assert.Equal("else\nfinally\n", Py.Run(src));
    }

    [Fact]
    public void Finally_runs_on_exception()
    {
        string src = """
            def f():
                try:
                    raise ValueError('v')
                finally:
                    print('cleanup')
            try:
                f()
            except ValueError:
                print('caught')
            """;
        Assert.Equal("cleanup\ncaught\n", Py.Run(src));
    }

    [Fact]
    public void Unmatched_exception_propagates()
    {
        string src = """
            try:
                try:
                    raise TypeError('t')
                except ValueError:
                    print('wrong')
            except TypeError:
                print('outer')
            """;
        Assert.Equal("outer\n", Py.Run(src));
    }

    [Fact]
    public void Bare_raise_reraises()
    {
        string src = """
            try:
                try:
                    raise ValueError('original')
                except ValueError:
                    raise
            except ValueError as e:
                print('again:', e)
            """;
        Assert.Equal("again: original\n", Py.Run(src));
    }

    [Fact]
    public void Custom_exception_classes()
    {
        string src = """
            class AppError(Exception):
                pass
            class ConfigError(AppError):
                def __init__(self, key):
                    super().__init__('missing key: ' + key)
                    self.key = key
            try:
                raise ConfigError('host')
            except AppError as e:
                print(type(e).__name__, e, e.key)
            """;
        Assert.Equal("ConfigError missing key: host host\n", Py.Run(src));
    }

    [Fact]
    public void Raise_class_without_instance()
    {
        string src = """
            try:
                raise RuntimeError
            except RuntimeError:
                print('ok')
            """;
        Assert.Equal("ok\n", Py.Run(src));
    }

    [Fact]
    public void Exception_in_loop_continues()
    {
        string src = """
            results = []
            for x in [1, 0, 2]:
                try:
                    results.append(10 // x)
                except ZeroDivisionError:
                    results.append(-1)
            print(results)
            """;
        Assert.Equal("[10, -1, 5]\n", Py.Run(src));
    }

    [Fact]
    public void With_suppresses_when_exit_returns_true()
    {
        string src = """
            class Suppress:
                def __enter__(self):
                    return self
                def __exit__(self, t, v, tb):
                    return True
            with Suppress():
                raise ValueError('hidden')
            print('survived')
            """;
        Assert.Equal("survived\n", Py.Run(src));
    }

    [Fact]
    public void With_calls_exit_on_return_inside_body()
    {
        // regression: paho's _mid_generate does 'return' inside 'with lock:' — __exit__ must run
        string src = """
            import threading
            lock = threading.Lock()
            def gen():
                with lock:
                    return 42
            print(gen())
            print(gen())
            print(lock.locked())
            """;
        Assert.Equal("42\n42\nFalse\n", Py.Run(src));
    }

    [Fact]
    public void Assert_raises_assertion_error()
    {
        string src = """
            try:
                assert 1 == 2, 'math is broken'
            except AssertionError as e:
                print(e)
            """;
        Assert.Equal("math is broken\n", Py.Run(src));
    }
}

/// <summary>PEP 654 BaseExceptionGroup/ExceptionGroup (real CPython 3.11+, matching this project's
/// declared 3.12 compatibility). Found via anyio's own `_backends/_asyncio.py` (an httpx transitive
/// dependency's cancel-scope teardown: `raise BaseExceptionGroup("unhandled errors in a TaskGroup",
/// self._exceptions)`, and `.split(condition)` to separate anyio's own cancellation exceptions from
/// genuine errors). `except*` syntax is NOT supported (a separate, out-of-scope parser gap — anyio
/// itself only uses `.split()`, not `except*`). See FASTAPI_PLAN.md Phase 4.</summary>
public class ExceptionGroupTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Construction_auto_upgrades_to_ExceptionGroup_when_every_item_is_an_Exception()
        => Assert.Equal("ExceptionGroup\nmultiple errors\n2\nTrue\nTrue", Run("""
            eg = BaseExceptionGroup("multiple errors", [ValueError("a"), TypeError("b")])
            print(type(eg).__name__)
            print(eg.message)
            print(len(eg.exceptions))
            print(isinstance(eg, ExceptionGroup))
            print(isinstance(eg, BaseExceptionGroup))
            """));

    [Fact]
    public void Split_partitions_exceptions_by_type_and_returns_None_for_an_empty_side()
        => Assert.Equal("a\n1\nb\n1\nNone\n2", Run("""
            eg = BaseExceptionGroup("multiple errors", [ValueError("a"), TypeError("b")])
            matched, rest = eg.split(ValueError)
            print(matched.exceptions[0].args[0])
            print(len(matched.exceptions))
            print(rest.exceptions[0].args[0])
            print(len(rest.exceptions))

            matched2, rest2 = eg.split(KeyError)
            print(matched2)
            print(len(rest2.exceptions))
            """));

    [Fact]
    public void Raise_and_catch_via_plain_except_preserves_message_and_exceptions()
        => Assert.Equal("caught: multiple errors 2", Run("""
            eg = BaseExceptionGroup("multiple errors", [ValueError("a"), TypeError("b")])
            try:
                raise eg
            except BaseExceptionGroup as e:
                print("caught:", e.message, len(e.exceptions))
            """));
}

/// <summary>ABC structural duck-typing for isinstance(): real CPython recognizes
/// types.CoroutineType/GeneratorType instances as virtual subclasses of
/// collections.abc.Coroutine/Generator/Awaitable without needing explicit inheritance. Found via
/// anyio's `abc/_tasks.py`'s `call_for_coroutine`: `isinstance(coro, Coroutine)` (imported from
/// collections.abc), verifying that calling an `async def` function actually produced a real
/// coroutine object. See FASTAPI_PLAN.md Phase 4.</summary>
public class CoroutineAbcDuckTypingTests
{
    [Fact]
    public void A_real_coroutine_object_satisfies_Coroutine_and_Awaitable_but_not_a_plain_int()
        => Assert.Equal("True\nTrue\nFalse\nTrue\n", Py.Run("""
            from collections.abc import Coroutine, Generator, Awaitable

            async def f():
                return 1

            def g():
                yield 1

            coro = f()
            print(isinstance(coro, Coroutine))
            print(isinstance(coro, Awaitable))
            print(isinstance(5, Coroutine))

            gen = g()
            print(isinstance(gen, Generator))
            coro.close()
            """));
}
