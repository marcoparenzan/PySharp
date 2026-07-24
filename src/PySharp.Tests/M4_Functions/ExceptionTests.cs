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
